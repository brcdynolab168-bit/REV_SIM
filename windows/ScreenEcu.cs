namespace RevGame;

/// <summary>แท็บ "จูนกล่อง" ในอู่: ตาราง ECU 20x20 (น้ำมัน % / องศาจุดระเบิด / AFR) คอลัมน์ = รอบ, แถว = โหลด 5..100%</summary>
public sealed partial class MainForm
{
    private int _ecuMode;                              // 0 = น้ำมัน %, 1 = องศาจุด, 2 = AFR (เป้าหมาย)
    private int _ecuC0, _ecuR0 = 19, _ecuC1, _ecuR1 = 19;  // มุมเลือก (จุดเริ่ม, จุดล่าสุด)
    private const int EN = EngineModel.EcuN;
    private static readonly string[] EcuModeNames = { "น้ำมัน (%)", "องศาจุด (°)", "AFR" };

    private bool IsEcuTab => GarageTabs[_tab].Name == "จูนกล่อง";

    private void EcuRange(out int c0, out int c1, out int r0, out int r1)
    {
        c0 = Math.Min(_ecuC0, _ecuC1); c1 = Math.Max(_ecuC0, _ecuC1);
        r0 = Math.Min(_ecuR0, _ecuR1); r1 = Math.Max(_ecuR0, _ecuR1);
    }

    private void EnsureFuel() => _draft.EcuFuel ??= Enumerable.Repeat(100f, EN * EN).ToArray();
    private void EnsureIgn() => _draft.EcuIgn ??= _preview.FactoryIgnTable();

