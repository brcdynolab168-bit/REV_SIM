namespace RevGame;

/// <summary>หน้าจูนกล่อง (ออกแบบใหม่): ตาราง 20x20 ซ้าย + เครื่องจูนสด/ข้อมูลช่อง/กราฟแรงบิดขวา + หน้าอธิบายศัพท์</summary>
public sealed partial class MainForm
{
    private bool _benchOn, _benchApplied, _benchDirty, _showEcuHelp;
    private readonly List<PointF> _benchTrail = new();

    private static readonly (string Title, string Body)[] EcuHelp =
    {
        ("AFR และ λ (แลมบ์ดา)",
         "AFR = มวลอากาศ ÷ มวลน้ำมันที่เข้าห้องเผาไหม้  เบนซินเผาหมดพอดี (สโตอิก) = 14.7 : 1\n" +
         "λ = AFR ÷ สโตอิก   λ 1.00 = พอดี   ต่ำกว่า 1 = หนา   สูงกว่า 1 = บาง\n" +
         "แรงสูงสุดที่ AFR ~12.5–13.2 (λ 0.85–0.90)  ประหยัดตอนวิ่งเบา ~14.7\n" +
         "บางเกิน (>15.5) = ร้อน น็อก แรงตก   หนาเกิน (<11) = แรงตก เปลืองน้ำมัน เขม่า\n" +
         "E85 สโตอิก 9.76 / เมทานอล 6.45 ต้องฉีดมากกว่าเบนซินหลายเท่า"),
        ("MBT และ ไฟแก่ / ไฟอ่อน",
         "MBT = Minimum advance for Best Torque คือองศาจุดระเบิดก่อนศูนย์ตายบน (°BTDC) ที่ได้แรงบิดสูงสุดพอดี\n" +
         "ไฟแก่ = จุดเร็ว (องศามาก)   ไฟอ่อน = จุดช้า (องศาน้อย)\n" +
         "แก่กว่า MBT = แรงดันพีคมาเร็วเกินจนต้านลูกสูบขาขึ้น แรงไม่เพิ่มแต่น็อกง่าย\n" +
         "อ่อนกว่า MBT = แรงตก ไอเสียร้อน\n" +
         "MBT สูงขึ้นเมื่อรอบสูง โหลดเบา ส่วนผสมบาง ลูกโต / ต่ำลงเมื่อ CR สูง ก้านยาว น้ำมันเผาไว (E85/เมทานอล)\n" +
         "ตั้งต่ำกว่า MBT 1–3° เป็นขอบปลอดภัย"),
        ("EGT (อุณหภูมิก๊าซไอเสีย)",
         "EGT = Exhaust Gas Temperature อุณหภูมิก๊าซไอเสียที่คอท่อ บอกว่าเผาไหม้ร้อนแค่ไหน\n" +
         "เบนซินเครื่องเล็กตอนวิ่งหนักปกติ ~700–900°C   เกิน ~1,000°C เสี่ยงวาล์วไอเสียไหม้ ลูกสูบหลอม\n" +
         "ที่ทำให้สูง: ส่วนผสมบาง, ไฟอ่อน (ไฟยังเผาอยู่ตอนไอเสียเปิด), โหลดและรอบสูง\n" +
         "ส่วนผสมหนากว่าเล็กน้อยช่วยลด EGT (น้ำมันระเหยดูดความร้อน) แต่หนาเกินแรงตก\n" +
         "ค่าในเกมเป็นการประมาณ ใช้ดูแนวโน้ม"),
        ("หัวฉีด (Duty)",
         "Duty = สัดส่วนเวลาที่หัวฉีดเปิดต่อ 1 รอบเครื่อง\n" +
         "ยิ่งรอบสูง โหลดหนัก หรือใช้น้ำมันที่ต้องฉีดเยอะ (E85 เมทานอล) ค่ายิ่งสูง\n" +
         "เกิน ~85% ใกล้สุดกำลังหัวฉีด   100% = เต็ม น้ำมันไม่พอ ส่วนผสมบางลงเอง (ร้อน แรงตก)\n" +
         "แก้ด้วยหัวฉีดที่ใหญ่ขึ้น (แท็บ อัด·น้ำมัน แถว หัวฉีด)"),
        ("น็อก (Knock)",
         "การเผาไหม้ระเบิดเองก่อนเปลวไฟมาถึง (เสียงกริ๊งโลหะ) แรงดันกระแทกลูกสูบ\n" +
         "เกิดเมื่อกำลังอัดสูง ออกเทนต่ำ ไฟแก่เกิน ส่วนผสมบาง หรือโหลดหนักรอบต่ำ\n" +
         "แก้: ลดองศา เติมน้ำมันเพิ่มเล็กน้อย ใช้ออกเทนสูงขึ้น หรือลด CR\n" +
         "ในเกมน็อกทำให้แรงบิดตกและเสียงหยาบ"),
        ("ลำดับจูนที่แนะนำ",
         "1) ตั้ง AFR ก่อน: โหลดหนักให้หนาเล็กน้อย (~12.8–13.0) โหลดเบา ~14.7\n" +
         "2) เดินองศาทีละ 1° ที่โหลดสูง ดูแรงบิดบนกราฟ / น็อก / EGT\n" +
         "3) แรงบิดหยุดเพิ่ม = ถึง MBT แล้ว ให้ถอยกลับ 2°\n" +
         "4) ตรวจ Duty ไม่ควรเกิน 85%   5) เกลี่ยตารางให้เรียบ ไม่กระโดด\n" +
         "6) เปิดเครื่องจูนสด เร่งเครื่อง ดูจุดวิ่งบนตาราง แล้วแก้ช่องที่น็อก/ร้อน"),
    };

