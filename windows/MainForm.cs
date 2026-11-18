using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace RevGame;

/// <summary>
/// หน้าต่างเกม: เต็มจอ ปรับสเกลจากพื้นที่เสมือน 1920x1080 ให้พอดีจอใดก็ได้
/// วาดเองทั้งหมดด้วย GDI+ แบบ immediate-mode: ทุกเฟรมวาดแล้วลงทะเบียนพื้นที่คลิก (Hit) ไปพร้อมกัน
/// ไฟล์นี้: หน้าต่าง/อินพุต/ลูปเกม/จับเวลาบนถนน  (ฉากอยู่ใน Screen*.cs, ตัวช่วยวาดใน UiHelpers.cs)
/// </summary>
public sealed partial class MainForm : Form
{
    private const float VW = 1920f, VH = 1080f;
    private enum Page { Menu, Garage, Drive, Settings }

    private Page _page = Page.Menu;
    private Page _prevPage = Page.Drive;
    private bool _fullscreen = true;
    private bool _showFps;
    private float _fps = 144f;
    private float _paintMs;
    private int _frame;

    private readonly SaveData _save = SaveData.Load();
    private readonly EngineSim _eng;
    private WaveOutPlayer? _player;
    private string? _audioError;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private readonly HashSet<Keys> _keys = new();

    // ---------- อู่ ----------
    private EngineDesign _draft;
    private EngineModel _preview;
    private int _sel;
    private readonly List<int> _vis = new();         // ค่าที่แสดงในอู่ตามแบบที่เลือก

    // ---------- ขับ ----------
    private bool _mouseThrottle;
    private float _mouseThrottleValue;
    private bool _mouseStarter;
    private bool _mouseClutch;
    private float _rpmRate, _lastRpmG;           // อัตราเปลี่ยนรอบ (rpm/วินาที) ใช้ทำนายรอบของ X/Z
    private float _gradThr;                     // คันเร่งที่ค่อย ๆ บิดด้วยปุ่ม X/Z
    private float _needleRpm;
    private float _peakRpm;
    private int _gearSel;
    private int _slope;

    // ทดสอบวิ่งถนน: 0-60 กม./ชม., 100 ม., 402 ม., ท็อปสปีด
    private bool _roadArmed = true, _roadTiming;
    private double _roadStart;
    private float _distStart;
    private bool _got060, _got100, _got402;
    private float _last060 = -1f, _last100 = -1f, _last402 = -1f, _trap402, _topSession;
    private bool _saveDirty;
    private RunStats _cur = new();                  // สถิติของชุดที่ประกอบอยู่ (หลังแก้)
    private RunStats? _before;                      // สถิติของชุดก่อนแก้ล่าสุด (เทียบ)

    // ผลไดโน
    private int _dynoVersionSeen;
    private DynoResult? _dyno;
    private bool _showDyno;

    private string _toast = "";
    private double _toastUntil;

    public MainForm()
    {
        _eng = new EngineSim(_save.Design) { Volume = _save.Volume, ClutchEnabled = _save.ClutchSys };
        Vehicle.SurfaceIndex = _save.Surface;
        _draft = _save.Design.Clone();
        _preview = new EngineModel(_draft);
        RefreshVisible();

        Text = "Rev Lab - ห้องทดลองเครื่องยนต์ 1 สูบ 4 จังหวะ";
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Palette.Bg;
        KeyPreview = true;
        MinimumSize = new Size(960, 540);
        StartPosition = FormStartPosition.CenterScreen;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        ApplyWindowMode(_save.Fullscreen);

        try
        {
            _player = new WaveOutPlayer(EngineSim.SampleRate, _eng.Render);
        }
        catch (Exception ex)
        {
            _audioError = ex.Message;
        }
        if (_eng.LoadError != null) _audioError = _eng.LoadError;

        _timer.Tick += (_, _) => Tick();
        _timer.Start();

    }

