using System.Drawing.Drawing2D;

namespace RevGame;

public sealed partial class MainForm
{
    private int _driveTab;   // การ์ดขวาหน้าทดสอบ: 0 = คลัตช์/ยาง, 1 = Live data
    private float _gaugeMax = 11000f;               // สเกลหน้าปัดตามรอบตัดของเครื่องที่ประกอบอยู่

    // ====================================================================
    //  หน้าขับ
    // ====================================================================

    private void DrawDrive(Graphics g)
    {
        DrawTopBar(g, "ห้องทดสอบ",
            "W เร่งสุด  •  X ค่อย ๆ เร่ง (ไม่ตัดรอบ)  •  Z ค่อย ๆ เร่ง (แตะรอบตัดเบา ๆ)  •  Space เบรก  •  S สตาร์ท  •  E/Q เกียร์  •  D ไดโน  •  [ ] ความชัน  •  P ขับเอง  •  V พื้นถนน  •  G อู่");

        var m = _eng.Model;
        var d = m.D;
        bool auto = d.Auto == 1;
        _gaugeMax = Math.Max(10000f, MathF.Ceiling((_eng.LimiterRpm + 1000f) / 1000f) * 1000f);

        // พื้นถนน (เหนือเกจ)
        Txt(g, "พื้นถนน  [V]", _fSm, Palette.Dim, 252, 116);
        for (int i = 0; i < Vehicle.Surfaces.Length; i++)
        {
            int si = i;
            Btn(g, new RectangleF(420 + i * 150, 108, 140, 44), Vehicle.Surfaces[i].Name, Vehicle.SurfaceIndex == i, Palette.Accent, () => SetSurface(si), _fSm);
        }
        var sf = Vehicle.Surfaces[Vehicle.SurfaceIndex];
        Txt(g, $"{sf.Note}   •   แรงเกาะ x{sf.MuK:0.00}   •   ต้านการกลิ้ง x{sf.CrrK:0.00}", _fXs, Palette.Dim, 254, 158);
        DrawGauge(g, 640, 552, 335);
        Btn(g, new RectangleF(868, 196, 168, 34), "M  จอ: " + new[] { "เข็ม", "XMAX", "FORZA" }[_save.GaugeMode], false, Palette.Blue, () => { _save.GaugeMode = (_save.GaugeMode + 1) % 3; _save.Save(); }, _fCap);
        DrawThrottle(g, 1056, 170, 64, 660);

        // ระดับเสียง + คลื่นเสียง (ใต้เกจ)
        Txt(g, "ระดับเสียง", _fSm, Palette.Dim, 270, 954);
        Slider(g, new RectangleF(430, 946, 560, 40), _eng.Volume, Palette.Accent, v => _eng.Volume = v);
        DrawScope(g, new RectangleF(270, 1000, 720, 64));

        // ---------- แผงขวา (เว้นช่องไฟระหว่างแผง 16 พิกเซล) ----------
        const float px = 1180, pw = 700;

        // ชุดส่งกำลัง
        PanelBox(g, new RectangleF(px, 100, pw, 176), auto ? "รถออโต้ (CVT)" : "รถเกียร์ธรรมดา");
        if (auto)
        {
            string w = d.Rollers == 6 && d.RollerG != d.RollerG2 ? $"{d.RollerG:0.#}/{d.RollerG2:0.#} g" : $"{d.RollerG:0.#} g";
            Txt(g, $"ชาม {d.RampDeg:0.0}°/{d.RampOutDeg:0.0}°  •  {d.Rollers} เม็ด {w}  •  สปริง {d.SpringK:0} N/มม.", _fMd, Palette.Text, px + 20, 140);
            Txt(g, $"{m.Cc:0} cc  •  {m.PeakHp:0.0} HP @ {m.PeakPsRpm:N0}", _fSm, Palette.Dim, px + 20, 176);
            Txt(g, $"อัตราทดตอนนี้ {_eng.CvtRatio * Cvt.Final(d):0.00} :1   (สายพาน {_eng.CvtRatio:0.00} x ทดท้าย {Cvt.Final(d):0.00})", _fSm, Palette.Text, px + 20, 208);
            Bar(g, new RectangleF(px + 20, 240, pw - 40, 12), _eng.CvtPosition, Palette.Accent);
            if (_autoDrive) Txt(g, AutopilotText(), _fXs, Palette.Green, px + 20, 254);
        }
        else
        {
            Txt(g, $"เกียร์ {d.Gears} สปีด  •  สเตอร์ {d.SprocketF:0}/{d.SprocketR:0}", _fMd, Palette.Text, px + 20, 140);
            Txt(g, $"{m.Cc:0} cc  •  {m.PeakHp:0.0} HP @ {m.PeakPsRpm:N0}", _fSm, Palette.Dim, px + 20, 176);
            Txt(g, _eng.Gear > 0 ? $"อัตราทด เกียร์ {_eng.Gear}:  {d.GearAt(_eng.Gear):0.000} :1   ทดรวม {Vehicle.Total(d, _eng.Gear):0.00} :1" : "อัตราทด: เกียร์ว่าง (N)", _fSm, Palette.Text, px + 20, 208);
            Txt(g, $"ทดสุดท้าย {Vehicle.Final(d):0.000}   รถ+คน {Vehicle.Mass(d):0} กก.", _fXs, Palette.Dim, px + 20, 240);
            if (_autoDrive) Txt(g, AutopilotText(), _fXs, Palette.Green, px + 20, 258);
        }
        Btn(g, new RectangleF(px + pw - 236, 108, 216, 34), _autoDrive ? "P ขับเอง: เปิด" : "P ขับเอง: ปิด", _autoDrive, Palette.Green, ToggleAutopilot, _fXs);

        // ปุ่มควบคุม
        const float bw = 163, bg = 16, by = 292, bh = 64;
        Btn(g, new RectangleF(px, by, bw, bh), "S สตาร์ท", _eng.StarterHeld, Palette.Green, null, _fMd,
            down: () => { _eng.Ignition = true; _mouseStarter = true; }, up: () => _mouseStarter = false);
        Btn(g, new RectangleF(px + (bw + bg), by, bw, bh), "K ดับเครื่อง", !_eng.Ignition, Palette.Red, () => _eng.Ignition = !_eng.Ignition, _fMd);
        Btn(g, new RectangleF(px + 2 * (bw + bg), by, bw, bh), "D ไดโน", _eng.DynoRunning, Palette.Green, StartDynoRun, _fMd);
        Btn(g, new RectangleF(px + 3 * (bw + bg), by, bw, bh), "G อู่", false, Palette.Accent, () => GoTo(Page.Garage), _fMd);

        // เกียร์ / คลัตช์
        PanelBox(g, new RectangleF(px, 372, pw, 148), auto ? "คลัตช์แรงเหวี่ยง (ไม่ต้องเปลี่ยนเกียร์ จับเองที่ ~3,200 รอบ)" : _eng.ClutchEnabled ? "เกียร์  (E ขึ้น  Q ลง  N ว่าง)  •  ค้าง Shift = กำครัตช์" : "เกียร์  (E ขึ้น  Q ลง  N ว่าง)  •  ครัตช์มือปิด เปลี่ยนเกียร์ได้ทันที");
        if (auto)
        {
            var kb = new RectangleF(px + 20, 424, pw - 40, 44);
            Bar(g, kb, _eng.ClutchLock, Palette.Green);
            TxtC(g, $"คลัตช์จับ {_eng.ClutchLock * 100f:0}%", _fSm, Palette.Text, kb.X + kb.Width / 2, kb.Y + kb.Height / 2);
        }
        else
        {
            int ng = m.D.Gears;
            float gsp = (pw - 40f) / (ng + 1);
            for (int i = 0; i <= ng; i++)
            {
                int gi = i;
                Btn(g, new RectangleF(px + 20 + i * gsp, 412, gsp - 12, 52), i == 0 ? "N" : i.ToString(), _gearSel == i, Palette.Accent, () => SelectGear(gi), _fLg);
            }
            if (_eng.ClutchEnabled)
            {
                var lv = new RectangleF(px + 20, 482, pw - 160, 14);
                Bar(g, lv, _eng.ClutchLever, _eng.ClutchLever > 0.6f ? Palette.Blue : Palette.Accent);
                var lvHit = new RectangleF(px + 20, 476, pw - 160, 26);
                AddHit(lvHit, _ => _mouseClutch = true, up: () => _mouseClutch = false);
                Txt(g, _eng.ClutchLever > 0.6f ? "กำครัตช์อยู่ (เปลี่ยนเกียร์ได้)" : "ครัตช์ปล่อย  [Shift หรือกดค้างแถบนี้]", _fXs, Palette.Dim, px + 22, 496);
            }
            else Txt(g, "ครัตช์มือปิด: ไม่ต้องกำครัตช์ เปลี่ยนเกียร์ได้เลย (คลัตช์แรงเหวี่ยงจับเอง)", _fXs, Palette.Dim, px + 22, 486);
            Btn(g, new RectangleF(px + pw - 130, 476, 112, 34), _eng.ClutchEnabled ? "C ครัตช์: เปิด" : "C ครัตช์: ปิด", _eng.ClutchEnabled, Palette.Blue, ToggleClutchSystem, _fXs);
        }

        // ถนน / ความชัน (แถวเดียว)
        string slopeText = _slope == 0 ? "ถนนราบ" : (_slope > 0 ? "ขึ้นเนิน +" : "ลงเนิน ") + _slope + "%";
        PanelBox(g, new RectangleF(px, 536, pw, 96), "ถนน / ความชัน   ( [ ] ปรับ )   " + slopeText);
        SmallBtn(g, new RectangleF(px + 20, 576, 52, 44), "−", () => ChangeSlope(-1));
        (string, int)[] tracks = { ("ราบ", 0), ("เนิน 6%", 6), ("ชัน 12%", 12), ("ลง −4%", -4) };
        for (int i = 0; i < tracks.Length; i++)
        {
            int val = tracks[i].Item2;
            Btn(g, new RectangleF(px + 82 + i * 132, 576, 124, 44), tracks[i].Item1, _slope == val, Palette.Blue, () => _slope = val, _fSm);
        }
        SmallBtn(g, new RectangleF(px + pw - 72, 576, 52, 44), "+", () => ChangeSlope(+1));

        // สถานะคลัตช์ / ยาง
        PanelBox(g, new RectangleF(px, 648, pw, 170), null);
        Btn(g, new RectangleF(px + 16, 654, 150, 32), "คลัตช์ / ยาง", _driveTab == 0, Palette.Green, () => _driveTab = 0, _fCap);
        Btn(g, new RectangleF(px + 174, 654, 150, 32), "Live data", _driveTab == 1, Palette.Blue, () => _driveTab = 1, _fCap);
        Btn(g, new RectangleF(px + 332, 654, 150, 32), "ความร้อน / สึก", _driveTab == 2, Palette.Red, () => _driveTab = 2, _fCap);
        float ry = 698;
        if (_driveTab == 2)
        {
            float T = _eng.EngT, visc = _eng.OilVisc, wear = _eng.Wear, oilT = _eng.OilT;
            Color tCol = T > 170f ? Palette.Red : T > 135f ? Palette.Yellow : T < 60f ? Palette.Blue : Palette.Green;
            string tTxt = T > 170f ? "ร้อนจัด! ลูกเสี่ยงละลาย" : T > 135f ? "ร้อน กำลังตก น็อกง่าย" : T < 60f ? "เครื่องยังเย็น" : "อุณหภูมิทำงาน";
            Color vCol = visc < 0.45f ? Palette.Red : visc < 0.7f ? Palette.Yellow : Palette.Green;
            string vTxt = visc < 0.45f ? "บางมาก ฟิล์มขาด" : visc < 0.7f ? "บาง" : visc > 1.8f ? "หนืด (เย็น)" : "ปกติ";
            Color wCol = wear > 0.5f ? Palette.Red : wear > 0.2f ? Palette.Yellow : Palette.Green;
            string wTxt = _eng.Seized ? _eng.SeizeCause : wear > 0.5f ? "ลูกสึกหนัก ไม่ข้ามรอบ แก๊ก" : wear > 0.2f ? "ลูกเริ่มสึก มีเสียงแก๊ก" : "ปกติ";
            Btn(g, new RectangleF(px + pw - 196, 654, 180, 32), "ซ่อม / เปลี่ยนน้ำมัน", _eng.Seized, Palette.Green, () => _eng.Repair(), _fCap);
            void TRow(string label, float frac, Color col, string value)
            {
                Txt(g, label, _fSm, Palette.Dim, px + 20, ry);
                Bar(g, new RectangleF(px + 190, ry + 6, 250, 16), frac, col);
                Txt(g, value, _fSm, col, px + 456, ry);
                ry += 34;
            }
            TRow("เครื่อง", Math.Clamp((T - 30f) / 190f, 0f, 1f), tCol, $"{T:0} °C  {tTxt}");
            TRow("น้ำมัน", Math.Clamp(visc / 2.2f, 0f, 1f), vCol, $"{oilT:0}°C  {visc:0.0}x  {vTxt}");
            TRow("ลูกสูบ", wear, wCol, $"สึก {wear * 100f:0}%  {wTxt}");
        }
        else if (_driveTab == 1)
        {
            var ec = _eng.Ecu; var mx = MixSymptom(ec.Lambda); float kn = _eng.KnockNow;
            var items = new (string lb, string val, Color col)[]
            {
                ("AFR", $"{ec.Afr:0.0}", mx.t != null ? mx.c : Palette.Text),
                ("จุดไฟ", $"{ec.Adv:0}° / MBT {ec.Mbt:0}°", MathF.Abs(ec.Adv - ec.Mbt) > 8f ? Palette.Yellow : Palette.Text),
                ("หัวฉีด", $"{ec.Duty * 100f:0}%  {ec.PulseMs:0.00} ms", ec.Duty > 0.85f ? Palette.Red : Palette.Text),
                ("EGT", $"{ec.Egt:0} °C", ec.Egt > 1050f ? Palette.Red : Palette.Text),
                ("น็อก", $"{kn * 100f:0}%", kn > 0.05f ? Palette.Red : Palette.Text),
                ("โหลด", $"{_eng.LoadNow * 100f:0}%", Palette.Text),
            };
            for (int i = 0; i < items.Length; i++)
            {
                float x = px + 20 + (i % 2) * (pw / 2), y = 696 + (i / 2) * 32;
                Txt(g, items[i].lb, _fXs, Palette.Dim, x, y + 5); Txt(g, items[i].val, _fSm, items[i].col, x + 96, y);
            }
            Txt(g, mx.t != null ? "อาการ: " + mx.t : "ส่วนผสมปกติ: ไม่มีอาการสะดุด/แห้ง", _fXs, mx.t != null ? mx.c : Palette.Green, px + 20, 796);
        }
        else {
        void SRow(string label, float frac, Color col, string value)
        {
            Txt(g, label, _fSm, Palette.Dim, px + 20, ry);
            Bar(g, new RectangleF(px + 190, ry + 6, 250, 16), frac, col);
            Txt(g, value, _fSm, col, px + 456, ry);
            ry += 34;
        }
        float lk = _eng.ClutchLock;
        SRow("คลัตช์จับ", lk, Palette.Green, $"{lk * 100f:0}%  " + (lk > 0.95f ? "จับสนิท" : lk > 0.05f ? "กำลังจับ" : "ปล่อย/ยังไม่จับ"));
        float slipRpm = MathF.Abs(_eng.ClutchSlipRpm);
        Color slipCol = lk < 0.05f ? Palette.Dim : slipRpm < 150f ? Palette.Green : slipRpm < 900f ? Palette.Yellow : Palette.Red;
        SRow("คลัตช์ลื่น", MathF.Min(slipRpm / 3000f, 1f), slipCol,
            lk < 0.05f ? "ไม่ส่งกำลัง" : $"{slipRpm:N0} rpm  " + (slipRpm < 150f ? "ไม่ลื่น" : slipRpm < 900f ? "ลื่นเล็กน้อย" : "ลื่น (ออกตัว)"));
        float tu = _eng.TractionUse;
        Color tCol = _eng.WheelSpin ? Palette.Red : tu > 0.85f ? Palette.Yellow : Palette.Green;
        SRow("แรงเกาะยาง", MathF.Min(tu, 1f), tCol, $"{tu * 100f:0}%  " + (_eng.WheelSpin ? "ล้อฟรี! ยางลื่น" : tu > 0.85f ? "ใกล้ลื่น" : "ยางเกาะดี"));
        Txt(g, $"แรงขับ {_eng.DriveForce:N0} N / เกาะได้สูงสุด {_eng.GripMax:N0} N  •  ยาง {d.TireCode} ขอบ {d.RimW:0.00}", _fXs, Palette.Dim, px + 20, ry - 2);
        }

        // สถิติ
        PanelBox(g, new RectangleF(px, 834, pw, 216), "สถิติ");
        // ดีสุด = สถิติสูงสุดที่เก็บไว้ในเซฟ | ก่อนแก้ = ดีสุดของชุดก่อนหน้า | ล่าสุด = ที่กำลังเทส
        // ผลต่าง = ล่าสุดเทียบก่อนแก้ (ถ้ายังไม่เคยแก้ เทียบกับดีสุด) + = ดีกว่า
        var bf = _before;
        TxtR(g, "ดีสุด", _fXs, Palette.Dim, px + 300, 846);
        TxtR(g, "ก่อนแก้", _fXs, Palette.Dim, px + 430, 846);
        TxtR(g, "ล่าสุด", _fXs, Palette.Dim, px + 560, 846);
        TxtR(g, bf != null ? "ต่างจากก่อนแก้" : "ต่างจากดีสุด", _fXs, Palette.Dim, px + pw - 20, 846);
        string N(float v, string f) => v <= 0f ? "-" : v.ToString(f);
        float sy = 878;
        void Row(string a, float best, float before, float last, bool lowerBetter, string f, string lastTxt = "")
        {
            Txt(g, a, _fSm, Palette.Dim, px + 20, sy);
            TxtR(g, N(best, f), _fSm, Palette.Text, px + 300, sy);
            TxtR(g, N(before, f), _fSm, Palette.Dim, px + 430, sy);
            TxtR(g, lastTxt.Length > 0 ? lastTxt : N(last, f), _fSm, Palette.Accent, px + 560, sy);
            float refV = bf != null ? before : best;
            if (refV > 0f && last > 0f)
            {
                float diff = lowerBetter ? refV - last : last - refV;
                string txt = (diff > 0.0049f ? "+" : "") + diff.ToString(f);
                Color c = MathF.Abs(diff) < 0.005f ? Palette.Dim : diff > 0f ? Palette.Green : Palette.Red;
                TxtR(g, txt, _fSm, c, px + pw - 20, sy);
            }
            else TxtR(g, "-", _fSm, Palette.Dim, px + pw - 20, sy);
            sy += 28;
        }
        float hpNow = _dyno != null ? _dyno.PeakPs * EngineModel.PsToHp : 0f;
        Row("รอบสูงสุด (rpm)", _cur.PeakRpm, bf?.PeakRpm ?? 0f, _peakRpm, false, "0");
        Row("0–60 (วิ)", _save.Best060, bf?.B060 ?? 0f, _last060, true, "0.00");
        Row("100 ม. (วิ)", _save.Best100, bf?.B100 ?? 0f, _last100, true, "0.00");
        Row(_last402 > 0f ? $"402 ม. (วิ) @{_trap402:0}" : "402 ม. (วิ)", _save.Best402, bf?.B402 ?? 0f, _last402, true, "0.00");
        Row("ท็อป (กม./ชม.)", _save.BestTop, bf?.Top ?? 0f, _topSession, false, "0");
        Row("แรงม้า (HP)", _cur.Hp, bf?.Hp ?? 0f, hpNow, false, "0.0");
    }