    private void DrawLiveBar(Graphics g, float x, float y, float w, string label, string value, float frac, Color col, float marker = -1f)
    {
        Txt(g, label, _fXs, Palette.Dim, x, y);
        TxtR(g, value, _fXs, col, x + w, y);
        var bar = new RectangleF(x, y + 28, w, 10);
        Bar(g, bar, frac, col);
        if (marker >= 0f) g.FillRectangle(Br(Palette.Text), bar.X + Math.Clamp(marker, 0f, 1f) * bar.Width - 1.5f, bar.Y - 4, 3, bar.Height + 8);
    }

    /// <summary>สเปกตรัมความร้อน: 0 = น้ำเงิน → เขียว → เหลือง → 1 = แดง</summary>
    private static Color Heat(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float h = 240f * (1f - t) / 60f, sat = 0.78f, v = 0.88f;
        int i = (int)Math.Floor(h) % 6; float f = h - MathF.Floor(h);
        float pp = v * (1f - sat), q = v * (1f - sat * f), tt = v * (1f - sat * (1f - f));
        (float R, float G, float B) = i switch { 0 => (v, tt, pp), 1 => (q, v, pp), 2 => (pp, v, tt), 3 => (pp, q, v), 4 => (tt, pp, v), _ => (v, pp, q) };
        return Color.FromArgb((int)(R * 255), (int)(G * 255), (int)(B * 255));
    }
    private static float Lum(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

    private void DrawEcu(Graphics g)
    {
        var m = _preview;
        bool bench = _benchOn && _benchApplied;
        const float LX = 252f, labW = 56f, cw = 47f, cellH = 30f, gridY = 248f, hdrY = 224f;
        float gx = LX + labW;

        // ---------- แถบโหมด ----------
        for (int i = 0; i < 3; i++)
        {
            int mi = i;
            Btn(g, new RectangleF(LX + i * 198, 176, 190, 40), EcuModeNames[i], _ecuMode == i, i == 1 ? Palette.Red : i == 0 ? Palette.Blue : Palette.Green, () => _ecuMode = mi, _fXs);
        }
        Btn(g, new RectangleF(LX + 940 + labW - 200, 176, 200, 40), "?  อธิบาย EGT MBT AFR", false, Palette.Yellow, () => _showEcuHelp = true, _fXs);

        // ---------- ตาราง ----------
        EcuRange(out int sc0, out int sc1, out int sr0, out int sr1);
        float fMin = float.MaxValue, fMax = float.MinValue;      // ช่วงเวลาฉีดของตาราง ใช้ไล่สี
        if (_ecuMode == 0) for (int rr0 = 0; rr0 < EN; rr0++) for (int cc0 = 0; cc0 < EN; cc0++) { float vv = m.CellMs(cc0, rr0); fMin = MathF.Min(fMin, vv); fMax = MathF.Max(fMax, vv); }
        Txt(g, "x1000", _fCap, Palette.Dim, LX + 4, hdrY + 2);
        for (int c = 0; c < EN; c++)
            TxtC(g, (m.EcuRpmAt(c) / 1000f).ToString("0.0"), _fCap, c >= sc0 && c <= sc1 ? Palette.Accent : Palette.Dim, gx + (c + 0.5f) * cw, hdrY + 10);
        for (int dr = 0; dr < EN; dr++)
        {
            int r = EN - 1 - dr;
            float y = gridY + dr * cellH;
            TxtC(g, $"{(r + 1) * 5}%", _fCap, r >= sr0 && r <= sr1 ? Palette.Accent : Palette.Dim, LX + labW / 2 - 4, y + cellH / 2);
            for (int c = 0; c < EN; c++)
            {
                var e = m.EcuAt(m.EcuRpmAt(c), EngineModel.EcuLoadAt(r));
                Color col; string txt; float dMbt = 0f;
                if (_ecuMode == 0)
                {
                    float v = m.CellMs(c, r);
                    col = Heat((v - fMin) / MathF.Max(0.01f, fMax - fMin)); txt = v >= 10f ? v.ToString("0.0") : v.ToString("0.00");
                }
                else if (_ecuMode == 1)
                {
                    float v = m.CellAdv(c, r);
                    dMbt = v - e.Mbt;
                    col = Heat(v / 45f); txt = v.ToString("0.0");
                }
                else
                {
                    col = Heat((e.Lambda - 0.7f) / 0.6f); txt = e.Afr.ToString("0.0");
                }
                var cell = new RectangleF(gx + c * cw, y, cw - 1, cellH - 1);
                g.FillRectangle(Br(col), cell);
                TxtC(g, txt, _fCap, Lum(col) > 135f ? Color.FromArgb(18, 20, 24) : Color.White, cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
                if (_ecuMode == 1)      // มุมขวาบน: เทียบ MBT  แดง = ไฟแก่เกิน  เขียว = ใกล้ MBT  น้ำเงิน = ไฟอ่อน
                {
                    var mk = dMbt > 1f ? Color.FromArgb(255, 30, 30) : dMbt > -2.5f ? Color.FromArgb(255, 255, 255) : Color.FromArgb(20, 20, 30);
                    g.FillPolygon(Br(mk), new[] { new PointF(cell.Right - 9, cell.Y), new PointF(cell.Right, cell.Y), new PointF(cell.Right, cell.Y + 9) });
                }
            }
        }
        for (int c = 0; c < EN; c++)          // เส้นไดโน (คันเร่งสุด) = จุดขาว
        {
            int r = Math.Clamp((int)MathF.Round(m.Load(1f, m.EcuRpmAt(c)) * EN - 1f), 0, EN - 1);
            g.FillEllipse(Br(Palette.Text), gx + (c + 0.5f) * cw - 3, gridY + (EN - 1 - r) * cellH + cellH - 8, 6, 6);
        }
        g.DrawRectangle(Pn(Palette.Accent, 3f), gx + sc0 * cw, gridY + (EN - 1 - sr1) * cellH, (sc1 - sc0 + 1) * cw - 1, (sr1 - sr0 + 1) * cellH - 1);

        // จุดทำงานสด (ติดเครื่องอยู่): วงกลม + ร่องรอย
        if (bench && _eng.Running)
        {
            float colF = Math.Clamp((_eng.Rpm - 1000f) / (m.EcuRpmMax - 1000f) * (EN - 1), 0f, EN - 1f);
            float rowF = Math.Clamp(_eng.LoadNow * EN - 1f, 0f, EN - 1f);
            var pt = new PointF(gx + (colF + 0.5f) * cw, gridY + (EN - 1 - rowF + 0.5f) * cellH);
            _benchTrail.Add(pt);
            if (_benchTrail.Count > 50) _benchTrail.RemoveAt(0);
            for (int i = 0; i < _benchTrail.Count; i++)
            {
                int a = 20 + i * 4;
                g.FillEllipse(Br(Color.FromArgb(Math.Min(a, 200), 255, 255, 255)), _benchTrail[i].X - 3, _benchTrail[i].Y - 3, 6, 6);
            }
            g.DrawEllipse(Pn(Palette.Text, 3f), pt.X - 10, pt.Y - 10, 20, 20);
            g.DrawEllipse(Pn(Color.Black, 1f), pt.X - 13, pt.Y - 13, 26, 26);
        }
        else _benchTrail.Clear();

        var gridRect = new RectangleF(gx, gridY, EN * cw, EN * cellH);
        (int, int) CellAt(PointF p) => (Math.Clamp((int)((p.X - gx) / cw), 0, EN - 1), Math.Clamp(EN - 1 - (int)((p.Y - gridY) / cellH), 0, EN - 1));
        AddHit(gridRect, down: p => { var (c, r) = CellAt(p); _ecuC0 = _ecuC1 = c; _ecuR0 = _ecuR1 = r; },
            drag: p => { var (c, r) = CellAt(p); _ecuC1 = c; _ecuR1 = r; }, up: () => { }, wheel: dir => EcuAdjust(dir, ModifierKeys.HasFlag(Keys.Shift)));

        // คำอธิบายสี (แถบไล่สี)
        float ly = gridY + EN * cellH + 6;
        for (int i = 0; i < 240; i++) g.FillRectangle(Br(Heat(i / 239f)), gx + i * 1.25f, ly + 3, 1.3f, 14);
        string lo = _ecuMode == 0 ? $"{fMin:0.0} ms" : _ecuMode == 1 ? "0°" : "λ0.7 หนา", hi = _ecuMode == 0 ? $"{fMax:0.0} ms" : _ecuMode == 1 ? "45°" : "λ1.3 บาง";
        TxtR(g, lo, _fCap, Palette.Dim, gx - 6, ly);
        Txt(g, hi, _fCap, Palette.Dim, gx + 306, ly);
        string legend = _ecuMode == 0 ? "ฟ้า = ฉีดน้อย (โหลดเบา)  แดง = ฉีดมาก (โหลดหนัก)  จุดขาว = ไดโน"
            : _ecuMode == 1 ? "มุมขวาบน: แดง = แก่เกิน MBT  ขาว = ใกล้  ดำ = ไฟอ่อน"
            : "ฟ้า = หนา  เขียว = แรงสุด  เหลือง = บาง  แดง = บางเกิน";
        Txt(g, legend, _fCap, Palette.Dim, gx + 400, ly);

        // ---------- แถบเครื่องมือ ----------
        float by = 884f, bx = LX;
        void B(string t, float w, Action a, Color? col = null) { Btn(g, new RectangleF(bx, by, w, 40), t, false, col ?? Palette.Accent, a, _fXs); bx += w + 8; }
        B("−−", 54, () => EcuAdjust(-1, true)); B("−", 54, () => EcuAdjust(-1, false)); B("+", 54, () => EcuAdjust(1, false)); B("++", 54, () => EcuAdjust(1, true));
        B("เกลี่ย", 80, EcuSmooth, Palette.Blue); B("เท่ากัน", 86, EcuEqualize, Palette.Blue);
        B("โรงงาน (ที่เลือก)", 150, EcuFactorySel, Palette.Green); B("โรงงาน (ทั้งตาราง)", 156, EcuFactoryAll, Palette.Red);
        if (_ecuMode == 1) B("ใกล้ MBT", 98, EcuNearMbt, Palette.Yellow);

        // ---------- แถบอธิบายย่อ ----------
        var gl = new RectangleF(LX, 936, 1000, 104);
        FillRR(g, gl, 4, Palette.Panel); DrawRR(g, gl, 4, Palette.Border, 1f);
        g.FillRectangle(Br(Palette.Yellow), gl.X + 14, gl.Y + 12, 4, 20);
        Txt(g, "ความรู้ย่อ   (กด ? ดูเต็ม)", _fCap, Palette.Dim, gl.X + 28, gl.Y + 12);
        Txt(g, "MBT = องศาจุดระเบิดที่ได้แรงบิดสูงสุด   แก่เกิน = น็อก/แรงไม่เพิ่ม   อ่อนเกิน = แรงตก ไอเสียร้อน", _fXs, Palette.Yellow, gl.X + 20, gl.Y + 36);
        Txt(g, "EGT = อุณหภูมิก๊าซไอเสีย   ปกติ ~700–900°C   เกิน 1,000°C เสี่ยงวาล์ว/ลูกสูบพัง (บางเกิน/ไฟอ่อนเกินทำให้สูง)", _fXs, Palette.Yellow, gl.X + 20, gl.Y + 58);
        Txt(g, "AFR = อากาศ:น้ำมัน   14.7 = พอดี   ~12.5–13.2 = แรงสุด   บางเกิน = ร้อน/น็อก   หนาเกิน = แรงตก/เปลือง", _fXs, Palette.Yellow, gl.X + 20, gl.Y + 80);

        // ---------- ขวา: เครื่องจูนสด ----------
        const float RX = 1280f, RW = 600f;
        PanelBox(g, new RectangleF(RX, 176, RW, 484), "เครื่องจูนสด (ติดเครื่องจริงบนแท่น)");
        if (!bench)
        {
            g.DrawString("เปิดโหมดนี้เพื่อติดเครื่องที่กำลังออกแบบอยู่บนแท่น (เกียร์ว่าง) แล้วสตาร์ท เร่งเครื่อง ฟังเสียง และดูว่าจุดทำงานวิ่งผ่านช่องไหนของตาราง ค่าที่แก้ในตารางมีผลทันที ออกจากแท็บนี้แล้วเครื่องที่ติดตั้งจริงจะกลับมาตามเดิม",
                _fXs, Br(Palette.Dim), new RectangleF(RX + 24, 224, RW - 48, 170));
            Btn(g, new RectangleF(RX + 24, 410, RW - 48, 64), "เปิดเครื่องจูนสด", false, Palette.Green, () => { _benchOn = true; _benchDirty = true; _benchTrail.Clear(); }, _fMd);
            Txt(g, "ปุ่มลัด:  S สตาร์ท (ค้าง)   W เร่งเต็ม   Z/X ค่อย ๆ บิด   K เปิด/ปิดจุดระเบิด", _fCap, Palette.Dim, RX + 24, 500);
        }
        else
        {
            var ec = _eng.Ecu;
            string st = !_eng.Ignition ? "ปิดจุดระเบิด" : _eng.Running ? "ติดเครื่อง" : "ดับ";
            Color stCol = _eng.Running ? Palette.Green : Palette.Dim;
            TxtR(g, st, _fSm, stCol, RX + RW - 24, 222);
            Txt(g, $"{_eng.Rpm:N0}", _fNumL, Palette.Accent, RX + 24, 224);
            Txt(g, "rpm", _fSm, Palette.Dim, RX + 262, 276);
            Txt(g, $"โหลด {_eng.LoadNow * 100f:0}%   •   แรงบิด x{ec.Mult:0.000}", _fXs, Palette.Dim, RX + 24, 304);

            // คันเร่ง
            Txt(g, $"คันเร่ง {_eng.Throttle * 100f:0}%", _fXs, Palette.Dim, RX + 24, 328);
            var tb = new RectangleF(RX + 24, 356, RW - 48, 26);
            FillRR(g, tb, 3, Color.FromArgb(14, 17, 21));
            g.FillRectangle(Br(Palette.Accent), tb.X, tb.Y, tb.Width * Math.Clamp(_eng.Throttle, 0f, 1f), tb.Height);
            DrawRR(g, tb, 3, Palette.Border, 1f);
            void Thr(PointF p) { _mouseThrottle = true; _mouseThrottleValue = Math.Clamp((p.X - tb.X) / tb.Width, 0f, 1f); }
            AddHit(new RectangleF(tb.X - 10, tb.Y - 10, tb.Width + 20, tb.Height + 20), Thr, Thr, () => _mouseThrottle = false);

            // ปุ่มควบคุม
            float cx0 = RX + 24, cwd = (RW - 48 - 16) / 3f;
            Btn(g, new RectangleF(cx0, 396, cwd, 40), "S สตาร์ท (ค้าง)", _mouseStarter || Down(Keys.S), Palette.Green, null, _fXs);
            AddHit(new RectangleF(cx0, 396, cwd, 40), down: _ => { _eng.Ignition = true; _mouseStarter = true; }, up: () => _mouseStarter = false);
            Btn(g, new RectangleF(cx0 + cwd + 8, 396, cwd, 40), "W เร่งเต็ม (ค้าง)", Down(Keys.W), Palette.Accent, null, _fXs);
            AddHit(new RectangleF(cx0 + cwd + 8, 396, cwd, 40), down: _ => { _mouseThrottle = true; _mouseThrottleValue = 1f; }, up: () => _mouseThrottle = false);
            Btn(g, new RectangleF(cx0 + 2 * (cwd + 8), 396, cwd, 40), _eng.Ignition ? "K ปิดจุดระเบิด" : "K เปิดจุดระเบิด", !_eng.Ignition, Palette.Red, () => _eng.Ignition = !_eng.Ignition, _fXs);

            // แถบค่าสด
            float by0 = 448f, bw = RW - 48;
            float lam = ec.Lambda;
            Color afrCol = lam > 1.12f ? Palette.Red : lam < 0.7f ? Palette.Blue : lam > 0.97f ? Palette.Yellow : Palette.Green;
            DrawLiveBar(g, cx0, by0, bw, "AFR (อากาศ:น้ำมัน)", $"{ec.Afr:0.0}  (λ {lam:0.00})", (lam - 0.6f) / 0.9f, afrCol, (0.89f - 0.6f) / 0.9f);
            float dlt = ec.Adv - ec.Mbt;
            Color advCol = dlt > 1.5f ? Palette.Red : dlt < -8f ? Palette.Blue : Palette.Green;
            DrawLiveBar(g, cx0, by0 + 44, bw, "จุดระเบิด  (ขีดขาว = MBT)", $"{ec.Adv:0.0}°  ({(dlt >= 0 ? "แก่" : "อ่อน")} {Math.Abs(dlt):0.0}° จาก MBT)", ec.Adv / 50f, advCol, ec.Mbt / 50f);
            DrawLiveBar(g, cx0, by0 + 88, bw, "หัวฉีด (Duty)", $"{ec.Duty * 100f:0}%{(ec.Duty > 1f ? "  เต็ม!" : "")}", ec.Duty, ec.Duty > 1f ? Palette.Red : ec.Duty > 0.85f ? Palette.Yellow : Palette.Green);
            DrawLiveBar(g, cx0, by0 + 132, bw, "EGT (ก๊าซไอเสีย)", $"{ec.Egt:0} °C", (ec.Egt - 400f) / 800f, ec.Egt > 1050f ? Palette.Red : ec.Egt > 950f ? Palette.Yellow : Palette.Green, (900f - 400f) / 800f);
            DrawLiveBar(g, cx0, by0 + 176, bw, "น็อก", $"{_eng.KnockNow * 100f:0}%", _eng.KnockNow * 5f, _eng.KnockNow > 0.05f ? Palette.Red : Palette.Green);
            Btn(g, new RectangleF(RX + RW - 24 - 150, 176 + 8, 150, 30), "ปิดจูนสด", false, Palette.Red, () => { _benchOn = false; _mouseThrottle = false; _mouseStarter = false; }, _fCap);
        }

        // ---------- ขวา: ช่องที่เลือก ----------
        int cc = _ecuC1, rr = _ecuR1;
        float prpm = m.EcuRpmAt(cc), pload = EngineModel.EcuLoadAt(rr);
        var s = m.EcuAt(prpm, pload);
        PanelBox(g, new RectangleF(RX, 672, RW, 134), $"ช่องที่เลือก  {prpm:N0} rpm  โหลด {pload * 100f:0}%");
        Color afrC2 = s.Lambda > 1.12f ? Palette.Red : s.Lambda < 0.7f ? Palette.Blue : s.Lambda > 0.97f ? Palette.Yellow : Palette.Green;
        Txt(g, $"AFR {s.Afr:0.0} (λ {s.Lambda:0.00})  •  ฉีด {s.PulseMs:0.00} ms  •  หัวฉีด {s.Duty * 100f:0}%", _fXs, afrC2, RX + 24, 706);
        float dl2 = s.Adv - s.Mbt;
        Txt(g, $"จุด {s.Adv:0.0}°  MBT {s.Mbt:0.0}°  ({(dl2 >= 0 ? "แก่" : "อ่อน")} {Math.Abs(dl2):0.0}°)  •  แรงบิด x{s.Mult:0.000}", _fXs, dl2 > 1.5f ? Palette.Red : dl2 < -8f ? Palette.Blue : Palette.Green, RX + 24, 730);
        string warn = s.Egt > 1050f ? "ไอเสียร้อนจัด! เสี่ยงลูกสูบพัง" : m.KnockWot(prpm) > 0.02f && pload > 0.7f ? "น็อกที่ช่องนี้: ลดองศา/เติมน้ำมัน" : dl2 < -10f ? "ไฟอ่อนมาก: แรงตก ไอเสียร้อน" : s.Lambda > 1.35f || s.Lambda < 0.6f ? "ส่วนผสมนอกช่วงจุดติด" : "";
        Txt(g, $"EGT ~{s.Egt:0}°C" + (warn.Length > 0 ? "  •  " + warn : ""), _fXs, warn.Length > 0 ? Palette.Yellow : Palette.Dim, RX + 24, 754);
        Txt(g, "ปุ่ม: ลูกศร เลือก  •  PgUp/PgDn หรือ [ ] ปรับ  •  D สลับโหมด", _fCap, Palette.Dim, RX + 24, 780);

        // ---------- ขวา: กราฟแรงบิด/แรงม้า (เห็นผลของตารางทันที) ----------
        var rpms = new List<float>(); var tq = new List<float>(); var hp = new List<float>();
        for (float r = 2000f; r <= _draft.Limiter; r += 100f)
        {
            float t = m.BrakeTorqueWot(r);
            rpms.Add(r); tq.Add(MathF.Max(0f, t)); hp.Add(MathF.Max(0f, EngineModel.Hp(t, r)));
        }
        DrawChart(g, new RectangleF(RX, 816, RW, 196), rpms.ToArray(), tq.ToArray(), hp.ToArray(), _draft.Limiter);
        TxtR(g, $"{m.PeakHp:0.0} HP @ {m.PeakPsRpm:N0}   {m.PeakTq:0.0} N·m", _fXs, Palette.Accent, RX + RW - 18, 1016);

        if (_showEcuHelp) DrawEcuHelp(g);
    }

    private void DrawEcuHelp(Graphics g)
    {
        g.FillRectangle(Br(Color.FromArgb(225, 6, 8, 11)), 0, 0, VW, VH);
        var box = new RectangleF(200, 40, 1520, 1000);
        FillRR(g, box, 6, Palette.Panel); DrawRR(g, box, 6, Palette.Accent, 2f);
        Txt(g, "อธิบายศัพท์จูนกล่อง", _fTitle, Palette.Accent, box.X + 36, box.Y + 18);
        TxtR(g, "คลิกที่ไหนก็ได้ / Esc เพื่อปิด", _fXs, Palette.Dim, box.Right - 36, box.Y + 34);
        float colW = (box.Width - 108) / 2f;
        for (int i = 0; i < EcuHelp.Length; i++)
        {
            int cx = i % 2, cy = i / 2;
            float x = box.X + 36 + cx * (colW + 36), y = box.Y + 100 + cy * 296;
            g.FillRectangle(Br(Palette.Yellow), x, y + 4, 4, 26);
            Txt(g, EcuHelp[i].Title, _fMd, Palette.Text, x + 16, y);
            g.DrawString(EcuHelp[i].Body, _fSm, Br(Palette.Dim), new RectangleF(x + 16, y + 44, colW - 16, 244));
        }
        AddHit(new RectangleF(0, 0, VW, VH), _ => _showEcuHelp = false);
    }
}
