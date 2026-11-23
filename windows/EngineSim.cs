namespace RevGame;

/// <summary>
/// เครื่องยนต์ 1 สูบ 4 จังหวะตามสเปกที่ออกแบบ (EngineModel): ฟิสิกส์รอบเครื่อง + ความเสียหาย + ไดโน + เสียง
/// เสียงไอเสียมาจากพัลส์ของเครื่องจริงที่อัดไว้ (Assets/engine_ref.wav) วางทีละรอบตามมุมข้อเหวี่ยง
/// (pitch-synchronous overlap-add) รอบเครื่องเปลี่ยนแค่ "ระยะห่าง" ระหว่างพัลส์ เสียงท่อจึงยังเป็นเสียงจริง
/// ทั้งหมดคำนวณในเธรดเสียง (เรียก Render) UI อ่าน/เขียนผ่านฟิลด์ volatile เท่านั้น
/// </summary>
public sealed class EngineSim
{
    public const int SampleRate = 44100;
    private const float Fs = SampleRate;

    // ---------- อินพุตจาก UI ----------
    public volatile float ThrottleInput;        // 0..1
    public volatile bool StarterHeld;
    public volatile bool Ignition = true;
    public volatile float Volume = 0.8f;
    public volatile float IdleBoostK = 12f;      // เสริมเสียงเดินเบา (ตั้งในหน้าตั้งค่า) x(1+K) ที่รอบเดินเบา
    public volatile int GearInput;              // 0 = เกียร์ว่าง, 1..4
    public volatile bool ClutchEnabled = true;  // ระบบครัตช์มือ: ปิด = ไม่มีคันครัตช์ (คลัตช์แรงเหวี่ยงอย่างเดียว) เปลี่ยนเกียร์ได้ทันที
    public volatile float ClutchInput;          // คันครัตช์มือ (กด Shift): 0 = ปล่อย, 1 = กำสุด (เฉพาะรถเกียร์ธรรมดา)
    private float _lever;                       // ตำแหน่งคันครัตช์จริง (ขยับตามมือ ปล่อยช้ากว่ากำ)
    private float _cfLock;                      // แรงจับของคลัตช์แรงเหวี่ยงก่อนคิดคันครัตช์
    public float ClutchLever => _lever;
    // สถานะคลัตช์/ยาง (UI อ่าน)
    private volatile float _slipRpm, _tracUse, _driveForce, _gripMax;
    private volatile bool _spin;
    public float ClutchSlipRpm => _slipRpm;     // รอบเครื่อง - รอบด้านล้อ (rpm) ยิ่งมากคลัตช์ยิ่งลื่น
    public float TractionUse => _tracUse;       // แรงที่คลัตช์พยายามส่งเทียบแรงเกาะยางหลัง (1.0 = เต็มที่ เกินแล้วล้อฟรี)
    public bool WheelSpin => _spin;             // ยางลื่น/ล้อฟรี
    public float DriveForce => _driveForce;     // แรงขับที่ล้อหลัง (N)
    public float GripMax => _gripMax;           // แรงเกาะยางหลังสูงสุด (N)
    public float CentrifugalLock => _cfLock;
    public volatile float SlopePct;             // ความชันถนน % (+ = ขึ้นเนิน)
    public volatile float BrakeInput;           // 0..1
    private volatile EngineModel? _modelPending;
    private volatile bool _roadReset;
    private volatile bool _dynoRequest;

    // ---------- ค่าที่ UI อ่าน ----------
    private volatile EngineModel _model;
    private volatile float _rpmPhys;
    private volatile float _thr;
    private volatile bool _cut;
    private volatile float _speed;              // m/s
    private volatile float _dist;               // m
    private volatile int _gear;
    private EngineModel.EcuState _ecu;
    /// <summary>ค่า ECU ปัจจุบัน (AFR, องศาจุด, หัวฉีด ฯลฯ)</summary>
    public EngineModel.EcuState Ecu => _ecu;
    private volatile float _clutchLock;         // 0..1 คลัตช์จับแน่นแค่ไหน (UI ใช้แสดง)
    private volatile float _cvtS;               // ตำแหน่งชุดขับหน้า CVT 0..1 (รถออโต้)
    public EngineModel Model => _model;
    public float Rpm => _rpmPhys;
    public float Throttle => _thr;
    public bool LimiterActive => _cut;
    public bool SoftCutActive => _softK > 0f && !_cut;
    public float SpeedKmh => _speed * 3.6f;
    public float DistanceM => _dist;
    public int Gear => _gear;
    public float ClutchLock => _clutchLock;
    /// <summary>เริ่มวัดระยะ/ความเร็วใหม่ (รถหยุดนิ่ง)</summary>
    public void RoadReset() => _roadReset = true;
    public bool Running => Ignition && _rpmPhys > 700f;
    public bool IsAuto => _model.D.Auto == 1;
    /// <summary>ตำแหน่งชุดขับหน้า CVT 0..1 และอัตราทดสายพานตอนนี้ (รถออโต้)</summary>
    public float CvtPosition => _cvtS;
    public float CvtRatio => Cvt.Ratio(_cvtS);
    public float RedlineRpm => _model.D.Limiter - 600f;
    public float LimiterRpm => _model.D.Limiter;

    // ---------- ไดโน ----------
    public const int DynoBins = (int)(EngineModel.MaxRpm / 100f) + 1;
    public const float DynoStartRpm = 3000f;
    private const float DynoRate = 2000f;       // rpm/วินาที
    /// <summary>แรงบิดที่วัดได้ (N·m) ทุก 100 rpm; NaN = ไม่มีข้อมูล</summary>
    public readonly float[] DynoTq = new float[DynoBins];
    private readonly float[] _dynoSum = new float[DynoBins];
    private readonly int[] _dynoCnt = new int[DynoBins];
    private volatile int _dynoPhase;            // 0 = ไม่วิ่ง, 1 = เร่งขึ้นจุดเริ่ม, 2 = กำลังกวาดรอบ
    private volatile int _dynoVersion;
    private volatile bool _dynoFailed;
    private float _dynoRpm;
    public bool DynoRunning => _dynoPhase != 0;
    public int DynoVersion => _dynoVersion;
    public bool DynoFailed => _dynoFailed;