    // ---------- คันเร่ง ----------
    private void DrawThrottle(Graphics g, float x, float y, float w, float h)
    {
        TxtC(g, "คันเร่ง", _fSm, Palette.Dim, x + w / 2, y - 28);
        var r = new RectangleF(x, y, w, h);
        FillRR(g, r, 3, Color.FromArgb(14, 17, 21));

        float thr = _eng.Throttle;
        float fh = h * Math.Clamp(thr, 0f, 1f);
        if (fh > 2f)
        {
            var fill = new RectangleF(x, y + h - fh, w, fh);
            using var fb = new LinearGradientBrush(r, Palette.Red, Palette.Accent, LinearGradientMode.Vertical);
            g.FillRectangle(fb, fill);
        }
        DrawRR(g, r, 3, Palette.Border, 1f);
        TxtC(g, (int)(thr * 100f) + "%", _fMd, Palette.Text, x + w / 2, y + h + 32);

        bool braking = Down(Keys.Space) || Down(Keys.B);
        var br = new RectangleF(x - 6, y + h + 62, w + 12, 46);
        FillRR(g, br, 3, braking ? Palette.Red : Palette.Panel);
        DrawRR(g, br, 3, braking ? Palette.Red : Palette.Border, 1f);
        TxtC(g, "เบรก", _fSm, braking ? Color.White : Palette.Dim, br.X + br.Width / 2, br.Y + br.Height / 2);

        // ลากแถบด้วยเมาส์
        void Apply(PointF p)
        {
            _mouseThrottle = true;
            _mouseThrottleValue = Math.Clamp(1f - (p.Y - y) / h, 0f, 1f);
        }
        AddHit(new RectangleF(x - 30, y - 10, w + 60, h + 20), Apply, Apply, () => _mouseThrottle = false);
    }