    private static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>ปรับค่าที่เลือกในโหมดปัจจุบัน dir = ±1</summary>
    private void EcuAdjust(int dir, bool coarse)
    {
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        var m = _preview;
        if (_ecuMode == 1)
        {
            EnsureIgn();
            float step = (coarse ? 2f : 0.5f) * dir;
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++)
                _draft.EcuIgn![r * EN + c] = Math.Clamp(_draft.EcuIgn[r * EN + c] + step, -5f, 50f);
        }
        else
        {
            EnsureFuel();
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++)
            {
                int i = r * EN + c;
                if (_ecuMode == 0) _draft.EcuFuel![i] = Math.Clamp(_draft.EcuFuel[i] + (coarse ? 5f : 1f) * dir, 50f, 160f);
                else
                {
                    // โหมด AFR: ปรับ AFR เป้าหมายของ ECU แล้วแปลงกลับเป็น % น้ำมัน
                    float baseAfr = m.Stoich * m.LambdaBase(m.EcuRpmAt(c), EngineModel.EcuLoadAt(r));
                    float afr = baseAfr * 100f / _draft.EcuFuel![i] + (coarse ? 0.5f : 0.1f) * dir;
                    _draft.EcuFuel[i] = Math.Clamp(MathF.Round(baseAfr * 100f / Math.Max(afr, 1f) * 10f) / 10f, 50f, 160f);
                }
            }
        }
        DraftChanged();
    }

    private void EcuSmooth()
    {
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        bool ign = _ecuMode == 1;
        if (ign) EnsureIgn(); else EnsureFuel();
        var t = ign ? _draft.EcuIgn! : _draft.EcuFuel!;
        var src = (float[])t.Clone();
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++)
        {
            float s = 0; int n = 0;
            for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
            {
                int rr = r + dr, cc = c + dc;
                if (rr < 0 || cc < 0 || rr >= EN || cc >= EN) continue;
                s += src[rr * EN + cc]; n++;
            }
            t[r * EN + c] = MathF.Round(s / n * 10f) / 10f;
        }
        DraftChanged();
        Toast("เกลี่ยค่าที่เลือกแล้ว");
    }

    private void EcuEqualize()
    {
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        bool ign = _ecuMode == 1;
        if (ign) EnsureIgn(); else EnsureFuel();
        var t = ign ? _draft.EcuIgn! : _draft.EcuFuel!;
        float v = t[_ecuR0 * EN + _ecuC0];
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) t[r * EN + c] = v;
        DraftChanged();
    }

    private void EcuFactorySel()
    {
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        if (_ecuMode == 1)
        {
            var fac = _preview.FactoryIgnTable();
            EnsureIgn();
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) _draft.EcuIgn![r * EN + c] = fac[r * EN + c];
        }
        else
        {
            EnsureFuel();
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) _draft.EcuFuel![r * EN + c] = 100f;
        }
        DraftChanged();
    }

    private void EcuFactoryAll()
    {
        if (_ecuMode == 1) _draft.EcuIgn = null; else _draft.EcuFuel = null;
        DraftChanged();
        Toast("คืนตารางเป็นค่าโรงงานแล้ว");
    }

    /// <summary>ตั้งองศาจุดของช่องที่เลือกให้ใกล้ MBT (ต่ำกว่า 1°) ตามหลักการจริง: ได้แรงบิดสูงสุดโดยยังไม่ล้ำเกินจนน็อก</summary>
    private void EcuNearMbt()
    {
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        EnsureIgn();
        var m = _preview;
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++)
        {
            var e = m.EcuAt(m.EcuRpmAt(c), EngineModel.EcuLoadAt(r));
            _draft.EcuIgn![r * EN + c] = MathF.Round((e.Mbt - 1f) * 2f) / 2f;
        }
        DraftChanged();
        Toast("ตั้งองศาใกล้ MBT แล้ว (ระวังน็อกถ้ากำลังอัดสูง/ออกเทนต่ำ)");
    }

    private bool EcuKey(Keys k, bool shift)
    {
        switch (k)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
            {
                int dc = k == Keys.Left ? -1 : k == Keys.Right ? 1 : 0, dr = k == Keys.Down ? -1 : k == Keys.Up ? 1 : 0;
                _ecuC1 = Math.Clamp(_ecuC1 + dc, 0, EN - 1); _ecuR1 = Math.Clamp(_ecuR1 + dr, 0, EN - 1);
                if (!shift) { _ecuC0 = _ecuC1; _ecuR0 = _ecuR1; }
                return true;
            }
            case Keys.Add: case Keys.Oemplus: case Keys.PageUp: EcuAdjust(1, shift || k == Keys.PageUp); return true;
            case Keys.Subtract: case Keys.OemMinus: case Keys.PageDown: EcuAdjust(-1, shift || k == Keys.PageDown); return true;
            case Keys.D: _ecuMode = (_ecuMode + 1) % 3; return true;
        }
        return false;
    }

    private void DrawEcu(Graphics g)
    {
        var m = _preview;
        // โหมดตาราง
        for (int i = 0; i < 3; i++)
        {
            int mi = i;
            Btn(g, new RectangleF(GL + i * 208, 176, 200, 40), EcuModeNames[i], _ecuMode == i, i == 1 ? Palette.Red : i == 0 ? Palette.Blue : Palette.Green, () => _ecuMode = mi, _fXs);
        }
        string modeNote = _ecuMode == 0 ? "100 = แมพโรงงาน" : _ecuMode == 1 ? "แก่ = จุดเร็ว  อ่อน = จุดช้า" : "ตั้งเป้า AFR";
        TxtR(g, modeNote, _fCap, Palette.Dim, GL + GW, 188);

        const float labW = 58f, hdrY = 224f, gridY = 248f, cellH = 29f;
        float gx = GL + labW, cw = (GW - labW) / EN;
        EcuRange(out int sc0, out int sc1, out int sr0, out int sr1);

        // หัวคอลัมน์ (รอบ)
        Txt(g, "x1000", _fCap, Palette.Dim, GL + 4, hdrY + 2);
        for (int c = 0; c < EN; c++)
            TxtC(g, (m.EcuRpmAt(c) / 1000f).ToString("0.0"), _fCap, c >= sc0 && c <= sc1 ? Palette.Accent : Palette.Dim, gx + (c + 0.5f) * cw, hdrY + 10);

        for (int dr = 0; dr < EN; dr++)
        {
            int r = EN - 1 - dr;
            float y = gridY + dr * cellH;
            TxtC(g, $"{(r + 1) * 5}%", _fCap, r >= sr0 && r <= sr1 ? Palette.Accent : Palette.Dim, GL + labW / 2 - 4, y + cellH / 2);
            for (int c = 0; c < EN; c++)
            {
                float rpm = m.EcuRpmAt(c), load = EngineModel.EcuLoadAt(r);
                var e = m.EcuAt(rpm, load);
                Color col; string txt;
                if (_ecuMode == 0)
                {
                    float v = m.CellFuel(c, r);
                    col = v >= 100f ? Mix(Palette.Panel, Color.FromArgb(200, 120, 20), (v - 100f) / 25f) : Mix(Palette.Panel, Color.FromArgb(40, 120, 190), (100f - v) / 25f);
                    txt = v.ToString("0");
                }
                else if (_ecuMode == 1)
                {
                    float v = m.CellAdv(c, r), d = v - e.Mbt;
                    col = d >= -1f ? Mix(Color.FromArgb(120, 100, 30), Color.FromArgb(200, 50, 45), (d + 1f) / 6f) : Mix(Palette.Panel, Color.FromArgb(40, 110, 180), (-1f - d) / 12f);
                    if (d >= -1f && d < 1f) col = Color.FromArgb(70, 140, 80);
                    txt = v.ToString("0.0");
                }
                else
                {
                    float l = e.Lambda;
                    col = l < 0.80f ? Mix(Color.FromArgb(50, 100, 170), Color.FromArgb(40, 70, 130), (0.8f - l) / 0.3f)
                        : l < 0.95f ? Color.FromArgb(50, 130, 80)
                        : l < 1.05f ? Color.FromArgb(150, 130, 40)
                        : Mix(Color.FromArgb(190, 90, 40), Color.FromArgb(200, 40, 40), (l - 1.05f) / 0.3f);
                    txt = e.Afr.ToString("0.0");
                }
                var cell = new RectangleF(gx + c * cw, y, cw - 1, cellH - 1);
                g.FillRectangle(Br(col), cell);
                TxtC(g, txt, _fCap, Palette.Text, cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
            }
        }
        // เส้นทางตอนเปิดคันเร่งสุด (ไดโน) = จุดขาว
        for (int c = 0; c < EN; c++)
        {
            float lw = m.Load(1f, m.EcuRpmAt(c));
            int r = Math.Clamp((int)MathF.Round(lw * EN - 1f), 0, EN - 1);
            float y = gridY + (EN - 1 - r) * cellH;
            g.FillEllipse(Br(Palette.Text), gx + (c + 0.5f) * cw - 3, y + cellH - 8, 6, 6);
        }
        // กรอบที่เลือก
        float sx = gx + sc0 * cw, sy = gridY + (EN - 1 - sr1) * cellH;
        g.DrawRectangle(Pn(Palette.Accent, 3f), sx, sy, (sc1 - sc0 + 1) * cw - 1, (sr1 - sr0 + 1) * cellH - 1);

        // คลิก/ลากเลือก
        var grid = new RectangleF(gx, gridY, EN * cw, EN * cellH);
        (int, int) CellAt(PointF p) => (Math.Clamp((int)((p.X - gx) / cw), 0, EN - 1), Math.Clamp(EN - 1 - (int)((p.Y - gridY) / cellH), 0, EN - 1));
        AddHit(grid, down: p => { var (c, r) = CellAt(p); _ecuC0 = _ecuC1 = c; _ecuR0 = _ecuR1 = r; },
            drag: p => { var (c, r) = CellAt(p); _ecuC1 = c; _ecuR1 = r; }, up: () => { }, wheel: dir => EcuAdjust(dir, ModifierKeys.HasFlag(Keys.Shift)));

        // แถบเครื่องมือ
        float by = 848f, bx = GL;
        void B(string t, float w, Action a, Color? col = null) { Btn(g, new RectangleF(bx, by, w, 42), t, false, col ?? Palette.Accent, a, _fXs); bx += w + 8; }
        B("−−", 56, () => EcuAdjust(-1, true)); B("−", 56, () => EcuAdjust(-1, false)); B("+", 56, () => EcuAdjust(1, false)); B("++", 56, () => EcuAdjust(1, true));
        B("เกลี่ย", 84, EcuSmooth, Palette.Blue); B("เท่ากัน", 90, EcuEqualize, Palette.Blue);
        B("โรงงาน (ที่เลือก)", 150, EcuFactorySel, Palette.Green); B("โรงงาน (ทั้งตาราง)", 150, EcuFactoryAll, Palette.Red);
        if (_ecuMode == 1) B("ใกล้ MBT", 100, EcuNearMbt, Palette.Yellow);

        // ข้อมูลช่องที่เลือก
        int cc = _ecuC1, rr = _ecuR1;
        float prpm = m.EcuRpmAt(cc), pload = EngineModel.EcuLoadAt(rr);
        var s = m.EcuAt(prpm, pload);
        var box = new RectangleF(GL, 900, GW, 120);
        FillRR(g, box, 4, Palette.Panel);
        DrawRR(g, box, 4, Palette.Border, 1f);
        g.FillRectangle(Br(Palette.Yellow), box.X + 14, box.Y + 12, 4, 20);
        Txt(g, $"ช่อง: {prpm:N0} rpm  โหลด {pload * 100f:0}%", _fCap, Palette.Dim, box.X + 28, box.Y + 12);
        string fuelNote = s.Duty > 1f ? "  หัวฉีดเต็ม! ส่วนผสมบางลง" : s.Duty > 0.85f ? "  หัวฉีดใกล้เต็ม" : "";
        Color afrCol = s.Lambda > 1.12f ? Palette.Red : s.Lambda < 0.7f ? Palette.Blue : s.Lambda > 0.97f ? Palette.Yellow : Palette.Green;
        Txt(g, $"AFR {s.Afr:0.0} (λ {s.Lambda:0.00})  •  น้ำมัน {s.FuelPct:0}%  •  หัวฉีด {s.Duty * 100f:0}%{fuelNote}", _fXs, afrCol, box.X + 20, box.Y + 40);
        float dlt = s.Adv - s.Mbt;
        Color advCol = dlt > 1.5f ? Palette.Red : dlt < -8f ? Palette.Blue : Palette.Green;
        Txt(g, $"จุดระเบิด {s.Adv:0.0}° (MBT {s.Mbt:0.0}°  {(dlt >= 0 ? "แก่กว่า" : "อ่อนกว่า")} {Math.Abs(dlt):0.0}°)  •  แรงบิด x{s.Mult:0.000}", _fXs, advCol, box.X + 20, box.Y + 66);
        string warn = s.Egt > 1050f ? "ไอเสียร้อนจัด! บางเกิน/จุดอ่อนเกิน เสี่ยงลูกสูบพัง" : m.KnockWot(prpm) > 0.02f && pload > 0.7f ? "น็อกที่ช่องนี้: ลดองศา/เติมน้ำมันเพิ่ม/ใช้ออกเทนสูงขึ้น" : dlt < -10f ? "จุดอ่อนมาก: แรงบิดตก ไอเสียร้อน" : s.Lambda > 1.35f || s.Lambda < 0.6f ? "ส่วนผสมนอกช่วงจุดติด: เครื่องสะดุด/ดับ" : "";
        Txt(g, $"EGT ~{s.Egt:0}°C" + (warn.Length > 0 ? "  •  " + warn : "  •  พื้นสีเขียว = ใกล้ MBT / AFR กำลังสูงสุด  จุดขาว = เส้นไดโน (คันเร่งสุด)"), _fXs, warn.Length > 0 ? Palette.Yellow : Palette.Dim, box.X + 20, box.Y + 92);
    }
}