    /// <summary>คลื่นเสียงล่าสุดสำหรับวาดสโคป (UI อ่านอย่างเดียว)</summary>
    public readonly float[] Scope = new float[2048];
    private int _scopeW;
    public int ScopeWritePos => _scopeW;

    // ---------- สถานะฟิสิกส์ ----------
    private const int Block = 32;               // อัปเดตฟิสิกส์ทุก 32 แซมเปิล (~0.7 ms)
    private const float BlockDt = Block / Fs;
    private int _blockPos;
    private float _idleInt;
    private float _huntPh, _huntRate, _surgePh, _brkPh;
    // ความร้อน / น้ำมัน / ลูกสูบสึก
    private float _engT = 35f, _oilT = 35f, _oilVisc = 2.2f, _wear, _thermMult = 1f, _effLim;
    private bool _seized; private string _seizeCause = "";
    private const float AmbientC = 30f;
    public float EngT => _engT;
    public float OilT => _oilT;
    public float OilVisc => _oilVisc;
    public float Wear => _wear;
    public bool Seized => _seized;
    public string SeizeCause => _seizeCause;
    /// <summary>ซ่อมเครื่อง: เปลี่ยนลูก/น้ำมันใหม่ ความร้อนกลับเป็นอุณหภูมิห้อง</summary>
    public void Repair() { _wear = 0f; _seized = false; _seizeCause = ""; _engT = AmbientC + 5f; _oilT = AmbientC + 5f; }
    private float _idleWobble;                  // รอบเดินเบาที่เหวี่ยงขึ้นลงช้า ๆ (แคมโอเวอร์แลปมาก/กำลังอัดต่ำ)
    private float _load;                        // 0..1 ปริมาณไอดีต่อรอบ
    public float KnockNow => _knock;
    public float LoadNow => _load;
    private float _thrEff;
    /// <summary>ตำแหน่งลิ้นเร่งจริง (TPS 0..1) รวมที่ระบบคุมรอบเดินเบาเปิดให้</summary>
    public float ThrEffNow => _thrEff;
    private float _knock;                       // 0..1 การน็อกตอนนี้
    private float _softK;                       // 0..1 ความแรงของ soft cut ตอนนี้
    private bool _fuelOn;
    private bool _starterActive;

    // ---------- สถานะรอบการทำงาน ----------
    private const float EvoDeg = 130f;          // วาล์วไอเสียเปิด: จุดที่พัลส์ไอเสียออกจากท่อ
    private float _phase;                       // องศาข้อเหวี่ยง 0..720 (0 = ศูนย์ตายบนจังหวะระเบิด)
    private float _rpmAudio;
    private bool _cycleFiring;
    private float _cycleVar = 1f;
    private float _cycAmp;
    private float _cycKnock;
    private uint _rng = 0x1234ABCDu;

    // ---------- เสียงจริงที่แกะจากคลิป: พัลส์ไอเสียทีละรอบ ----------
    private readonly EnginePulses _pulses;           // แบบ A: พัลส์จริงตัดจากคลิป เรียงต่อเนื่อง
    private const int OlaMask = 8191;
    private readonly float[] _ola = new float[OlaMask + 1];
    private int _olaR;
    private const int _olaLead = 3200;          // หน่วงให้วางส่วนก่อนหน้าพัลส์ได้ (ยืดสูงสุดของพัลส์ที่อัดมา)
    private float _dcX, _dcY;

    /// <summary>ข้อความผิดพลาดถ้าโหลดไฟล์เสียงไม่ได้ (null = ปกติ)</summary>
    public string? LoadError { get; }