    // ---------- เกจรอบ ----------
    private static PointF Polar(float cx, float cy, float angleDeg, float radius)
    {
        double a = angleDeg * Math.PI / 180.0;
        return new PointF(cx + (float)(Math.Cos(a) * radius), cy + (float)(Math.Sin(a) * radius));
    }

    private float GaugeAngle(float rpm) => 135f + 270f * Math.Clamp(rpm, 0f, _gaugeMax) / _gaugeMax;

    private Bitmap? _gaugeBmp;
    private (float max, float red, float r, float scale) _gaugeKey;

    /// <summary>ส่วนที่ไม่เปลี่ยนของเกจ (หน้าปัด วงโค้ง ขีด ตัวเลข) วาดครั้งเดียวลงภาพที่ความละเอียดจริงของจอ แล้ววางทับทุกเฟรม</summary>
    private void DrawGaugeStatic(Graphics g, float cx, float cy, float r, float redline)
    {
        float k = r / 215f;
        var arc = new RectangleF(cx - r, cy - r, r * 2, r * 2);

        g.FillEllipse(Br(Color.FromArgb(14, 17, 21)), cx - r - 26, cy - r - 26, (r + 26) * 2, (r + 26) * 2);
        g.DrawEllipse(Pn(Palette.Border, 3f), cx - r - 26, cy - r - 26, (r + 26) * 2, (r + 26) * 2);
        g.DrawEllipse(Pn(Color.FromArgb(30, 35, 42), 1.5f), cx - r + 34 * k, cy - r + 34 * k, (r - 34 * k) * 2, (r - 34 * k) * 2);
        g.DrawArc(Pn(Color.FromArgb(70, 76, 88), 10f * k), arc, 135f, 270f * redline / _gaugeMax);
        g.DrawArc(Pn(Palette.Red, 10f * k), arc, GaugeAngle(redline), 270f * (_gaugeMax - redline) / _gaugeMax);

        // ขีดและตัวเลข
        for (int rpm = 0; rpm <= (int)_gaugeMax; rpm += 500)
        {
            float a = GaugeAngle(rpm);
            bool major = rpm % 1000 == 0;
            g.DrawLine(major ? Pn(Palette.Text, 3f * k) : Pn(Palette.Dim, 1.5f * k),
                Polar(cx, cy, a, r - 8 * k), Polar(cx, cy, a, r - (major ? 30 : 20) * k));
            if (major)
            {
                var pt = Polar(cx, cy, a, r - 54 * k);
                TxtC(g, (rpm / 1000).ToString(), _fGaugeNum, rpm >= redline ? Palette.Red : Palette.Text, pt.X, pt.Y);
            }
        }
        TxtC(g, "x1000 rpm", _fSm, Palette.Dim, cx, cy - 80 * k);
    }

