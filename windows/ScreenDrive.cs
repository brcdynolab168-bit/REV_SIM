using System.Drawing.Drawing2D;

namespace RevGame;

public sealed partial class MainForm
{
    private float _gaugeMax = 11000f;               // สเกลหน้าปัดตามรอบตัดของเครื่องที่ประกอบอยู่

    // ====================================================================
    //  หน้าขับ
    // ====================================================================

    private void DrawDrive(Graphics g)
    {
        DrawTopBar(g, "ห้องทดสอบ",
            "W เร่งสุด  •  X ค่อย ๆ เร่ง (ไม่ตัดรอบ)  •  Z ค่อย ๆ เร่ง (แตะรอบตัดเบา ๆ)  •  Space เบรก  •  S สตาร์ท  •  E/Q เกียร์  •  D ไดโน  •  [ ] ความชัน  •  P ขับเอง  •  G อู่");

        var m = _eng.Model;
        var d = m.D;
        bool auto = d.Auto == 1;
        _gaugeMax = Math.Max(10000f, MathF.Ceiling((_eng.LimiterRpm + 1000f) / 1000f) * 1000f);

        DrawGauge(g, 640, 552, 335);
        DrawThrottle(g, 1056, 170, 64, 660);

        // ---------- แผงขวา ----------
        const float px = 1180, pw = 700;

        // ชุดส่งกำลัง
        PanelBox(g, new RectangleF(px, 100, pw, 200), auto ? "รถออโต้ (CVT)" : "รถเกียร์ธรรมดา");
        if (auto)
        {
            string w = d.Rollers == 6 && d.RollerG != d.RollerG2 ? $"{d.RollerG:0.#}/{d.RollerG2:0.#} g" : $"{d.RollerG:0.#} g";
            Txt(g, $"ชาม {d.RampDeg:0.0}°  •  {d.Rollers} เม็ด {w}  •  สปริง {d.SpringK:0} N/มม.", _fMd, Palette.Text, px + 20, 142);
            Txt(g, $"{m.Cc:0} cc  •  {m.PeakHp:0.0} HP @ {m.PeakPsRpm:N0}", _fSm, Palette.Dim, px + 20, 184);
            Txt(g, $"อัตราทดสายพาน {_eng.CvtRatio:0.00}  •  เฟืองท้าย {d.GearF}/{d.GearR} x ลดขั้นแรก {d.FirstRed:0.00} (ทดรวม {Cvt.Final(d):0.00})", _fSm, Palette.Dim, px + 20, 222);
            Bar(g, new RectangleF(px + 20, 262, pw - 40, 16), _eng.CvtPosition, Palette.Accent);
        }
        else
        {
            Txt(g, $"เกียร์ 4 สปีด  •  สเตอร์ {d.SprocketF:0}/{d.SprocketR:0}", _fMd, Palette.Text, px + 20, 142);
            Txt(g, $"{m.Cc:0} cc  •  {m.PeakHp:0.0} HP @ {m.PeakPsRpm:N0}", _fSm, Palette.Dim, px + 20, 184);
            Txt(g, $"ทดสุดท้าย {Vehicle.Final(d):0.000}   รถ+คน {Vehicle.Mass(d):0} กก.", _fSm, Palette.Dim, px + 20, 222);
        }

        Btn(g, new RectangleF(px + pw - 236, 108, 216, 34), _autoDrive ? "P ขับเอง: เปิด" : "P ขับเอง: ปิด", _autoDrive, Palette.Green, ToggleAutopilot, _fXs);
        if (_autoDrive) Txt(g, AutopilotText(), _fXs, Palette.Green, px + 20, 278);

        // ปุ่มควบคุม
        const float bw = 163, bg = 16, by = 316, bh = 70;
        Btn(g, new RectangleF(px, by, bw, bh), "S สตาร์ท", _eng.StarterHeld, Palette.Green, null, _fMd,
            down: () => { _eng.Ignition = true; _mouseStarter = true; }, up: () => _mouseStarter = false);
        Btn(g, new RectangleF(px + (bw + bg), by, bw, bh), "K ดับเครื่อง", !_eng.Ignition, Palette.Red, () => _eng.Ignition = !_eng.Ignition, _fMd);
        Btn(g, new RectangleF(px + 2 * (bw + bg), by, bw, bh), "D ไดโน", _eng.DynoRunning, Palette.Green, StartDynoRun, _fMd);
        Btn(g, new RectangleF(px + 3 * (bw + bg), by, bw, bh), "G อู่", false, Palette.Accent, () => GoTo(Page.Garage), _fMd);

        // เกียร์ / คลัตช์
        PanelBox(g, new RectangleF(px, 400, pw, 138), auto ? "คลัตช์แรงเหวี่ยง (ไม่ต้องเปลี่ยนเกียร์ จับเองที่ ~3,200 รอบ)" : _eng.ClutchEnabled ? "เกียร์  (E ขึ้น  Q ลง  N ว่าง)  •  ค้าง Shift = กำครัตช์" : "เกียร์  (E ขึ้น  Q ลง  N ว่าง)  •  ครัตช์มือปิด เปลี่ยนเกียร์ได้ทันที");
        if (auto)
        {
            var kb = new RectangleF(px + 20, 452, pw - 40, 40);
            Bar(g, kb, _eng.ClutchLock, Palette.Green);
            TxtC(g, $"คลัตช์จับ {_eng.ClutchLock * 100f:0}%", _fSm, Palette.Text, kb.X + kb.Width / 2, kb.Y + kb.Height / 2);
        }
        else
        {
            string[] names = { "N", "1", "2", "3", "4" };
            for (int i = 0; i < 5; i++)
            {
                int gi = i;
                Btn(g, new RectangleF(px + 20 + i * 136, 440, 120, 54), names[i], _gearSel == i, Palette.Accent, () => SelectGear(gi), _fLg);
            }
            if (_eng.ClutchEnabled)
            {
                var lv = new RectangleF(px + 20, 500, pw - 160, 14);
                Bar(g, lv, _eng.ClutchLever, _eng.ClutchLever > 0.6f ? Palette.Blue : Palette.Accent);
                var lvHit = new RectangleF(px + 20, 496, pw - 160, 22);
                AddHit(lvHit, _ => _mouseClutch = true, up: () => _mouseClutch = false);
                Txt(g, _eng.ClutchLever > 0.6f ? "กำครัตช์อยู่ (เปลี่ยนเกียร์ได้)" : "ครัตช์ปล่อย  [Shift หรือกดค้างแถบนี้]", _fXs, Palette.Dim, px + 24, 516);
            }
            else Txt(g, "ครัตช์มือปิด: ไม่ต้องกำครัตช์ เปลี่ยนเกียร์ได้เลย (คลัตช์แรงเหวี่ยงจับเอง)", _fXs, Palette.Dim, px + 24, 508);
            Btn(g, new RectangleF(px + pw - 130, 496, 112, 34), _eng.ClutchEnabled ? "C ครัตช์: เปิด" : "C ครัตช์: ปิด", _eng.ClutchEnabled, Palette.Blue, ToggleClutchSystem, _fXs);
        }

        // ถนน / ความชัน
        PanelBox(g, new RectangleF(px, 540, pw, 164), "ถนน / ความชัน   ( [ ] ปรับ )");
        string slopeText = _slope == 0 ? "ถนนราบ" : (_slope > 0 ? "ขึ้นเนิน +" : "ลงเนิน ") + _slope + "%";
        SmallBtn(g, new RectangleF(px + 20, 592, 70, 44), "−", () => ChangeSlope(-1));
        TxtC(g, slopeText, _fMd, _slope == 0 ? Palette.Text : Palette.Accent, px + pw / 2, 614);
        SmallBtn(g, new RectangleF(px + pw - 90, 592, 70, 44), "+", () => ChangeSlope(+1));
        (string, int)[] tracks = { ("ราบ", 0), ("เนิน 6%", 6), ("ชัน 12%", 12), ("ลง −4%", -4) };
        for (int i = 0; i < tracks.Length; i++)
        {
            int val = tracks[i].Item2;
            Btn(g, new RectangleF(px + 20 + i * 166, 646, 154, 44), tracks[i].Item1, _slope == val, Palette.Blue, () => _slope = val, _fSm);
        }

        // ระดับเสียง
        Txt(g, "ระดับเสียง", _fSm, Palette.Dim, px, 726);
        Slider(g, new RectangleF(px + 160, 718, pw - 160, 40), _eng.Volume, Palette.Accent, v => _eng.Volume = v);

        // สถิติ
        PanelBox(g, new RectangleF(px, 776, pw, 204), "สถิติ");
        // ดีสุด = สถิติสูงสุดที่เก็บไว้ในเซฟ | ก่อนแก้ = ดีสุดของชุดก่อนหน้า | ล่าสุด = ที่กำลังเทส
        // ผลต่าง = ล่าสุดเทียบก่อนแก้ (ถ้ายังไม่เคยแก้ เทียบกับดีสุด) + = ดีกว่า
        var bf = _before;
        TxtR(g, "ดีสุด", _fXs, Palette.Dim, px + 300, 788);
        TxtR(g, "ก่อนแก้", _fXs, Palette.Dim, px + 430, 788);
        TxtR(g, "ล่าสุด", _fXs, Palette.Dim, px + 560, 788);
        TxtR(g, bf != null ? "ต่างจากก่อนแก้" : "ต่างจากดีสุด", _fXs, Palette.Dim, px + pw - 20, 788);
        string N(float v, string f) => v <= 0f ? "-" : v.ToString(f);
        float sy = 816;
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
            sy += 27;
        }
        float hpNow = _dyno != null ? _dyno.PeakPs * EngineModel.PsToHp : 0f;
        Row("รอบสูงสุด (rpm)", _cur.PeakRpm, bf?.PeakRpm ?? 0f, _peakRpm, false, "0");
        Row("0–60 (วิ)", _save.Best060, bf?.B060 ?? 0f, _last060, true, "0.00");
        Row("100 ม. (วิ)", _save.Best100, bf?.B100 ?? 0f, _last100, true, "0.00");
        Row(_last402 > 0f ? $"402 ม. (วิ) @{_trap402:0}" : "402 ม. (วิ)", _save.Best402, bf?.B402 ?? 0f, _last402, true, "0.00");
        Row("ท็อป (กม./ชม.)", _save.BestTop, bf?.Top ?? 0f, _topSession, false, "0");
        Row("แรงม้า (HP)", _cur.Hp, bf?.Hp ?? 0f, hpNow, false, "0.0");