    private void ApplyWindowMode(bool full)
    {
        _fullscreen = full;
        _save.Fullscreen = full;
        if (full)
        {
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = System.Windows.Forms.Screen.FromControl(this).Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            WindowState = FormWindowState.Normal;
            ClientSize = new Size(1280, 720);
            CenterToScreen();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _save.Volume = _eng.Volume;
        _save.Save();
        _player?.Dispose();
        _player = null;
        base.OnFormClosed(e);
    }

    private void Toast(string s)
    {
        _toast = s;
        _toastUntil = _clock.Elapsed.TotalSeconds + 3.5;
    }

    private void GoTo(Page p)
    {
        if (_page == Page.Settings && p != Page.Settings) { _save.Volume = _eng.Volume; _save.Save(); }
        if (p == Page.Menu && _page != Page.Menu) _prevPage = _page;
        _page = p;
        _mouseThrottle = _mouseStarter = _mouseClutch = false;
    }

    // ====================================================================
    //  อินพุตแป้นพิมพ์
    // ====================================================================

    protected override bool IsInputKey(Keys keyData)
    {
        Keys k = keyData & Keys.KeyCode;
        if (k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool first = _keys.Add(e.KeyCode);
        if (first)
        {
            if (e.KeyCode == Keys.F11) ApplyWindowMode(!_fullscreen);
            else if (_showDyno)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape) _showDyno = false;
            }
            else switch (_page)
            {
                case Page.Menu: MenuKey(e.KeyCode); break;
                case Page.Settings: if (e.KeyCode == Keys.Escape) GoTo(Page.Menu); break;
                case Page.Garage: GarageKey(e.KeyCode, e.Shift); break;
                case Page.Drive: DriveKey(e.KeyCode); break;
            }
        }
        else if (_page == Page.Garage && !_showDyno && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
            GarageKey(e.KeyCode, e.Shift);                   // กดค้างเพื่อปรับต่อเนื่อง

        if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add) _eng.Volume = Math.Min(1f, _eng.Volume + 0.05f);
        if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract) _eng.Volume = Math.Max(0f, _eng.Volume - 0.05f);
        if (!e.Alt)                                     // ปล่อย Alt+F4 ให้ Windows จัดการ
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _keys.Remove(e.KeyCode);
        if (!e.Alt) e.Handled = true;
        base.OnKeyUp(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        // สลับหน้าต่างตอนกดค้าง: ปล่อยทุกอย่าง ไม่ให้คันเร่งค้าง
        _keys.Clear();
        _mouseThrottle = _mouseStarter = _mouseClutch = false;
        _drag?.Up?.Invoke();
        _drag = null;
        base.OnDeactivate(e);
    }

    private bool Down(Keys k) => _keys.Contains(k);

    private void MenuKey(Keys k)
    {
        switch (k)
        {
            case Keys.Enter: GoTo(Page.Drive); break;
            case Keys.G: GoTo(Page.Garage); break;
            case Keys.Escape: GoTo(_prevPage); break;
        }
    }

    private void DriveKey(Keys k)
    {
        switch (k)
        {
            case Keys.K: _eng.Ignition = !_eng.Ignition; break;
            case Keys.D: StartDynoRun(); break;
            case Keys.G: GoTo(Page.Garage); break;
            case Keys.Up: case Keys.E: Shift(+1); break;
            case Keys.Down: case Keys.Q: Shift(-1); break;
            case Keys.N: SelectGear(0); break;
            case Keys.C: ToggleClutchSystem(); break;
            case Keys.P: ToggleAutopilot(); break;
            case Keys.V: SetSurface((Vehicle.SurfaceIndex + 1) % Vehicle.Surfaces.Length); break;
            case Keys.OemOpenBrackets: ChangeSlope(-1); break;
            case Keys.OemCloseBrackets: ChangeSlope(+1); break;
            case Keys.Escape: GoTo(Page.Menu); break;
        }
    }