    /// <summary>ระยะจากจอด + ข้อความสถานะ (ใช้ร่วมกันทั้งจอเข็มและจอ TFT)</summary>
    private void DriveOverlay(Graphics g, float cx, float msgY)
    {
        Txt(g, "ระยะจากจอด", _fXs, Palette.Dim, 262, 800);
        Txt(g, _runDist < 0f ? "-" : (_runDist < 1000f ? $"{_runDist:0} ม." : $"{_runDist / 1000f:0.00} กม."), _fNum, Palette.Accent, 262, 826);
        Txt(g, _runDist < 0f ? "ออกตัวจากหยุดนิ่งเพื่อวัด" : $"{_runTime:0.0} วิ  " + (_runFull ? "• เต็มคันเร่ง" : "• ไม่เต็มคันเร่ง"), _fXs, _runDist >= 0f && _runFull ? Palette.Green : Palette.Dim, 262, 876);

        // ข้อความสถานะ
        if (_eng.DynoRunning)
            TxtC(g, "กำลังวัดไดโน...", _fBtn, Palette.Green, cx, msgY);
        else if (_eng.Seized)
            TxtC(g, "ลูกสูบล็อก! " + _eng.SeizeCause, _fBtn, Palette.Red, cx, msgY);
        else if (_eng.EngT > 150f)
            TxtC(g, "เครื่องร้อนจัด! ปล่อยคันเร่ง ให้ลมระบาย", _fBtn, Palette.Red, cx, msgY);
        else if (!_eng.Running)
            TxtC(g, _eng.Ignition ? "กด S ค้างเพื่อสตาร์ท" : "ดับเครื่องอยู่ - กด S ค้างเพื่อสตาร์ท", _fMd, Palette.Accent, cx, msgY);

    }