    public EngineSim(EngineDesign design)
    {
        _model = new EngineModel(design);
        try
        {
            _pulses = EnginePulses.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "source", "engine_ref"), SampleRate);
        }
        catch (Exception ex)
        {
            _pulses = new EnginePulses();
            LoadError = "โหลดเสียงเครื่องไม่ได้ (Assets/source): " + ex.Message;
        }
        Array.Fill(DynoTq, float.NaN);
    }

    // ====================================================================
    //  คำสั่งจาก UI
    // ====================================================================

    /// <summary>ประกอบเครื่อง/ชุดขับใหม่ตามแบบ</summary>
    public void Assemble(EngineDesign design)
    {
        _modelPending = new EngineModel(design);
        _roadReset = true;
    }

    /// <summary>เริ่มวิ่งไดโน (เครื่องต้องติดอยู่)</summary>
    public void StartDyno()
    {
        if (Running && _dynoPhase == 0) _dynoRequest = true;
    }

    // ====================================================================
    //  ฟิสิกส์
    // ====================================================================

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

    private void StepPhysics(float dt)
    {
        var pending = _modelPending;
        if (pending != null)
        {
            _model = pending;
            _modelPending = null;
        }
        var m = _model;

        if (_roadReset)
        {
            _roadReset = false;
            _speed = 0f;
            _dist = 0f;
            _cvtS = 0f;
            _tcSmooth = 0f;
            _dynoPhase = 0;
        }
        if (_dynoRequest)
        {
            _dynoRequest = false;
            Array.Clear(_dynoSum);
            Array.Clear(_dynoCnt);
            Array.Fill(DynoTq, float.NaN);
            _dynoFailed = false;
            _dynoPhase = 1;
        }

        float rpm = _rpmPhys;
        bool ign = Ignition;
        if (_dynoPhase != 0 && !ign)
        {
            _dynoPhase = 0;
            _dynoFailed = true;
            _dynoVersion++;
        }

        // ลิ้นเร่งตามมือบิด (หน่วงเล็กน้อย) - วิ่งไดโนเปิดคันเร่งเอง
        float target = Clamp(ThrottleInput, 0f, 1f);
        if (_dynoPhase == 1) target = rpm < DynoStartRpm - 400f ? 0.35f : 0.08f;
        if (_dynoPhase == 2) target = 1f;
        float thr = _thr;
        float k = target > thr ? 12f : 20f;
        thr += (target - thr) * MathF.Min(1f, k * dt);
        _thr = thr;

        // ระบบรอบเดินเบา (PI) คุมให้นิ่งที่ IdleRpm  (ปรับตามขนาดเรือน ให้ไวเท่ากันทุกแบบ)
        float idleThr = 0f;
        if (ign)
        {
            // เหวี่ยง: เดินเบายิ่งแคมแรง (โอเวอร์แลปมาก) ยิ่งไม่นิ่ง เหมือนเครื่องแต่งจริง ฟิลเตอร์สัญญาณสุ่มให้ช้า ~0.5 วินาที
            float wobAmp = (25f + 420f * m.Lope + 600f * m.LowCrMisfire) / (m.IdleSteady * m.IdleSteady);
            _idleWobble += (Noise() * wobAmp * 2.2f - _idleWobble) * 0.0015f;
            float huntOff = 0f;
            if (m.Hunt > 0f)
            {
                _huntRate += (Noise() * 0.5f - _huntRate) * 0.002f;
                _huntPh += dt * 6.2832f * (1.3f + 0.4f * _huntRate);       // คลิปอ้างอิง: จังหวะหอบ ~1.3 ครั้ง/วินาที (คาบ ~0.75 วิ)
                huntOff = m.Hunt * 210f * MathF.Sin(_huntPh) + m.Hunt * 70f * MathF.Sin(_huntPh * 2.7f);
            }
            float err = m.IdleRpm + _idleWobble + huntOff - rpm;
            _idleInt = Clamp(_idleInt + err * dt * 0.00008f, 0f, 0.06f);
            idleThr = Clamp(0.022f + err * 0.00006f + _idleInt, 0f, 0.10f) * (24500f / m.AirK);
        }
        float eff = idleThr + thr * (1f - idleThr);
        _thrEff = eff;
        _load = m.Load(eff, rpm);
        _ecu = m.EcuAt(rpm, eff);
        _knock = m.Knock(rpm, _load, _ecu.KnockShift);
        // ---------------- ความร้อน / น้ำมัน / ลูกสูบสึก ----------------
        {
            float lim = m.D.Limiter, rr = Clamp(rpm / lim, 0f, 1.2f), ldr = _load, lam = _ecu.Lambda;
            float lean = Clamp((lam - 1.0f) / 0.35f, 0f, 1f), rich = Clamp((0.92f - lam) / 0.3f, 0f, 1f), advK = Clamp((_ecu.Adv - _ecu.Mbt) / 12f, -0.5f, 1f), crK = Clamp((m.CrDyn - 9.5f) / 6f, -0.3f, 1f);
            float gen = 6.5f * 0.34f * (0.45f * (ign ? 1f : 0.1f) + 0.9f * ldr * rr * (_fuelOn ? 1f : 0.35f)) * MathF.Pow(m.Cc / 125f, 0.3f) * (1f + 0.9f * lean - 0.25f * rich + 0.35f * MathF.Max(0f, advK) + 0.3f * crK);
            float cool = 6.5f * (0.0030f + 0.0035f * MathF.Min(1f, MathF.Abs(_speed) / 15f));
            _engT += (gen - cool * (_engT - AmbientC)) * dt;
            _oilT += (_engT * 0.92f + 4f - _oilT) * MathF.Min(1f, 0.08f * dt);
            _oilVisc = Clamp(MathF.Exp(-(_oilT - 100f) / 45f), 0.25f, 3f);
            float film = Clamp(_oilVisc / 0.6f, 0f, 1f), hotK = Clamp((_engT - 150f) / 40f, 0f, 1f);
            if (!_seized)
            {
                _wear = Clamp(_wear + (0.012f * (1f - film) * rr * (0.4f + ldr) + 0.03f * hotK * (0.3f + ldr) + 0.05f * _knock * ldr) * dt, 0f, 1f);
                if (_wear >= 1f) { _seized = true; _seizeCause = _engT > 185f ? "ลูกละลาย (ร้อนจัด)" : "ลูกล็อก (น้ำมันบาง/สึก)"; }
            }
            // ร้อน: กำลังตก + น็อกง่าย  ลูกสึก: แก๊ก (เสียงน็อก/สะบัด) กำลังตก ไม่ข้ามรอบสูง
            _knock = Clamp(_knock + 0.3f * Clamp((_engT - 125f) / 45f, 0f, 1f) + 0.14f * Clamp((_wear - 0.2f) / 0.4f, 0f, 1f), 0f, 1f);
            _thermMult = (1f - 0.2f * Clamp((_engT - 115f) / 60f, 0f, 1f)) * (1f - 0.45f * _wear * (0.4f + 0.6f * rr * rr));
            _effLim = lim * (1f - 0.3f * Clamp((_wear - 0.35f) / 0.65f, 0f, 1f));
        }
        _surgePh += dt * 6.2832f * (2.2f + 1.5f * Noise());

        // ตัดรอบแบบ hard cut มี hysteresis ทำให้รอบเด้ง
        float derate = _effLim > 0f ? m.D.Limiter - _effLim : 0f;
        if (rpm > m.D.Limiter - derate) _cut = true;
        else if (rpm < m.D.ResumeRpm - derate) _cut = false;

        // soft cut: ใต้ hard cut ลงมา ค่อย ๆ หน่วงไฟ/ข้ามจังหวะระเบิดมากขึ้นจนถึง hard cut
        _softK = m.D.SoftGap > 0f ? Clamp((rpm - m.D.SoftRpm) / m.D.SoftGap, 0f, 1f) : 0f;
        _fuelOn = ign && !_cut && rpm > 220f;
        _starterActive = StarterHeld && rpm < 1000f;

        float drive = 0f;
        if (_fuelOn && _cycleFiring) drive = m.TorqueInd(rpm) * _load * _ecu.Mult * _thermMult * (1f + 0.16f * _ecu.Lean * MathF.Sin(_surgePh) - 0.10f * _ecu.Rich * MathF.Max(0f, MathF.Sin(_surgePh * 0.5f))) * _cycleVar * (1f - 0.5f * _cycKnock) * (1f - 0.4f * _softK);
        float torque = drive;
        if (_starterActive) torque += 5.5f * MathF.Max(0f, 1f - rpm / 650f);

        // แรงเสียดทาน + pumping loss (ลิ้นปิด = เอนจินเบรก)
        float friction = (m.Friction(rpm) + m.PumpLoss(rpm, _load)) * (1f + 0.10f * Clamp(_oilVisc - 1f, -0.5f, 1.5f) + 0.5f * _wear);
        // เครื่องสูบเดียว: จังหวะอัดต้าน จังหวะขยายคืนแรง เบรกเครื่องเป็นจังหวะหนืด ๆ (คาบ = 2 รอบเพลา)
        _brkPh = (_brkPh + dt * rpm / 60f * MathF.PI) % (2f * MathF.PI);
        friction *= 1f + 0.55f * (1f - _load) * MathF.Min(1.4f, m.CompDrag) * MathF.Sin(_brkPh);
        if (_seized) { torque = 0f; friction = 70f; }       // ลูกสูบล็อก: เพลาข้อล็อก ไม่มีกำลัง
        if (rpm <= 0f && torque < friction) friction = torque;   // หยุดนิ่ง ไม่หมุนกลับ

        // ----- ไดโน: เบรกคุมรอบให้กวาดขึ้นคงที่ แล้ววัดแรงบิดที่เครื่องทำได้ -----
        if (_dynoPhase == 1 && rpm >= DynoStartRpm)
        {
            _dynoPhase = 2;
            _dynoRpm = DynoStartRpm;
        }
        if (_dynoPhase == 2)
        {
            _dynoRpm += DynoRate * dt;
            int bin = (int)(_dynoRpm / 100f);
            if (bin >= 0 && bin < DynoBins)
            {
                _dynoSum[bin] += drive - friction;
                _dynoCnt[bin]++;
            }
            rpm = _dynoRpm;
            if (_dynoRpm >= m.D.Limiter - 120f)
            {
                for (int i = 0; i < DynoBins; i++)
                    DynoTq[i] = _dynoCnt[i] > 0 ? _dynoSum[i] / _dynoCnt[i] : float.NaN;
                _dynoPhase = 0;
                _dynoVersion++;
            }
        }
        else
        {
            rpm = StepDrivetrain(m, rpm, torque - friction, dt);
        }
        if (rpm < 0f) rpm = 0f;
        _rpmPhys = rpm;
    }

    // ---------- ระบบขับเคลื่อน ----------
    private int _gearApplied;
    private float _shiftT;                      // เวลาที่คลัตช์ถูกตัดตอนเปลี่ยนเกียร์ (วินาที)
    private float _tcSmooth;                    // แรงบิดผ่านคลัตช์ (เฉลี่ย) ใช้คำนวณแรงหนีบชุดขับหลังของ CVT
    private const float ShiftTime = 0.20f;
    private const float ClutchRamp = 1800f;     // คลัตช์แรงเหวี่ยง: จับเต็มเมื่อรอบเกินจุดเริ่มจับ + ค่านี้
    private const float ClutchTorque = 60f;     // แรงบิดที่คลัตช์ส่งได้เต็มที่ (N·m ที่ข้อเหวี่ยง)
    private const float SlipScale = 12f;        // rad/s ที่ลื่นแล้วคลัตช์ส่งแรงเต็ม

    /// <summary>
    /// ข้อเหวี่ยง -> คลัตช์แรงเหวี่ยง -> (เกียร์ + สเตอร์ | ชุดขับ CVT + เฟืองท้าย) -> ล้อ -> ถนน
    /// คืนรอบเครื่องใหม่ netEngine = แรงบิดสุทธิของเครื่อง (หักเสียดทานแล้ว)
    /// </summary>
    private float StepDrivetrain(EngineModel m, float rpm, float netEngine, float dt)
    {
        var d = m.D;
        bool auto = d.Auto == 1;
        int g = GearInput;
        if (!auto)
        {
            float want = ClutchEnabled ? Clamp(ClutchInput, 0f, 1f) : 0f;
            _lever += (want - _lever) * MathF.Min(1f, dt * (want > _lever ? 30f : 7f));
        }
        else _lever = 0f;
        if (!auto)
        {
            if (g != _gearApplied)
            {
                _gearApplied = g;
                _gear = g;
                _shiftT = ShiftTime;            // ตัดคลัตช์ชั่วขณะเปลี่ยนเกียร์
            }
            if (_shiftT > 0f) _shiftT -= dt;
        }
        else
        {
            _gear = 0;
            _shiftT = 0f;
        }

        float v = _speed;
        float mass = Vehicle.Mass(d);
        float mEff = Vehicle.EffMass(d);
        float r = Vehicle.WheelRadius(d);
        float ratio = auto ? Cvt.Ratio(_cvtS) * Cvt.Final(d) : Vehicle.Total(d, g);
        float omegaE = rpm / 9.5493f;

        float tc = 0f;                          // แรงบิดที่ส่งผ่านคลัตช์ (บวก = เครื่องลากรถ)
        float lock01 = 0f;
        float slipRpm = 0f, tracUse = 0f; bool spin = false;
        if (ratio > 0f && _shiftT <= 0f)
        {
            // คลัตช์แรงเหวี่ยง: แรงจับขึ้นตามรอบกำลังสอง (แรงเหวี่ยง ∝ ω²) เริ่มจับที่ start เต็มที่ราว full
            // รถออโต้จับที่รอบสูงกว่า (~3,200) เวฟจับตามรอบเดินเบา แต่ไม่ต่ำกว่า 2,600
            float start = auto ? 3200f : MathF.Max(2600f, m.IdleRpm + 900f);
            float full = auto ? 7500f : 7000f;
            lock01 = Clamp((rpm * rpm - start * start) / (full * full - start * start), 0f, 1f);
            _cfLock = lock01;
            lock01 *= 1f - _lever * _lever * (3f - 2f * _lever) * 1f;      // กำคันครัตช์ = แรงจับลดลงนุ่ม ๆ (smoothstep)
            float omegaIn = v / r * ratio;
            float slip = omegaE - omegaIn;
            tc = Clamp(slip / SlipScale, -1f, 1f) * ClutchTorque * lock01;

            // แรงเกาะยางหลัง: เกินแล้วล้อฟรี เครื่องรอบพุ่ง
            float fmax = Vehicle.TractionMax(d);
            float tcMax = fmax * r / (ratio * Vehicle.DriveEff);
            slipRpm = slip * 9.5493f;
            tracUse = tcMax > 0f ? MathF.Abs(tc) / tcMax : 0f;
            spin = MathF.Abs(tc) > tcMax * 1.0f;
            tc = Clamp(tc, -tcMax, tcMax);
        }
        _clutchLock = lock01;
        _slipRpm = slipRpm; _tracUse = tracUse; _spin = spin; _gripMax = Vehicle.TractionMax(d);

        // ----- ชุดขับหน้า CVT: แรงเหวี่ยงของเม็ด (ผ่านชามเม็ด) สู้แรงหนีบชุดขับหลัง -----
        if (auto)
        {
            _tcSmooth += (MathF.Max(tc, 0f) - _tcSmooth) * 0.003f;
            float ff = Cvt.Ff(d, omegaE, _cvtS);
            float fr = Cvt.Fr(d, _tcSmooth, _cvtS);
            float ds = Clamp((ff - fr) / Cvt.FScale, -1f, 1f);
            if (ds > 0f) ds *= Clamp(1f - MathF.Abs(slipRpm) / 800f, 0f, 1f) * Clamp(lock01 * 4f, 0f, 1f);   // สายพานไม่ไล่ขึ้นตอนคลัตช์ยังลื่น/ไม่จับ (ไม่มีโหลด) กันอัตราทดแกว่งขึ้นแล้วตกตอนออกตัว
            _cvtS = Clamp(_cvtS + ds * Cvt.Rate * dt, 0f, 1f);
        }

        // ----- เครื่อง -----
        // ความเฉื่อยข้อเหวี่ยง: เกียร์ว่าง/คลัตช์ยังไม่จับ = ค่าที่ตั้งไว้ให้ฟีลเร่งเครื่องตึง, คลัตช์จับแล้ว ลดเหลือ ~35%
        // (ข้อเหวี่ยงจริงเบากว่า ไม่งั้นแรงบิดครึ่งหนึ่งหมดไปกับการปั่นข้อเหวี่ยงเอง รถเร่งอืด)
        float inertia = m.Inertia * (1f - 0.65f * lock01);
        float newOmega = omegaE + (netEngine - tc) / inertia * dt;
        rpm = newOmega * 9.5493f;

        // ----- รถ -----
        float fDrive = ratio > 0f ? tc * ratio * Vehicle.DriveEff / r : 0f;
        _driveForce = MathF.Max(0f, fDrive);
        float fRes = Vehicle.Resist(d, v, SlopePct);
        float fBrake = Clamp(BrakeInput, 0f, 1f) * Vehicle.BrakeDecel * mass;
        if (v < 0.02f)
        {
            // หยุดนิ่ง: แรงเสียดทานสถิตกับเบรกกันรถไถลถอยหลัง
            float net0 = fDrive - fRes;
            if (net0 <= 0f || fBrake > 0f && net0 < fBrake) { v = 0f; }
            else v += net0 / mEff * dt;
        }
        else
        {
            float net = fDrive - fRes - fBrake;
            v += net / mEff * dt;
            if (v < 0f) v = 0f;
        }
        _speed = v;
        _dist += v * dt;
        return rpm;
    }

    // ====================================================================
    //  เสียง
    // ====================================================================

    private float Noise()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return unchecked((int)_rng) * (1f / 2147483648f);         // -1..1
    }

    private float Unit() => Noise() * 0.5f + 0.5f;                // 0..1

    /// <summary>เริ่มรอบการทำงานใหม่ (ทุก 2 รอบข้อเหวี่ยง): ตัดสินว่ารอบนี้จุดระเบิดไหม แรงแค่ไหน</summary>
    private void NewCycle()
    {
        var m = _model;
        float rpm = _rpmPhys;
        float load = _load;

        bool fire = _fuelOn;
        // แคมโอเวอร์แลปมาก: รอบต่ำไอเสียย้อนเข้าไอดี จุดไม่ติดบางรอบ (เสียงลูปแคม)
        float low = Clamp(1f - (rpm - m.IdleRpm) / 2500f, 0f, 1f) * (1f - load * 0.6f);
        if (fire && Unit() < m.Lope * 0.30f * low) fire = false;
        // กำลังอัดต่ำ: เดินเบาแล้วจุดไม่ติดเป็นพัก ๆ
        if (fire && Unit() < m.LowCrMisfire * (0.4f + low)) fire = false;
        // ตารางจูนกล่อง: ส่วนผสมบาง/หนาเกิน ประกายอ่อน หรือตัดน้ำมัน -> จุดไม่ติดเป็นจังหวะ ๆ (เดินหอบ/สะดุด)
        if (fire && Unit() < _ecu.Misfire) fire = false;
        // soft cut: ข้ามจังหวะระเบิดแบบสุ่ม มากขึ้นเรื่อย ๆ จนถึง hard cut
        if (fire && Unit() < _softK * 0.6f) fire = false;
        // วาล์วลอย: ไอดีรั่ว จุดไม่ติด
        if (fire && rpm > m.FloatRpm && Unit() < 0.45f) fire = false;
        _cycleFiring = fire;

        // ความไม่สม่ำเสมอรอบต่อรอบ (เดินเบาจะสะดุดมากกว่าตอนเปิดคันเร่ง)
        float g = Noise() + Noise() + Noise();
        float jitter = 0.03f + 0.12f * (1f - load) + 0.22f * m.Lope * low + 0.30f * _knock + 0.42f * _ecu.Lean + 0.34f * _ecu.Rich;   // บางเกิน/หนาเกิน: เผาไหม้ไม่สม่ำเสมอ
        _cycleVar = Clamp(1f + jitter * g, 0.5f, 1.5f);
        _cycKnock = fire ? _knock * (0.5f + Unit()) : 0f;
        // ความแรงของพัลส์ไอเสีย: ไอดีเยอะ = ดัง
        // + กำลังอัดสูงดันแรง, + ช่วงรอบที่เครื่องหายใจดี (VE) พัลส์แรง ช่วงที่แคมไม่ทำงาน (ต่ำกว่าช่วงแรง) พัลส์อ่อน
        float cond = m.CrAmp * (0.55f + 0.45f * m.VeNorm(rpm));
        // เดินหอบ (คลิปอ้างอิง): เสียงแรงสลับเบาเป็นจังหวะ ~±15% ช่วงรอบเดินเบา
        if (m.Hunt > 0f) cond *= 1f - 0.20f * m.Hunt * Math.Max(0f, MathF.Sin(_huntPh)) * Clamp(1f - (rpm - m.IdleRpm) / 1500f, 0f, 1f);
        _cycAmp = fire ? MathF.Pow(0.12f + 0.88f * load, 0.8f) * _cycleVar * cond : 0f;
        // ใช้พัลส์กลุ่ม "เปิดคันเร่ง" เมื่อจุดระเบิดจริงและไอดีมากพอ ที่เหลือใช้กลุ่ม "ปิดคันเร่ง/เบรกเครื่อง"
        _wantOn = fire && load > 0.3f;
        // ตัดรอบ: ไอเสียยังพ่นออกมา (ไอดีที่ไม่ได้เผา) ดังประมาณครึ่งหนึ่ง จึงเป็นเสียง "บรื๊ด" ต่อเนื่อง ไม่เงียบ
        _cycCut = _cut;
    }

    private bool _wantOn, _cycCut;

    // ----- เลือกพัลส์ตามรอบเครื่อง: เดินต่อเนื่องเป็นท่อนสั้น ๆ แล้วสุ่มกระโดด ไม่วนซ้ำเป็นรอบ -----
    private int _cursor;                        // พัลส์ล่าสุดที่เลือกจากไฟล์ (แบบ A)
    private int _runLeft;
    private readonly int[] _recent = new int[40];
    private int _recentPos;
    private readonly int[] _cand = new int[1024];

    private bool UsedRecently(int idx)
    {
        for (int i = 0; i < _recent.Length; i++)
            if (Math.Abs(_recent[i] - idx) < 8) return true;
        return false;
    }

    private int Collect(float rpm, bool on, float tol, bool skipRecent)
    {
        int n = 0;
        for (int k = 0; k < _pulses.Count && n < _cand.Length; k++)
        {
            if (_pulses.OnLoad[k] != on) continue;
            if (MathF.Abs(_pulses.Rpm[k] - rpm) > tol * rpm) continue;
            if (skipRecent && UsedRecently(k)) continue;
            _cand[n++] = k;
        }
        return n;
    }

    /// <summary>
    /// เลือกพัลส์ถัดไปให้รอบใกล้รอบเครื่องและกลุ่มภาระตรงกัน (เปิด/ปิดคันเร่ง)
    /// ภายในท่อ (3-11 ลูก) เดินตามไฟล์ต่อเนื่อง จบท่อนแล้วสุ่มกระโดดไปที่อื่นที่ไม่ได้ใช้เร็ว ๆ นี้
    /// ถ้าไม่มีพัลส์ที่รอบใกล้พอ ใช้ที่ใกล้สุดแล้วให้ EmitGrain ยืด/หดตามส่วน
    /// </summary>
    private int NextPulse(float rpm, bool on)
    {
        int cnt = _pulses.Count;
        if (cnt == 0) return 0;
        rpm = MathF.Max(rpm, 300f);

        int nx = _cursor + 1;
        if (_runLeft > 0 && nx < cnt && _pulses.Seg[nx] == _pulses.Seg[_cursor]
            && _pulses.OnLoad[nx] == on
            && MathF.Abs(_pulses.Rpm[nx] - _pulses.Rpm[_cursor]) < 0.12f * _pulses.Rpm[_cursor])
        {
            _cursor = nx;
            _runLeft--;
        }
        else
        {
            // ขยายช่วงรอบที่ยอมรับทีละขั้นจนมีตัวเลือกพอสุ่ม (กลุ่มเดียวกันก่อน แล้วค่อยยืมอีกกลุ่ม)
            int n = Collect(rpm, on, 0.08f, true);
            if (n < 10) n = Collect(rpm, on, 0.16f, true);
            if (n < 10) n = Collect(rpm, on, 0.30f, true);
            if (n < 10) n = Collect(rpm, on, 0.70f, true);
            if (n < 10) n = Collect(rpm, on, 3.00f, true);
            if (n < 3) n = Collect(rpm, on, 3.00f, false);
            if (n == 0)
            {
                // ไม่มีใกล้เลย: เลือกที่รอบใกล้ที่สุดในกลุ่มเดียวกัน (ไม่มีกลุ่มนั้นก็กลุ่มไหนก็ได้)
                float bestD = float.MaxValue;
                int bi = 0;
                for (int k = 0; k < cnt; k++)
                {
                    float d = MathF.Abs(_pulses.Rpm[k] - rpm) + (_pulses.OnLoad[k] == on ? 0f : 3000f);
                    if (d < bestD && !UsedRecently(k)) { bestD = d; bi = k; }
                }
                _cand[0] = bi;
                n = 1;
            }
            _cursor = _cand[(int)(Unit() * n) % n];
            _runLeft = 3 + (int)(Unit() * 8f);
        }
        _recent[_recentPos] = _cursor;
        _recentPos = (_recentPos + 1) % _recent.Length;
        return _cursor;
    }

    private const float MaxStretch = 3.2f;
    private const float BrightK = 1.5f;             // เสริมย่านสูงตามส่วนที่ยืดพัลส์ (ยืดมาก = ทุ้มลง จึงเติมความใส)

    /// <summary>
    /// วางพัลส์ที่อัดไว้ลงบัฟเฟอร์ ถ้ารอบเครื่องต่ำกว่ารอบที่อัด จะยืดพัลส์ให้ยาวขึ้นตามส่วน
    /// (เสียงทุ้มลงและหางเสียงยาวขึ้น แทนที่จะเป็นเสียงติ๊กสั้น ๆ คั่นด้วยความเงียบ)
    /// </summary>
    private void EmitGrain(int index, float amp, float rpm)
    {
        if (_pulses.Count == 0) return;
        var p = _pulses.Grains[index];
        amp *= _pulses.Gain[index];
        // PitchStretch: เครื่องเล็กพัลส์สั้น/แหลม เครื่องใหญ่ยาว/ทุ้ม เทียบเสียงต้นแบบ 196cc
        float s = Clamp(_pulses.Rpm[index] / MathF.Max(rpm, 400f) * _model.PitchStretch, 0.6f, MaxStretch);
        int outLen = (int)(p.Length * s);
        int start = _olaR + _olaLead - (int)(_pulses.Pre[index] * s);
        float inv = 1f / s;

        // ความแผด: ชดเชยย่านสูงที่หายตอนยืดพัลส์ + ตามสเปกเครื่อง (กำลังอัด/ลูปกว้าง/แคม/4 วาล์ว) หนักตอนเปิดคันเร่งเต็ม + น็อกแผดหยาบ
        float boost = _cycCut ? 0f : Clamp(BrightK * (s - 1f) + _model.SoundBright * (0.35f + 0.65f * _load) + 1.4f * _cycKnock, -0.85f, 2.8f);
        float hpA = 1f - MathF.Exp(-2f * MathF.PI * 800f / Fs), z = 0f;
        for (int j = 0; j < outLen; j++)
        {
            float x = j * inv;
            int i0 = (int)x;
            if (i0 >= p.Length - 1) break;
            float f = x - i0;
            float v = p[i0] + (p[i0 + 1] - p[i0]) * f;
            if (boost != 0f)
            {
                z += hpA * (v - z);
                v += boost * (v - z);
            }
            _ola[(start + j) & OlaMask] += v * amp;
        }
    }

    private static bool Crossed(float prev, float ph, float deg) => prev < deg && ph >= deg;

    /// <summary>สร้างเสียง count แซมเปิล (mono 16-bit) เรียกจากเธรดเสียงเท่านั้น</summary>
    public void Render(short[] buffer, int count)
    {
        float vol = Clamp(Volume, 0f, 1f);

        for (int i = 0; i < count; i++)
        {
            if (_blockPos == 0) StepPhysics(BlockDt);
            _blockPos++;
            if (_blockPos >= Block) _blockPos = 0;

            _rpmAudio += (_rpmPhys - _rpmAudio) * 0.02f;

            // ----- มุมข้อเหวี่ยง -----
            float prev = _phase;
            float ph = prev + _rpmAudio * (6f / Fs);              // rpm * 360/60 องศาต่อวินาที

            // วาล์วไอเสียเปิด = พัลส์ไอเสียหนึ่งลูก (เสียงจริงจากไฟล์)
            if (Crossed(prev, ph, EvoDeg) && _rpmAudio > 150f)
            {
                float amp;
                if (_cycCut) amp = 0.55f;                           // ตัดรอบ: พ่นไอดีที่ไม่ได้เผา
                else if (_wantOn) amp = _cycAmp;
                else amp = 0.75f * (0.35f + 0.65f * Clamp((_rpmAudio - 2500f) / 5000f, 0f, 1f)) * _cycleVar;
                // เสียงเดินเบาเบาเกินไป (พัลส์โหลดต่ำ): เสริมระดับช่วงรอบเดินเบาให้ได้ยินชัด ค่อย ๆ ลดจนไม่มีผลที่ ~รอบเดินเบา+2,500
                if (!_cycCut) amp *= 1f + IdleBoostK * Clamp(1f - (_rpmAudio - _model.IdleRpm) / 2500f, 0f, 1f);
                EmitGrain(NextPulse(_rpmAudio, _wantOn), amp, _rpmAudio);
            }

            if (ph >= 720f)
            {
                ph -= 720f;
                NewCycle();
            }
            _phase = ph;

            float e = _ola[_olaR];
            _ola[_olaR] = 0f;
            _olaR = (_olaR + 1) & OlaMask;

            float o = e * _model.Loudness * 0.95f * vol;   // ปรับให้สองแบบดังเท่ากัน
            float dc = o - _dcX + 0.9975f * _dcY;                 // ตัด DC
            _dcX = o;
            _dcY = dc;
            float outS = MathF.Tanh(dc * 1.2f);                   // กันเสียงแตกที่ลำโพง

            Scope[_scopeW] = outS;
            _scopeW = (_scopeW + 1) & 2047;
            buffer[i] = (short)(outS * 30000f);
        }
    }
}

