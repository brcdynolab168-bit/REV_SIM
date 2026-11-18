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
        ("MAP (แรงดันท่อไอดี)",
         "MAP = Manifold Absolute Pressure แรงดันสัมบูรณ์ในท่อไอดี หน่วย kPa  บรรยากาศ ~101 kPa  ยิ่งต่ำยิ่งสุญญากาศลึก\n" +
         "ลิ้นเร่งแคบ + รอบสูง = ดูดแรง MAP ต่ำ (~30–40 kPa) อากาศเข้าน้อย โหลดเบา   ลิ้นเปิดเต็ม MAP ใกล้บรรยากาศ\n" +
         "ECU ใช้ MAP บอกว่าเครื่องกินอากาศเท่าไร แล้วเลือกเวลาฉีด/องศาจุดตามโหลดนั้น\n" +
         "ตารางนี้อ่านอย่างเดียว: แกนนอนเป็นตำแหน่งคันเร่ง แกนตั้งเป็นรอบ ใช้ดูว่าที่คันเร่งและรอบนั้น ๆ เครื่องมีโหลดเท่าไร"),
        ("องศาจบฉีด (EOI)",
         "EOI = End Of Injection องศาที่หัวฉีดหยุดฉีด วัดเป็น °ก่อนศูนย์ตายบนของจังหวะดูด (ยิ่งมากยิ่งฉีดเสร็จเร็ว)\n" +
         "ฉีดให้เสร็จก่อนวาล์วไอดีเปิด น้ำมันมีเวลาระเหยบนวาล์วปิดที่ร้อน ผสมกับอากาศดี\n" +
         "ฉีดตอนวาล์วเปิดแล้ว (EOI น้อยกว่าองศาวาล์วเปิด) ละอองน้ำมันระเหยไม่ทัน กำลังตกเล็กน้อย โดยเฉพาะรอบต่ำ\n" +
         "รอบสูงเวลาฉีดยาว ต้องเลื่อน EOI ให้ช้าลงเพื่อฉีดทันในรอบเดียว ถ้าเลยจะฉีดข้ามไปรอบก่อน กำลังตก\n" +
         "ฉีดเร็วเกินไป (EOI มาก) น้ำมันเกาะผนังท่อไอดี ตอบสนองคันเร่งช้า"),
        ("ไทม์คอยล์ (Dwell)",
         "Dwell = เวลาที่ ECU ปล่อยกระแสชาร์จคอยล์จุดระเบิดก่อนตัดให้เกิดประกายไฟ ยิ่งนานยิ่งเก็บพลังงานมากขึ้นจนอิ่มตัว\n" +
         "สั้นเกินไป = ประกายอ่อน จุดไม่ติด โดยเฉพาะโหลดหนัก กำลังอัดสูง ส่วนผสมบาง (ต้องใช้ประกายแรงกว่า)\n" +
         "นานเกินไป = คอยล์ร้อน เปลืองกระแส ประสิทธิภาพตก เสี่ยงคอยล์ไหม้ (เครื่องสูบเดียวมีเวลาเหลือเฟือ ปกติ ~2–6 ms)\n" +
         "ค่าโรงงานคำนวณให้พลังงานประกายเกินที่ต้องการราว 8%"),
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
        var bar = new RectangleF(x, y + 25, w, 8);
        Bar(g, bar, frac, col);
        if (marker >= 0f) g.FillRectangle(Br(Palette.Text), bar.X + Math.Clamp(marker, 0f, 1f) * bar.Width - 1.5f, bar.Y - 3, 3, bar.Height + 6);
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
        const float LX = 252f, labW = 56f, cw = 46f, cellH = 30f, gridY = 256f, hdrY = 232f;
        const float RX = 1264f, RW = 616f;
        float gx = LX + labW;

        // ---------- แถบโหมด ----------
        Color[] modeCol = { Palette.Blue, Palette.Accent, Palette.Red, Palette.Yellow, Palette.Green, Color.FromArgb(150, 120, 255) };
        for (int i = 0; i < ModeCount; i++)
        {
            int mi = i;
            Btn(g, new RectangleF(LX + i * 142, 176, 134, 44), EcuModeNames[i], _ecuMode == i, modeCol[i], () => _ecuMode = mi, _fXs);
        }
        Btn(g, new RectangleF(gx + EN * cw - 90, 176, 90, 44), "? ศัพท์", false, Palette.Yellow, () => _showEcuHelp = true, _fXs);

        // ---------- ตาราง ----------
        EcuRange(out int sc0, out int sc1, out int sr0, out int sr1);
        float fMin = float.MaxValue, fMax = float.MinValue;      // ช่วงค่าของตาราง ใช้ไล่สี
        Func<int, int, float>? cellVal = _ecuMode == 0 ? m.CellMs : _ecuMode == 1 ? m.CellEoi : _ecuMode == 3 ? m.CellDwell : _ecuMode == 5 ? (c, r) => m.MapAtTps(m.EcuRpmAt(c), EngineModel.TpsAxis(r)) : null;
        if (cellVal != null) for (int rr0 = 0; rr0 < EN; rr0++) for (int cc0 = 0; cc0 < EN; cc0++) { float vv = cellVal(cc0, rr0); fMin = MathF.Min(fMin, vv); fMax = MathF.Max(fMax, vv); }
        Txt(g, _ecuMode == 5 ? "TPS %" : "โหลด→", _fCap, Palette.Dim, LX + 4, hdrY + 2);
        for (int r = 0; r < EN; r++)
            TxtC(g, _ecuMode == 5 ? (EngineModel.TpsAxis(r) * 100f).ToString(EngineModel.TpsAxis(r) < 0.1f ? "0.0" : "0") : $"{(r + 1) * 5}%", _fCap, r >= sr0 && r <= sr1 ? Palette.Accent : Palette.Dim, gx + (r + 0.5f) * cw, hdrY + 10);
        for (int c = 0; c < EN; c++)
        {
            float y = gridY + c * cellH;
            TxtC(g, (m.EcuRpmAt(c) / 1000f).ToString("0.0"), _fCap, c >= sc0 && c <= sc1 ? Palette.Accent : Palette.Dim, LX + labW / 2 - 4, y + cellH / 2);
            for (int r = 0; r < EN; r++)
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
                    float v = m.CellEoi(c, r);
                    col = Heat((v - fMin) / MathF.Max(1f, fMax - fMin)); txt = v.ToString("0");
                }
                else if (_ecuMode == 2)
                {
                    float v = m.CellAdv(c, r);
                    dMbt = v - e.Mbt;
                    col = Heat(v / 45f); txt = v.ToString("0.0");
                }
                else if (_ecuMode == 3)
                {
                    float v = m.CellDwell(c, r);
                    col = Heat((v - fMin) / MathF.Max(0.05f, fMax - fMin)); txt = v.ToString("0.00");
                }
                else if (_ecuMode == 5)
                {
                    float v = m.MapAtTps(m.EcuRpmAt(c), EngineModel.TpsAxis(r));
                    col = Heat((v - fMin) / MathF.Max(1f, fMax - fMin)); txt = v.ToString("0");
                }
                else
                {
                    col = Heat((e.Lambda - 0.7f) / 0.6f); txt = e.Afr.ToString("0.0");
                }
                var cell = new RectangleF(gx + r * cw + 1.5f, y + 1.5f, cw - 3f, cellH - 3f);     // เว้นร่องระหว่างช่อง ให้ตาโล่ง
                g.FillRectangle(Br(col), cell);
                TxtC(g, txt, _fCap, Lum(col) > 135f ? Color.FromArgb(18, 20, 24) : Color.White, cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
                Color? mk = null;
                if (_ecuMode == 2) mk = dMbt > 1f ? Color.FromArgb(255, 30, 30) : dMbt > -2.5f ? Color.FromArgb(255, 255, 255) : Color.FromArgb(20, 20, 30);   // เทียบ MBT: แดง = แก่เกิน  ขาว = ใกล้  ดำ = ไฟอ่อน
                else if (_ecuMode == 1 && m.OpenValveFrac(c, r) > 0.1f) mk = Color.FromArgb(20, 20, 30);                                                  // ดำ = ฉีดตอนวาล์วเปิดแล้ว
                else if (_ecuMode == 3)
                {
                    float sq = e.Spark, cd = m.CoilDuty(c, r);
                    if (sq < 0.95f) mk = Color.FromArgb(255, 30, 30);               // แดง = ประกายอ่อน
                    else if (cd > 0.55f) mk = Color.FromArgb(20, 20, 30);           // ดำ = คอยล์ร้อน (ชาร์จนาน)
                }
                if (mk != null)
                    g.FillPolygon(Br(mk.Value), new[] { new PointF(cell.Right - 8, cell.Y), new PointF(cell.Right, cell.Y), new PointF(cell.Right, cell.Y + 8) });
            }
        }
        for (int c = 0; c < EN; c++)          // เส้นไดโน (คันเร่งสุด) = จุดขาว ที่ช่องโหลดของแต่ละรอบ
        {
            int r = _ecuMode == 5 ? EN - 1 : Math.Clamp((int)MathF.Round(m.Load(1f, m.EcuRpmAt(c)) * EN - 1f), 0, EN - 1);
            g.FillEllipse(Br(Palette.Text), gx + (r + 0.5f) * cw - 3, gridY + c * cellH + cellH - 9, 6, 6);
        }
        g.DrawRectangle(Pn(Palette.Accent, 3f), gx + sr0 * cw, gridY + sc0 * cellH, (sr1 - sr0 + 1) * cw, (sc1 - sc0 + 1) * cellH);

        // จุดทำงานสด (ติดเครื่องอยู่): วงกลม + ร่องรอย
        if (bench && _eng.Running)
        {
            float colF = Math.Clamp((_eng.Rpm - 1000f) / (m.EcuRpmMax - 1000f) * (EN - 1), 0f, EN - 1f);
            float rowF = _ecuMode == 5 ? EngineModel.TpsToIndex(_eng.Throttle) : Math.Clamp(_eng.LoadNow * EN - 1f, 0f, EN - 1f);
            var pt = new PointF(gx + (rowF + 0.5f) * cw, gridY + (colF + 0.5f) * cellH);
            _benchTrail.Add(pt);
            if (_benchTrail.Count > 50) _benchTrail.RemoveAt(0);
            for (int i = 0; i < _benchTrail.Count; i++)
                g.FillEllipse(Br(Color.FromArgb(Math.Min(20 + i * 4, 200), 255, 255, 255)), _benchTrail[i].X - 3, _benchTrail[i].Y - 3, 6, 6);
            g.DrawEllipse(Pn(Palette.Text, 3f), pt.X - 10, pt.Y - 10, 20, 20);
            g.DrawEllipse(Pn(Color.Black, 1f), pt.X - 13, pt.Y - 13, 26, 26);
        }
        else _benchTrail.Clear();

        var gridRect = new RectangleF(gx, gridY, EN * cw, EN * cellH);
        (int, int) CellAt(PointF p) => (Math.Clamp((int)((p.Y - gridY) / cellH), 0, EN - 1), Math.Clamp((int)((p.X - gx) / cw), 0, EN - 1));    // (รอบ, โหลด)
        AddHit(gridRect, down: p => { var (c, r) = CellAt(p); _ecuC0 = _ecuC1 = c; _ecuR0 = _ecuR1 = r; },
            drag: p => { var (c, r) = CellAt(p); _ecuC1 = c; _ecuR1 = r; }, up: () => { }, wheel: dir => EcuAdjust(dir, ModifierKeys.HasFlag(Keys.Shift)));

        // คำอธิบายสี (แถบไล่สี)
        float ly = gridY + EN * cellH + 10;
        for (int i = 0; i < 240; i++) g.FillRectangle(Br(Heat(i / 239f)), gx + i * 1.0f, ly + 3, 1.3f, 14);
        string lo = _ecuMode == 0 ? $"{fMin:0.0} ms" : _ecuMode == 1 ? $"{fMin:0}°" : _ecuMode == 2 ? "0°" : _ecuMode == 3 ? $"{fMin:0.0} ms" : _ecuMode == 5 ? $"{fMin:0} kPa" : "λ0.7 หนา";
        string hi = _ecuMode == 0 ? $"{fMax:0.0} ms" : _ecuMode == 1 ? $"{fMax:0}°" : _ecuMode == 2 ? "45°" : _ecuMode == 3 ? $"{fMax:0.0} ms" : _ecuMode == 5 ? $"{fMax:0} kPa" : "λ1.3 บาง";
        TxtR(g, lo, _fCap, Palette.Dim, gx - 8, ly);
        Txt(g, hi, _fCap, Palette.Dim, gx + 250, ly);
        string legend = _ecuMode == 0 ? "ฟ้า = ฉีดน้อย (โหลดเบา)  •  แดง = ฉีดมาก  •  จุดขาว = เส้นไดโน"
            : _ecuMode == 1 ? "ฟ้า = จบฉีดช้า  •  แดง = จบฉีดเร็ว  •  มุมดำ = ฉีดตอนวาล์วเปิด"
            : _ecuMode == 2 ? "มุมขวาบน: แดง = แก่เกิน MBT  •  ขาว = ใกล้  •  ดำ = ไฟอ่อน"
            : _ecuMode == 3 ? "มุมแดง = ประกายอ่อน (ชาร์จไม่พอ)  •  มุมดำ = คอยล์ร้อน"
            : _ecuMode == 5 ? "แกนคันเร่งแบบลอการิทึม  •  ฟ้า = สุญญากาศลึก  •  แดง = ใกล้บรรยากาศ"
            : "ฟ้า = หนา  •  เขียว = แรงสุด  •  เหลือง = บาง  •  แดง = บางเกิน";
        TxtR(g, legend, _fCap, Palette.Dim, gx + EN * cw, ly);

        // ---------- แถบเครื่องมือ ----------
        float by = 902f, bx = LX;
        void B(string t, float w, Action a, Color? col = null) { Btn(g, new RectangleF(bx, by, w, 44), t, false, col ?? Palette.Accent, a, _fXs); bx += w + 10; }
        B("−−", 58, () => EcuAdjust(-1, true)); B("−", 58, () => EcuAdjust(-1, false)); B("+", 58, () => EcuAdjust(1, false)); B("++", 58, () => EcuAdjust(1, true));
        B("เกลี่ย", 84, EcuSmooth, Palette.Blue); B("เท่ากัน", 90, EcuEqualize, Palette.Blue);
        B("โรงงาน (ที่เลือก)", 158, EcuFactorySel, Palette.Green); B("โรงงาน (ทั้งตาราง)", 164, EcuFactoryAll, Palette.Red);
        if (_ecuMode == 2) B("ใกล้ MBT", 104, EcuNearMbt, Palette.Yellow);

        // ---------- ซ้ายล่าง: ช่องที่เลือก ----------
        int cc = _ecuC1, rr = _ecuR1;
        float prpm = m.EcuRpmAt(cc), pload = EngineModel.EcuLoadAt(rr);
        var s = m.EcuAt(prpm, pload);
        float dl2 = s.Adv - s.Mbt;
        FillRR(g, new RectangleF(LX, 962, 976, 78), 4, Palette.Panel); DrawRR(g, new RectangleF(LX, 962, 976, 78), 4, Palette.Border, 1f);
        g.FillRectangle(Br(Palette.Accent), LX + 14, 974, 4, 18);
        Txt(g, $"ช่องที่เลือก  {prpm:N0} rpm  •  โหลด {pload * 100f:0}%", _fCap, Palette.Dim, LX + 28, 972);
        Color afrC2 = s.Lambda > 1.12f ? Palette.Red : s.Lambda < 0.7f ? Palette.Blue : s.Lambda > 0.97f ? Palette.Yellow : Palette.Green;
        string warn = s.Egt > 1050f ? "ไอเสียร้อนจัด! เสี่ยงลูกสูบพัง" : m.KnockWot(prpm) > 0.02f && pload > 0.7f ? "น็อกที่ช่องนี้" : dl2 < -10f ? "ไฟอ่อนมาก แรงตก" : s.Lambda > 1.35f || s.Lambda < 0.6f ? "ส่วนผสมนอกช่วงจุดติด" : "";
        Txt(g, $"AFR {s.Afr:0.0} (λ {s.Lambda:0.00})   ฉีด {s.PulseMs:0.00} ms ({s.Duty * 100f:0}%)   จบฉีด {s.Eoi:0}°   MAP {m.MapKpa(pload):0} kPa", _fXs, afrC2, LX + 340, 970);
        Txt(g, $"จุด {s.Adv:0.0}° / MBT {s.Mbt:0.0}°   คอยล์ {s.DwellMs:0.00} ms (ประกาย {s.Spark * 100f:0}%)   แรงบิด x{s.Mult:0.000}   EGT {s.Egt:0}°C" + (warn.Length > 0 ? "   ⚠ " + warn : ""),
            _fXs, warn.Length > 0 ? Palette.Yellow : dl2 > 1.5f ? Palette.Red : s.Spark < 0.95f ? Palette.Red : Palette.Dim, LX + 28, 1004);

        // ---------- ขวา: เครื่องจูนสด ----------
        float ix = RX + 28f, iw = RW - 56f;
        PanelBox(g, new RectangleF(RX, 176, RW, 564), "เครื่องจูนสด  (ติดเครื่องจริงบนแท่น)");
        if (!bench)
        {
            g.DrawString("เปิดโหมดนี้เพื่อติดเครื่องที่กำลังออกแบบอยู่บนแท่น แล้วสตาร์ท เร่งเครื่อง เข้าเกียร์ ลองขับ ฟังเสียง และดูว่าจุดทำงานวิ่งผ่านช่องไหนของตาราง\n\nค่าที่แก้ในตารางมีผลทันที ออกจากแท็บนี้แล้วเครื่องที่ติดตั้งจริงจะกลับมาตามเดิม และสถิติจะไม่ถูกบันทึกขณะจูน",
                _fSm, Br(Palette.Dim), new RectangleF(ix, 236, iw, 230));
            Btn(g, new RectangleF(ix, 500, iw, 68), "เปิดเครื่องจูนสด", false, Palette.Green, () => { _benchOn = true; _benchDirty = true; _benchTrail.Clear(); }, _fMd);
            Txt(g, "ปุ่มลัด:   S สตาร์ท (ค้าง)   W เร่งเต็ม   K จุดระเบิด   E/Q เปลี่ยนเกียร์", _fCap, Palette.Dim, ix, 596);
        }
        else
        {
            var ec = _eng.Ecu;
            string st = !_eng.Ignition ? "ปิดจุดระเบิด" : _eng.Running ? "ติดเครื่อง" : "ดับ";
            TxtR(g, st, _fSm, _eng.Running ? Palette.Green : Palette.Dim, RX + RW - 28, 226);
            Btn(g, new RectangleF(RX + RW - 28 - 128, 180, 128, 32), "ปิดจูนสด", false, Palette.Red, () => { _benchOn = false; _mouseThrottle = false; _mouseStarter = false; }, _fCap);

            // รอบ + ความเร็ว
            Txt(g, $"{_eng.Rpm:N0}", _fNumL, Palette.Accent, ix, 240);
            Txt(g, "รอบเครื่อง rpm", _fXs, Palette.Dim, ix + 2, 322);
            Txt(g, $"{_eng.SpeedKmh:0}", _fNumL, Palette.Text, ix + 330, 240);
            string gearTxt = _eng.IsAuto ? $"ออโต้  (สายพาน x{_eng.CvtRatio:0.00})" : _eng.Gear == 0 ? "เกียร์ว่าง (N)" : $"เกียร์ {_eng.Gear}";
            Txt(g, "กม./ชม.   " + gearTxt, _fXs, Palette.Dim, ix + 332, 322);

            // คันเร่ง
            Txt(g, $"คันเร่ง {_eng.Throttle * 100f:0}%   MAP {m.MapKpa(_eng.LoadNow):0} kPa   โหลด {_eng.LoadNow * 100f:0}%   แรงบิด x{ec.Mult:0.000}", _fXs, Palette.Dim, ix, 356);
            var tb = new RectangleF(ix, 388, iw, 26);
            FillRR(g, tb, 3, Color.FromArgb(14, 17, 21));
            g.FillRectangle(Br(Palette.Accent), tb.X, tb.Y, tb.Width * Math.Clamp(_eng.Throttle, 0f, 1f), tb.Height);
            DrawRR(g, tb, 3, Palette.Border, 1f);
            void Thr(PointF p) { _mouseThrottle = true; _mouseThrottleValue = Math.Clamp((p.X - tb.X) / tb.Width, 0f, 1f); }
            AddHit(new RectangleF(tb.X - 10, tb.Y - 12, tb.Width + 20, tb.Height + 24), Thr, Thr, () => _mouseThrottle = false);

            // ปุ่มควบคุม
            float cwd = (iw - 20) / 3f;
            Btn(g, new RectangleF(ix, 432, cwd, 44), "S สตาร์ท (ค้าง)", _mouseStarter || Down(Keys.S), Palette.Green, null, _fXs);
            AddHit(new RectangleF(ix, 432, cwd, 44), down: _ => { _eng.Ignition = true; _mouseStarter = true; }, up: () => _mouseStarter = false);
            Btn(g, new RectangleF(ix + cwd + 10, 432, cwd, 44), "W เร่งเต็ม (ค้าง)", Down(Keys.W), Palette.Accent, null, _fXs);
            AddHit(new RectangleF(ix + cwd + 10, 432, cwd, 44), down: _ => { _mouseThrottle = true; _mouseThrottleValue = 1f; }, up: () => _mouseThrottle = false);
            Btn(g, new RectangleF(ix + 2 * (cwd + 10), 432, cwd, 44), _eng.Ignition ? "K ปิดจุดระเบิด" : "K เปิดจุดระเบิด", !_eng.Ignition, Palette.Red, () => _eng.Ignition = !_eng.Ignition, _fXs);

            // เกียร์ (รถเกียร์ธรรมดา)
            if (!_eng.IsAuto)
            {
                int ng = m.D.Gears;
                float gsp = (iw + 8f) / (ng + 1);
                for (int i = 0; i <= ng; i++)
                {
                    int gi = i;
                    Btn(g, new RectangleF(ix + i * gsp, 490, gsp - 8, 40), i == 0 ? "N" : i.ToString(), _gearSel == i, Palette.Accent, () => SelectGear(gi), _fSm);
                }
            }
            else Txt(g, "รถออโต้ CVT: บิดคันเร่งแล้วออกตัวได้เลย", _fXs, Palette.Dim, ix, 500);

            // แถบค่าสด
            float by0 = 550f, bw = iw, rowH = 38f;
            float lam = ec.Lambda;
            Color afrCol = lam > 1.12f ? Palette.Red : lam < 0.7f ? Palette.Blue : lam > 0.97f ? Palette.Yellow : Palette.Green;
            DrawLiveBar(g, ix, by0, bw, "AFR (อากาศ:น้ำมัน)", $"{ec.Afr:0.0}  (λ {lam:0.00})", (lam - 0.6f) / 0.9f, afrCol, (0.89f - 0.6f) / 0.9f);
            float dlt = ec.Adv - ec.Mbt;
            Color advCol = dlt > 1.5f ? Palette.Red : dlt < -8f ? Palette.Blue : Palette.Green;
            DrawLiveBar(g, ix, by0 + rowH, bw, "จุดระเบิด (ขีดขาว = MBT)", $"{ec.Adv:0.0}°  ({(dlt >= 0 ? "แก่" : "อ่อน")} {Math.Abs(dlt):0.0}°)", ec.Adv / 50f, advCol, ec.Mbt / 50f);
            DrawLiveBar(g, ix, by0 + 2 * rowH, bw, "หัวฉีด (Duty)", $"{ec.Duty * 100f:0}%{(ec.Duty > 1f ? "  เต็ม!" : "")}", ec.Duty, ec.Duty > 1f ? Palette.Red : ec.Duty > 0.85f ? Palette.Yellow : Palette.Green);
            DrawLiveBar(g, ix, by0 + 3 * rowH, bw, "EGT (ก๊าซไอเสีย)", $"{ec.Egt:0} °C", (ec.Egt - 400f) / 800f, ec.Egt > 1050f ? Palette.Red : ec.Egt > 950f ? Palette.Yellow : Palette.Green, (900f - 400f) / 800f);
            DrawLiveBar(g, ix, by0 + 4 * rowH, bw, "น็อก", $"{_eng.KnockNow * 100f:0}%", _eng.KnockNow * 5f, _eng.KnockNow > 0.05f ? Palette.Red : Palette.Green);
        }

        // ---------- ขวาล่าง: กราฟ ก่อน/หลัง ----------
        DrawCompareChart(g, new RectangleF(RX, 756, RW, 284), m, _draft.Limiter);

        if (_showEcuHelp) DrawEcuHelp(g);
    }

    private void DrawEcuHelp(Graphics g)
    {
        g.FillRectangle(Br(Color.FromArgb(225, 6, 8, 11)), 0, 0, VW, VH);
        var box = new RectangleF(200, 40, 1520, 1000);
        FillRR(g, box, 6, Palette.Panel); DrawRR(g, box, 6, Palette.Accent, 2f);
        Txt(g, "อธิบายศัพท์จูนกล่อง", _fTitle, Palette.Accent, box.X + 36, box.Y + 18);
        TxtR(g, "คลิกที่ไหนก็ได้ / Esc เพื่อปิด", _fXs, Palette.Dim, box.Right - 36, box.Y + 34);
        float colW = (box.Width - 144) / 3f;
        for (int i = 0; i < EcuHelp.Length; i++)
        {
            int cx = i % 3, cy = i / 3;
            float x = box.X + 36 + cx * (colW + 36), y = box.Y + 92 + cy * 300;
            g.FillRectangle(Br(Palette.Yellow), x, y + 4, 4, 26);
            Txt(g, EcuHelp[i].Title, _fMd, Palette.Text, x + 16, y);
            g.DrawString(EcuHelp[i].Body, _fXs, Br(Palette.Dim), new RectangleF(x + 16, y + 40, colW - 16, 250));
        }
        AddHit(new RectangleF(0, 0, VW, VH), _ => _showEcuHelp = false);
    }
}