    /// <summary>จอ TFT แบบ XMAX: จอเล็กด้านบน (ความเร็ว/อุณหภูมิ/ระยะ) + จอใหญ่เกจรอบเป็นแท่งโค้งเรียงจากซ้ายไปขวา</summary>
    private void DrawGaugeTft(Graphics g, float cx, float cy)
    {
        float lim = _eng.LimiterRpm, red = lim - 600f, gm = _gaugeMax, T = _eng.EngT;
        bool hot = T > 135f;
        var ink = Color.FromArgb(236, 242, 248);
        var top = new RectangleF(cx - 330, cy - 330, 660, 150);
        FillRR(g, top, 14, Color.FromArgb(8, 11, 16)); DrawRR(g, top, 14, Color.FromArgb(46, 52, 62), 3f);
        TxtR(g, $"{_eng.SpeedKmh:0}", _fSpeed, ink, top.X + 250, top.Y + 14);
        Txt(g, "km/h", _fSm, Palette.Dim, top.X + 262, top.Y + 96);
        float gx = top.X + 340, gw = 280;
        Txt(g, "C", _fCap, Palette.Dim, gx - 22, top.Y + 22); Txt(g, "H", _fCap, hot ? Palette.Red : Palette.Dim, gx + gw + 8, top.Y + 22);
        int filled = (int)MathF.Round(Math.Clamp((T - 30f) / 160f, 0f, 1f) * 10f);
        for (int i = 0; i < 10; i++)
            g.FillRectangle(Br(i < filled ? (hot && i > 6 ? Palette.Red : ink) : Color.FromArgb(28, 34, 42)), gx + i * (gw / 10) + 2, top.Y + 16, gw / 10 - 6, 30);
        Txt(g, $"{T:0}°C", _fXs, hot ? Palette.Red : Palette.Dim, gx, top.Y + 54);
        Txt(g, _eng.IsAuto ? "AUTO " + _eng.CvtRatio.ToString("0.00") : "GEAR " + (_eng.Gear == 0 ? "N" : _eng.Gear.ToString()), _fSm, Palette.Text, gx + 150, top.Y + 52);
        Txt(g, "TRIP", _fCap, Palette.Dim, gx, top.Y + 98); TxtR(g, (_eng.DistanceM / 1000f).ToString("0.00") + " km", _fSm, ink, gx + gw, top.Y + 92);

        var tft = new RectangleF(cx - 350, cy - 160, 700, 340);
        FillRR(g, tft, 16, Color.FromArgb(4, 7, 12)); DrawRR(g, tft, 16, Color.FromArgb(46, 52, 62), 3f);
        var now = DateTime.Now;
        Txt(g, now.ToString("HH:mm"), _fMd, ink, tft.X + 28, tft.Y + 14);
        TxtR(g, $"{T:0}°C", _fMd, hot ? Palette.Red : ink, tft.Right - 28, tft.Y + 14);
        var abs = new RectangleF(tft.X + tft.Width / 2 - 28, tft.Y + 18, 56, 26);
        FillRR(g, abs, 13, _eng.WheelSpin ? Palette.Accent : Color.FromArgb(60, 50, 20));
        TxtC(g, "ABS", _fCap, _eng.WheelSpin ? Color.FromArgb(14, 14, 16) : Color.FromArgb(150, 120, 40), abs.X + 28, abs.Y + 13);
        float x0 = tft.X + 44, w = tft.Width - 88, yT = tft.Y + 70, yB = tft.Y + 262, yM = (yT + yB) / 2;
        int n = Math.Max(20, (int)MathF.Round(gm / 250f));
        for (int i = 0; i <= n; i++)
        {
            float rp = i * gm / n, x = x0 + w * i / n, bow = 40f * MathF.Pow(1f - (float)i / n, 1.5f) + 4f;
            bool on = rp <= _needleRpm, isRed = rp >= red;
            Color col = isRed ? (on ? Color.FromArgb(255, 72, 60) : Color.FromArgb(96, 28, 28))
                : on ? Color.FromArgb(Math.Clamp((int)(150 + 90 * (1 - (float)i / n)), 0, 255), Math.Clamp((int)(190 + 50 * (1 - (float)i / n)), 0, 255), 255) : Color.FromArgb(30, 56, 92);
            if (on && rp > _needleRpm - gm / n * 3f)       // หัวแท่งเรืองแสง
                using (var glow = new Pen(Color.FromArgb(70, isRed ? Palette.Red : Color.FromArgb(80, 150, 255)), 11f))
                    g.DrawBezier(glow, x, yT, x - bow, yM, x - bow, yM, x, yB);
            using var pen = new Pen(col, on ? 3.4f : 1.7f);
            g.DrawBezier(pen, x, yT, x - bow * 1.33f, yM - 20, x - bow * 1.33f, yM + 20, x, yB);
        }
        for (int r = 0; r <= (int)gm; r += 500)
        {
            float x = x0 + w * r / gm; bool major = r % 1000 == 0, on = r <= _needleRpm;
            if (major)
            {
                g.FillRectangle(Br(Color.FromArgb(200, 4, 7, 12)), x - 13, yM - 14, 26, 28);
                TxtC(g, (r / 1000).ToString(), _fSm, r >= red ? Palette.Red : on ? Color.FromArgb(240, 246, 255) : Color.FromArgb(110, 122, 140), x, yM);
            }
            else g.FillEllipse(Br(on ? Color.FromArgb(240, 246, 255) : Color.FromArgb(80, 90, 106)), x - 2.6f, yM - 2.6f, 5.2f, 5.2f);
        }
        TxtR(g, "x1000r/min", _fXs, Palette.Dim, tft.Right - 24, tft.Bottom - 36);

        TxtC(g, ((int)(MathF.Round(_needleRpm / 10f) * 10f)).ToString(), _fNumL, Palette.Text, cx, cy + 238);
        bool limiter = _eng.LimiterActive || _needleRpm > _eng.LimiterRpm - 150f;
        var lamp = new RectangleF(cx - 70, cy + 296, 140, 28);
        FillRR(g, lamp, 8, limiter ? Palette.Red : Color.FromArgb(50, 54, 62));
        TxtC(g, "ตัดรอบ", _fSm, limiter ? Color.White : Palette.Dim, lamp.X + lamp.Width / 2, lamp.Y + lamp.Height / 2);
        DriveOverlay(g, cx, cy - 172);
    }