/// <summary>
/// พัลส์ไอเสียทีละรอบที่ตัดจากเสียงจริง: แต่ละลูกครอบคลุมตั้งแต่พัลส์ก่อนหน้าถึงพัลส์ถัดไป
/// คูณหน้าต่าง Hann สองข้าง เมื่อวางห่างเท่าเดิมจะต่อกันสนิทเป็นเสียงต้นฉบับ
/// </summary>
internal sealed class EnginePulses
{
    public float[][] Grains = Array.Empty<float[]>();
    public int[] Pre = Array.Empty<int>();
    public float[] Rpm = Array.Empty<float>();     // รอบเครื่องของพัลส์แต่ละลูก (จากคาบที่วัดได้)
    public int[] Seg = Array.Empty<int>();         // ช่วงที่อัดต่อเนื่องเดียวกัน
    public bool[] OnLoad = Array.Empty<bool>();    // true = เปิดคันเร่ง, false = ปิดคันเร่ง/เบรกเครื่อง
    public float[] Gain = Array.Empty<float>();    // ปรับระดับให้ทุกช่วงดังเท่ากัน
    public int Count => Grains.Length;
    public int MaxPre;
    private const float TargetRms = 0.15f;

    /// <summary>
    /// ไฟล์ .marks: บรรทัด "seg id load rms" ขึ้นต้นช่วงใหม่ ตามด้วยเลขแซมเปิลของพัลส์แต่ละลูกในช่วงนั้น
    /// พัลส์หนึ่งลูกครอบคลุมจากพัลส์ก่อนหน้าถึงพัลส์ถัดไปในช่วงเดียวกัน คูณหน้าต่าง Hann สองข้าง
    /// </summary>
    public static EnginePulses Load(string basePath, int sampleRate)
    {
        float[] x = ReadWav(basePath + ".wav", out int fs);
        if (fs != sampleRate) throw new InvalidDataException($"engine_ref.wav ต้องเป็น {sampleRate} Hz");

        var segs = new List<(int id, bool on, List<int> marks)>();
        foreach (var line in File.ReadAllLines(basePath + ".marks"))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("seg "))
            {
                var p = line.Split(' ');
                segs.Add((int.Parse(p[1]), p[2] == "1", new List<int>()));
            }
            else if (segs.Count > 0) segs[^1].marks.Add(int.Parse(line));
        }

        var grains = new List<float[]>();
        var pre = new List<int>();
        var rpm = new List<float>();
        var seg = new List<int>();
        var on = new List<bool>();
        var gain = new List<float>();
        int maxPre = 0;
        foreach (var (id, isOn, marks) in segs)
        {
            if (marks.Count < 3) continue;
            double ss = 0;
            for (int i = marks[0]; i < marks[^1]; i++) ss += x[i] * x[i];
            float g0 = TargetRms / MathF.Max(1e-4f, (float)Math.Sqrt(ss / (marks[^1] - marks[0])));

            for (int k = 1; k + 1 < marks.Count; k++)
            {
                int a = marks[k - 1], c = marks[k], b = marks[k + 1];
                int pr = c - a, po = b - c;
                var g = new float[pr + po];
                for (int j = 0; j < pr; j++)
                    g[j] = x[a + j] * (0.5f - 0.5f * MathF.Cos(MathF.PI * j / pr));
                for (int j = 0; j < po; j++)
                    g[pr + j] = x[c + j] * (0.5f + 0.5f * MathF.Cos(MathF.PI * j / po));
                grains.Add(g);
                pre.Add(pr);
                rpm.Add(120f * fs / (0.5f * (pr + po)));      // 1 พัลส์ต่อ 2 รอบข้อเหวี่ยง
                seg.Add(id);
                on.Add(isOn);
                gain.Add(g0);
                maxPre = Math.Max(maxPre, pr);
            }
        }
        return new EnginePulses
        {
            Grains = grains.ToArray(), Pre = pre.ToArray(), Rpm = rpm.ToArray(),
            Seg = seg.ToArray(), OnLoad = on.ToArray(), Gain = gain.ToArray(), MaxPre = maxPre,
        };
    }

    private static float[] ReadWav(string p, out int fs)
    {
        var b = File.ReadAllBytes(p);
        int pos = 12; fs = 0; int ch = 1, bits = 16;
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int len = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ")
            {
                ch = BitConverter.ToInt16(b, pos + 10);
                fs = BitConverter.ToInt32(b, pos + 12);
                bits = BitConverter.ToInt16(b, pos + 22);
            }
            if (id == "data")
            {
                if (bits != 16) throw new InvalidDataException("engine_ref.wav ต้องเป็น 16-bit PCM");
                int n = Math.Min(len, b.Length - pos - 8) / 2 / ch;
                var r = new float[n];
                for (int i = 0; i < n; i++) r[i] = BitConverter.ToInt16(b, pos + 8 + i * 2 * ch) / 32768f;
                return r;
            }
            pos += 8 + len + (len & 1);
        }
        throw new InvalidDataException("engine_ref.wav ไม่มีข้อมูลเสียง");
    }
}