    private void SetSurface(int i)
    {
        Vehicle.SurfaceIndex = i;
        _save.Surface = i;
        _save.Save();
        var s = Vehicle.Surfaces[i];
        Toast($"พื้นถนน: {s.Name}  (แรงเกาะ x{s.MuK:0.00}, ต้านการกลิ้ง x{s.CrrK:0.00})");
    }

    private void ToggleClutchSystem()
    {
        _eng.ClutchEnabled = !_eng.ClutchEnabled;
        _save.ClutchSys = _eng.ClutchEnabled;
        _save.Save();
        Toast(_eng.ClutchEnabled ? "ระบบครัตช์มือ: เปิด (ค้าง Shift = กำครัตช์)" : "ระบบครัตช์มือ: ปิด (เปลี่ยนเกียร์ได้ทันที คลัตช์แรงเหวี่ยงจับเอง)");
    }

    private void Shift(int dir) => SelectGear(Math.Clamp(_gearSel + dir, 0, 4));

    /// <summary>เปลี่ยนเกียร์: ต้องกำคันครัตช์ (ค้าง Shift) ก่อน ถ้าคลัตช์แรงเหวี่ยงกำลังจับอยู่ (ต่ำกว่ารอบจับเปลี่ยนได้เลย)</summary>
    private void SelectGear(int gi)
    {
        if (_eng.IsAuto || gi == _gearSel) return;
        if (_eng.ClutchEnabled && _eng.Running && _eng.ClutchLever < 0.6f && _eng.CentrifugalLock > 0.05f)
        {
            Toast("กำครัตช์ก่อน: ค้าง Shift ไว้ แล้วกดเปลี่ยนเกียร์");
            return;
        }
        _gearSel = gi;
    }

    private void ChangeSlope(int dir) => _slope = Math.Clamp(_slope + dir, -6, 20);

    private void StartDynoRun()
    {
        if (_eng.IsAuto) { if (_eng.SpeedKmh > 1f) { Toast("หยุดรถก่อนวิ่งไดโน"); return; } }
        else if (_gearSel != 0) { Toast("ใส่เกียร์ว่าง (N) ก่อนวิ่งไดโน"); return; }
        if (!_eng.Running) { Toast("ต้องสตาร์ทเครื่องให้ติดก่อน (กด S ค้าง)"); return; }
        _eng.StartDyno();
    }

    private void Assemble()
    {
        // แบบเปลี่ยนจริงจึงเริ่มสถิติชุดใหม่ และเก็บชุดเดิมไว้เทียบ (ประกอบซ้ำแบบเดิม = สถิติต่อเนื่อง)
        bool changed = _draft.Serialize() != _save.Design.Serialize();
        if (changed)
        {
            if (_cur.Any) _before = _cur;
            _cur = new RunStats();
            _last060 = _last100 = _last402 = -1f;
            _peakRpm = 0f;
            _topSession = 0f;
            _dyno = null;
        }
        _eng.Assemble(_draft);
        _save.Design = _draft.Clone();
        _save.Save();
        _gearSel = 0;
        _roadArmed = false;                     // ต้องหยุดนิ่งก่อนจึงจะเริ่มจับเวลาใหม่
        _roadTiming = false;
        GoTo(Page.Drive);
        Toast(_draft.Auto == 1
            ? "ประกอบแล้ว กด S ค้างเพื่อสตาร์ท แล้วบิดคันเร่งออกตัวได้เลย (ออโต้)"
            : "ประกอบแล้ว กด S ค้างเพื่อสตาร์ท แล้วเข้าเกียร์ด้วย E");
    }

    // ====================================================================
    //  ลูปเกม (ทุกเฟรม)
    // ====================================================================

    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(0.1, now - _lastTime);
        _lastTime = now;
        if (dt > 0f) _fps += (1f / dt - _fps) * 0.05f;