        DrawScope(g, new RectangleF(px, 992, pw, 64));
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

    private void DrawGauge(Graphics g, float cx, float cy, float r)
    {
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

        // ข้อความสถานะ
        if (_eng.DynoRunning)
            TxtC(g, "กำลังวัดไดโน...", _fBtn, Palette.Green, cx, cy - 112 * k);
        else if (!_eng.Running)
            TxtC(g, _eng.Ignition ? "กด S ค้างเพื่อสตาร์ท" : "ดับเครื่องอยู่ - กด S ค้างเพื่อสตาร์ท", _fMd, Palette.Accent, cx, cy - 112 * k);

        // เข็ม
        float na = GaugeAngle(_needleRpm);
        using (var needle = new Pen(Palette.Accent, 9f * k) { StartCap = LineCap.Round, EndCap = LineCap.Triangle })
            g.DrawLine(needle, Polar(cx, cy, na + 180f, 26 * k), Polar(cx, cy, na, r - 26 * k));
        g.FillEllipse(Br(Color.FromArgb(60, 66, 78)), cx - 16 * k, cy - 16 * k, 32 * k, 32 * k);
    }

    // ====================================================================
    //  ผลไดโน (หน้าต่างทับ)
    // ====================================================================

    private void DrawDynoResult(Graphics g)
    {
        var res = _dyno!;
        // พื้นที่คลิกเต็มจอ ลงทะเบียนท้ายสุดจึงบังปุ่มข้างหลังทั้งหมด
        AddHit(new RectangleF(0, 0, VW, VH), _ => _showDyno = false);
        FillRR(g, new RectangleF(0, 0, VW, VH), 0, Color.FromArgb(190, 0, 0, 0));

        var box = new RectangleF(260, 120, 1400, 840);
        FillRR(g, box, 4, Color.FromArgb(252, 17, 20, 25));
        DrawRR(g, box, 4, Palette.Accent, 2f);

        Txt(g, $"ผลวัดไดโน  ({res.Model.Cc:0} cc)", _fTitle, Palette.Accent, box.X + 40, box.Y + 24);

        var rpm = new float[res.Tq.Length];
        var ps = new float[res.Tq.Length];
        for (int i = 0; i < rpm.Length; i++)
        {
            rpm[i] = i * 100f;
            ps[i] = float.IsNaN(res.Tq[i]) ? float.NaN : EngineModel.Hp(res.Tq[i], rpm[i]);
        }
        DrawChart(g, new RectangleF(box.X + 40, box.Y + 110, box.Width - 80, 460), rpm, res.Tq, ps, res.Model.D.Limiter);

        float y = box.Y + 600;
        Txt(g, $"กำลังสูงสุด  {res.PeakPs * EngineModel.PsToHp:0.0} HP @ {res.PeakPsRpm:N0} rpm", _fLg, Palette.Accent, box.X + 40, y);
        Txt(g, $"แรงบิดสูงสุด  {res.PeakTq:0.0} N·m @ {res.PeakTqRpm:N0} rpm", _fLg, Palette.Blue, box.X + 40, y + 52);
        Txt(g, $"ซีซี {res.Model.Cc:0}   •   HP ต่อลิตร = {res.PeakPs * EngineModel.PsToHp / res.Model.Cc * 1000f:0}", _fMd, Palette.Text, box.X + 40, y + 112);
        var rd = res.Model.D;
        if (rd.RealHp > 0f || rd.RealTq > 0f)
        {
            string cmp = "เทียบรถจริง:  ";
            if (rd.RealHp > 0f) cmp += $"แรงม้า เกม {res.PeakPs * EngineModel.PsToHp:0.0} / จริง {rd.RealHp:0.0} HP ({(res.PeakPs * EngineModel.PsToHp / rd.RealHp - 1f) * 100f:+0;-0;0}%)   ";
            if (rd.RealTq > 0f) cmp += $"แรงบิด เกม {res.PeakTq:0.0} / จริง {rd.RealTq:0.0} N·m ({(res.PeakTq / rd.RealTq - 1f) * 100f:+0;-0;0}%)";
            Txt(g, cmp, _fMd, Palette.Yellow, box.X + 40, y + 160);
        }
        TxtR(g, "คลิก / Enter เพื่อปิด", _fSm, Palette.Dim, box.Right - 30, box.Bottom - 46);
    }
}