    /// <summary>จอ TFT แบบ Honda Forza: มาตรรอบวงโค้ง + ความเร็วตัวใหญ่กลางจอ + มาตรด้านซ้าย + แถบเตือนด้านล่าง</summary>
    private void DrawGaugeForza(Graphics g, float cx, float cy)
    {
        float lim = _eng.LimiterRpm, red = lim - 600f, gm = _gaugeMax, T = _eng.EngT;
        bool hot = T > 135f;
        var ink = Color.FromArgb(214, 242, 234); var white = Color.FromArgb(236, 240, 244); var grey = Color.FromArgb(150, 160, 168);
        var pn = new RectangleF(cx - 345, cy - 262, 690, 480);
        FillRR(g, pn, 26, Color.FromArgb(7, 9, 13)); DrawRR(g, pn, 26, Color.FromArgb(58, 64, 74), 4f);
        TxtR(g, DateTime.Now.ToString("HH:mm"), _fMd, white, pn.Right - 34, pn.Y + 20);
        string warn = _eng.Seized ? "ลูกสูบล็อก! " + _eng.SeizeCause : T > 150f ? "เครื่องร้อนจัด!" : _eng.Wear > 0.5f ? "ลูกสึกหนัก ตรวจเครื่อง" : _eng.LimiterActive ? "ตัดรอบ" : "";
        if (warn.Length > 0) { var tr = new RectangleF(cx - 22, pn.Y + 14, 44, 38); FillRR(g, tr, 6, Color.FromArgb(240, 90, 40)); TxtC(g, "!", _fMd, Color.FromArgb(20, 12, 8), cx, pn.Y + 33); }
        float ax = cx, ay = cy + 10, ar = 195f;
        var arc = new RectangleF(ax - ar, ay - ar, ar * 2, ar * 2);
        float Ang(float rpm) => 150f + 240f * Math.Clamp(rpm, 0f, gm) / gm;
        using (var p = new Pen(Color.FromArgb(40, 50, 52), 22f)) g.DrawArc(p, arc, 150f, 240f * red / gm);
        using (var p = new Pen(Color.FromArgb(96, 40, 22), 22f)) g.DrawArc(p, arc, Ang(red), 240f * (gm - red) / gm);
        if (_needleRpm > 30f) using (var p = new Pen(Color.FromArgb(222, 242, 236), 22f)) g.DrawArc(p, arc, 150f, Ang(MathF.Min(_needleRpm, red)) - 150f);
        if (_needleRpm > red) using (var p = new Pen(Color.FromArgb(255, 96, 40), 22f)) g.DrawArc(p, arc, Ang(red), Ang(_needleRpm) - Ang(red));
        for (int r = 0; r <= (int)gm; r += 500)
        {
            float a = Ang(r) * MathF.PI / 180f; bool major = r % 1000 == 0;
            float c = MathF.Cos(a), s2 = MathF.Sin(a);
            g.DrawLine(major ? Pn(Color.FromArgb(214, 222, 228), 2.6f) : Pn(Color.FromArgb(96, 104, 112), 1.4f), ax + c * (ar + 16), ay + s2 * (ar + 16), ax + c * (ar + (major ? 34 : 26)), ay + s2 * (ar + (major ? 34 : 26)));
            if (major && (r / 1000) % 2 == 0) TxtC(g, (r / 1000).ToString(), _fSm, r >= red ? Color.FromArgb(255, 110, 60) : Color.FromArgb(222, 230, 236), ax + c * (ar - 40), ay + s2 * (ar - 40));
        }
        TxtC(g, "x1000 r/min", _fXs, grey, ax, ay - 125);
        TxtC(g, $"{_eng.SpeedKmh:0}", _fRpm, ink, ax, ay - 20);
        TxtC(g, "km/h", _fSm, Color.FromArgb(200, 210, 214), ax, ay + 62);
        float fx = pn.X + 38, fy = pn.Y + 150, fh = 190;
        Txt(g, "H", _fCap, hot ? Palette.Red : Color.FromArgb(190, 200, 206), fx + 2, fy - 28);
        int fill = (int)MathF.Round(Math.Clamp((T - 30f) / 160f, 0f, 1f) * 8f);
        for (int i = 0; i < 8; i++) g.FillRectangle(Br(8 - i <= fill ? (hot && i < 2 ? Palette.Red : ink) : Color.FromArgb(30, 38, 42)), fx, fy + i * (fh / 8), 30, fh / 8 - 5);
        Txt(g, "C", _fCap, Color.FromArgb(190, 200, 206), fx + 4, fy + fh + 6);
        float rx = pn.Right - 150;
        TxtR(g, _eng.IsAuto ? "D" : (_eng.Gear == 0 ? "N" : _eng.Gear.ToString()), _fNumL, white, pn.Right - 30, pn.Y + 100);
        TxtR(g, _eng.IsAuto ? "AUTO" : "GEAR", _fCap, grey, pn.Right - 30, pn.Y + 180);
        TxtR(g, "TRIP", _fCap, grey, pn.Right - 30, pn.Y + 330); TxtR(g, (_eng.DistanceM / 1000f).ToString("0.00") + " km", _fSm, white, pn.Right - 30, pn.Y + 352);
        var bar = new RectangleF(cx - 160, pn.Bottom - 86, 320, 56);
        FillRR(g, bar, 8, warn.Length > 0 ? Color.FromArgb(240, 96, 40) : Color.FromArgb(24, 30, 34));
        TxtC(g, warn.Length > 0 ? "! " + warn : $"{(int)(MathF.Round(_needleRpm / 10f) * 10f)} rpm", _fSm, warn.Length > 0 ? Color.FromArgb(24, 14, 8) : Color.FromArgb(222, 230, 236), cx, bar.Y + 28);
        DriveOverlay(g, cx, cy - 288);
    }