        // คันเร่ง: เมาส์ลากแถบ = ละเอียด, คีย์ = ระดับตายตัว (เฉพาะหน้าขับ)
        float thr = 0f;
        float brake = 0f;
        bool starter = false;
        if (_page == Page.Drive && !_showDyno)
        {
            // X/Z = ค่อย ๆ บิดคันเร่ง (ไม่ใช่ระดับตายตัว): X หยุดที่ต่ำกว่ารอบตัดไม่ตัด, Z ไปจนเกือบ/แตะรอบตัดเบา ๆ, W = เต็ม
            bool zKey = Down(Keys.Z), xKey = Down(Keys.X);
            if (zKey || xKey)
            {
                float ceil = zKey ? _eng.LimiterRpm - 40f : _eng.LimiterRpm - 800f;
                float pred = _eng.Rpm + _rpmRate * 0.4f;                  // ดูล่วงหน้า 0.4 วิ ไม่ให้รอบพุ่งเลยเป้า
                float rate = 0.8f * Math.Clamp((ceil - pred) / 350f, -1.5f, 1f);
                _gradThr = Math.Clamp(_gradThr + rate * dt, 0f, 1f);
            }
            else _gradThr = Math.Max(0f, _gradThr - 3f * dt);
            thr = _gradThr;
            if (Down(Keys.W)) { thr = 1f; _gradThr = 1f; }
            if (_mouseThrottle) thr = _mouseThrottleValue;
            if (Down(Keys.Space) || Down(Keys.B)) brake = 1f;
            starter = Down(Keys.S) || _mouseStarter;
            if (starter && Down(Keys.S)) _eng.Ignition = true;
            if (_autoDrive) AutopilotStep(dt, ref thr, ref brake, ref starter);
        }
        else if (_autoDrive) { _autoDrive = false; }
        _eng.ThrottleInput = thr;
        _eng.BrakeInput = brake;
        _eng.StarterHeld = starter;
        bool shiftHeld = Down(Keys.ShiftKey) || Down(Keys.LShiftKey) || Down(Keys.RShiftKey);
        _eng.ClutchInput = _page == Page.Drive && !_showDyno && (shiftHeld || _mouseClutch || _clock.Elapsed.TotalSeconds < _apClutchUntil) ? 1f : 0f;
        _eng.GearInput = _gearSel;
        _eng.SlopePct = _slope;

        if (dt > 0f) { _rpmRate += ((_eng.Rpm - _lastRpmG) / dt - _rpmRate) * 0.3f; _lastRpmG = _eng.Rpm; }
        float rpm = _eng.Rpm;
        _needleRpm += (rpm - _needleRpm) * Math.Min(1f, dt * 25f);
        if (rpm > _peakRpm) _peakRpm = rpm;
        if (_peakRpm > _cur.PeakRpm) _cur.PeakRpm = _peakRpm;

        // ผลไดโนเสร็จ
        if (_eng.DynoVersion != _dynoVersionSeen)
        {
            _dynoVersionSeen = _eng.DynoVersion;
            FinishDyno();
        }

        RoadTests(now);
        _frame++;
        if (_page == Page.Drive || _showDyno || _frame % 3 == 0) Invalidate();
    }

    private void FinishDyno()
    {
        if (_eng.DynoFailed)
        {
            _dyno = null;
            Toast("วิ่งไดโนไม่จบ: เครื่องดับระหว่างวัด");
            return;
        }
        _dyno = DynoResult.From(_eng.Model, _eng.DynoTq, false);
        _cur.Hp = _dyno.PeakPs * EngineModel.PsToHp;
        _showDyno = true;
    }

    private void RoadTests(double now)
    {
        float kmh = _eng.SpeedKmh;
        if (kmh > _topSession) _topSession = kmh;
        if (_topSession > _cur.Top) _cur.Top = _topSession;
        if (_topSession > _save.BestTop + 0.5f)
        {
            _save.BestTop = _topSession;
            _saveDirty = true;
        }

        if (kmh < 1f)
        {
            // รถหยุดนิ่ง: เตรียมจับเวลาใหม่
            _roadArmed = true;
            _roadTiming = false;
            _distStart = _eng.DistanceM;
            if (_saveDirty) { _save.Save(); _saveDirty = false; }
        }
        else if (_roadArmed && !_roadTiming)
        {
            _roadArmed = false;                 // เริ่มวัดครั้งนี้แล้ว (หรือข้าม) ต้องหยุดนิ่งก่อนจึงจะวัดใหม่ได้
            if (kmh < 8f)
            {
                _roadTiming = true;
                _roadStart = now;
                _got060 = _got100 = _got402 = false;
            }
        }
        if (!_roadTiming) return;

        float el = (float)(now - _roadStart);
        float dist = _eng.DistanceM - _distStart;
        if (!_got060 && kmh >= 60f)
        {
            _got060 = true;
            _last060 = el;
            if (el >= 0.8f) RunStats.Best(ref _cur.B060, el);
            if (el >= 0.8f && (_save.Best060 < 0f || el < _save.Best060)) { _save.Best060 = el; _saveDirty = true; }
        }
        if (!_got100 && dist >= 100f)
        {
            _got100 = true;
            _last100 = el;
            if (el >= 2f) RunStats.Best(ref _cur.B100, el);
            if (el >= 2f && (_save.Best100 < 0f || el < _save.Best100)) { _save.Best100 = el; _saveDirty = true; }
        }
        if (!_got402 && dist >= 402.3f)
        {
            _got402 = true;
            _last402 = el;
            _trap402 = kmh;
            if (el >= 5f) RunStats.Best(ref _cur.B402, el);
            if (el >= 5f && (_save.Best402 < 0f || el < _save.Best402)) { _save.Best402 = el; _saveDirty = true; }
        }
    }

    // ====================================================================
    //  วาดจอ
    // ====================================================================

    protected override void OnPaint(PaintEventArgs e) => OnPaintCore(e, ClientSize.Width, ClientSize.Height);

    private void OnPaintCore(PaintEventArgs e, int cw, int ch)
    {
        long t0 = Stopwatch.GetTimestamp();
        Graphics g = e.Graphics;
        g.ResetTransform();
        g.SetClip(new Rectangle(0, 0, cw, ch));
        DrawBackgroundCached(g, cw, ch);

        float s = Math.Min(cw / VW, ch / VH);
        if (s <= 0f) return;
        _scale = s;
        _offX = (cw - VW * s) / 2f;
        _offY = (ch - VH * s) / 2f;
        g.TranslateTransform(_offX, _offY);
        g.ScaleTransform(s, s);
        g.SetClip(new RectangleF(0, 0, VW, VH));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        _hits.Clear();

        switch (_page)
        {
            case Page.Menu: DrawMenu(g); break;
            case Page.Settings: DrawSettings(g); break;
            case Page.Garage: DrawGarage(g); break;
            case Page.Drive: DrawDrive(g); break;
        }

        if (_audioError != null)
            Txt(g, "ไม่มีเสียง: " + _audioError, _fSm, Palette.Red, 40, VH - 64);
        if (_showDyno && _dyno != null) DrawDynoResult(g);

        if (_clock.Elapsed.TotalSeconds < _toastUntil)
        {
            // ข้อความแจ้งเตือนอยู่แถบล่างฝั่งซ้าย (หน้าขับให้ตรงกลางเกจ) ไม่ทับปุ่มทางขวา
            float tx = _page == Page.Drive ? 640f : 1070f;
            var r = new RectangleF(tx - 460, VH - 58, 920, 48);
            FillRR(g, r, 4, Color.FromArgb(245, 26, 30, 38)); DrawRR(g, r, 4, Palette.Accent, 1f);
            TxtC(g, _toast, _fMd, Palette.Accent, tx, r.Y + r.Height / 2);
        }
        if (_showFps) g.DrawString($"{_fps:0} fps  /  paint {_paintMs:0.0} ms", _fXs, Br(Palette.Dim), new RectangleF(VW - 520, 2, 500, 30), SfR);
        _paintMs += ((float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency) - _paintMs) * 0.1f;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _player?.Dispose();
            DisposeUi();
        }
        base.Dispose(disposing);
    }
}