    private void DrawGauge(Graphics g, float cx, float cy, float r)
    {
        if (_save.GaugeMode == 2) { DrawGaugeForza(g, cx, cy); return; }
        if (_save.GaugeMode == 1) { DrawGaugeTft(g, cx, cy); return; }
        float redline = _eng.RedlineRpm;
        float k = r / 215f;

        // ส่วนนิ่งจากแคช (สร้างใหม่เมื่อสเกลหน้าปัด/เส้นแดง/ขนาดจอเปลี่ยน)
        var key = (_gaugeMax, redline, r, _scale);
        float pad = r + 40f;
        if (_gaugeBmp == null || key != _gaugeKey)
        {
            _gaugeKey = key;
            _gaugeBmp?.Dispose();
            int px = (int)MathF.Ceiling(pad * 2f * _scale);
            _gaugeBmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using var gg = Graphics.FromImage(_gaugeBmp);
            gg.SmoothingMode = SmoothingMode.AntiAlias;
            gg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            gg.ScaleTransform(_scale, _scale);
            gg.TranslateTransform(-(cx - pad), -(cy - pad));
            DrawGaugeStatic(gg, cx, cy, r, redline);
        }
        g.DrawImage(_gaugeBmp, new RectangleF(cx - pad, cy - pad, pad * 2f, pad * 2f));

        // ตัวเลขรอบ
        int shown = (int)(MathF.Round(_needleRpm / 10f) * 10f);
        TxtC(g, shown.ToString(), _fRpm, Palette.Text, cx, cy + 105 * k);

        // ไฟตัดรอบ
        bool limiter = _eng.LimiterActive || _needleRpm > _eng.LimiterRpm - 150f;
        var lamp = new RectangleF(cx - 70 * k, cy + 148 * k, 140 * k, 26 * k);
        FillRR(g, lamp, 8, limiter ? Palette.Red : Color.FromArgb(50, 54, 62));
        TxtC(g, "ตัดรอบ", _fSm, limiter ? Color.White : Palette.Dim, lamp.X + lamp.Width / 2, lamp.Y + lamp.Height / 2);

        // เกียร์ + ความเร็ว
        string gear = _eng.IsAuto ? "ออโต้ " + _eng.CvtRatio.ToString("0.00") : "เกียร์ " + (_eng.Gear == 0 ? "N" : _eng.Gear.ToString());
        TxtC(g, $"{_eng.SpeedKmh:0}", _fSpeed, Palette.Accent, cx, cy + 205 * k);
        TxtC(g, $"กม./ชม.     {gear}", _fSm, Palette.Text, cx, cy + 238 * k);
        DriveOverlay(g, cx, cy - 112 * k);

        // เข็ม
        float na = GaugeAngle(_needleRpm);
        using (var needle = new Pen(Palette.Accent, 9f * k) { StartCap = LineCap.Round, EndCap = LineCap.Triangle })
            g.DrawLine(needle, Polar(cx, cy, na + 180f, 26 * k), Polar(cx, cy, na, r - 26 * k));
        g.FillEllipse(Br(Color.FromArgb(60, 66, 78)), cx - 16 * k, cy - 16 * k, 32 * k, 32 * k);
    }

    // ====================================================================
    //  ผลไดโน (หน้าต่างทับ)
    // ====================================================================
}
