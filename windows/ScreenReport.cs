namespace RevGame;

/// <summary>สรุปการแก้ไข: เทียบเครื่องที่กำลังแก้ (หลัง) กับเครื่องที่ติดตั้งอยู่ (ก่อน)  บอกว่าเปลี่ยนอะไร กราฟช่วงรอบไหนดีขึ้น/แย่ลง และผลต่อเวลา/ท็อปสปีด</summary>
public sealed partial class MainForm
{
    private bool _showReport;
    private readonly List<(string Name, string Old, string New)> _repChanges = new();
    private Perf _repP0, _repP1;
    private float _repUp0, _repUp1, _repHold0, _repHold1;
    private EngineModel? _repM0, _repM1;
    private readonly List<(string Text, int State)> _repNotes = new();      // State: 1 ดี  2 เตือน/แย่  0 กลาง

    /// <summary>ช่วงรอบที่กำลัง (HP) ต่างจากก่อนชัดเจน: Good = ดีขึ้น</summary>
    private readonly record struct Zone(float Lo, float Hi, float Peak, bool Good);

    private static List<Zone> ChartZones(float[] rpm, float[] hp, float[]? hp0)
    {
        var res = new List<Zone>();
        if (hp0 == null) return res;
        float mx = 1f;
        for (int i = 0; i < hp.Length; i++) if (!float.IsNaN(hp[i])) mx = MathF.Max(mx, hp[i]);
        float thr = MathF.Max(0.12f, 0.025f * mx);
        int start = -1, cls = 0; float pk = 0f;
        void Close(int end)
        {
            if (start >= 0 && end - start + 1 >= 3) res.Add(new Zone(rpm[start], rpm[end], pk, cls > 0));
            start = -1; cls = 0; pk = 0f;
        }
        for (int i = 0; i < rpm.Length; i++)
        {
            int c = 0; float d = 0f;
            if (!float.IsNaN(hp[i]) && !float.IsNaN(hp0[i])) { d = hp[i] - hp0[i]; c = d > thr ? 1 : d < -thr ? -1 : 0; }
            if (c != cls) { Close(i - 1); if (c != 0) { start = i; cls = c; pk = d; } }
            else if (c != 0 && MathF.Abs(d) > MathF.Abs(pk)) pk = d;
        }
        Close(rpm.Length - 1);
        res.Sort((a, b) => MathF.Abs(b.Peak).CompareTo(MathF.Abs(a.Peak)));
        return res;
    }

    private void OpenReport()
    {
        var m0 = BaseModel(); var m1 = _preview;
        _repM0 = m0; _repM1 = m1;
        _repChanges.Clear(); _repNotes.Clear();
        foreach (var p in EngineDesign.Params)
        {
            if (!p.Persist || p.Key == "seal" || p.Key == "gtop") continue;
            float a = p.Get(m0.D), b = p.Get(m1.D);
            if (MathF.Abs(a - b) > MathF.Max(p.Step * 0.01f, 1e-5f)) _repChanges.Add((p.Name, p.Display(m0.D), p.Display(m1.D)));
        }
        void Tab(string name, Func<int, int, float> f0, Func<int, int, float> f1, float tol)
        {
            int n = 0;
            for (int r = 0; r < EN; r++) for (int c = 0; c < EN; c++) if (MathF.Abs(f0(c, r) - f1(c, r)) > tol) n++;
            if (n > 0) _repChanges.Add((name, "ค่าเดิม", $"แก้ {n} ช่อง"));
        }
        Tab("ตารางน้ำมัน (ms)", m0.CellMs, m1.CellMs, 0.005f);
        Tab("ตารางองศาฉีด", m0.CellEoi, m1.CellEoi, 0.5f);
        Tab("ตารางองศาจุด", m0.CellAdv, m1.CellAdv, 0.05f);
        Tab("ตารางไทม์คอยล์", m0.CellDwell, m1.CellDwell, 0.005f);
        Tab("ตาราง MAP", m0.CellMap, m1.CellMap, 0.5f);

        _repP0 = Advisor.Predict(m0); _repP1 = Advisor.Predict(m1);
        float ra = MathF.Round(m1.D.Limiter * 0.35f / 100f) * 100f, rb = MathF.Round(m1.D.Limiter * 0.80f / 100f) * 100f;
        _repUp0 = m0.RevUpTime(ra, rb); _repUp1 = m1.RevUpTime(ra, rb);
        _repHold0 = m1.D.Auto == 1 ? Cvt.HoldRpm(m0) : 0f; _repHold1 = m1.D.Auto == 1 ? Cvt.HoldRpm(m1) : 0f;

        // ---- วิเคราะห์ ----
        var rpmA = new List<float>(); var hp = new List<float>(); var hp0 = new List<float>();
        for (float r = 2000f; r <= m1.D.Limiter; r += 100f)
        {
            rpmA.Add(r);
            hp.Add(MathF.Max(0f, EngineModel.Hp(m1.BrakeTorqueWot(r), r)));
            hp0.Add(r <= m0.D.Limiter ? MathF.Max(0f, EngineModel.Hp(m0.BrakeTorqueWot(r), r)) : float.NaN);
        }
        var zones = ChartZones(rpmA.ToArray(), hp.ToArray(), hp0.ToArray());
        float maxD = 0f;
        for (int i = 0; i < hp.Count; i++) if (!float.IsNaN(hp0[i])) maxD = MathF.Max(maxD, MathF.Abs(hp[i] - hp0[i]));
        if (_repChanges.Count == 0) { _repNotes.Add(("ยังไม่ได้แก้อะไรจากเครื่องที่ติดตั้งอยู่", 0)); _showReport = true; return; }

        float dh = m1.PeakHp - m0.PeakHp, dt = m1.PeakTq - m0.PeakTq;
        if (maxD < 0.05f)
            _repNotes.Add(("กราฟแรงม้า/แรงบิดของตัวเครื่องไม่เปลี่ยน — สิ่งที่แก้อยู่ที่ชุดส่งกำลัง/ตัวรถ (ชามเม็ด เม็ด สปริง เฟือง ล้อ น้ำหนัก ฯลฯ) ผลจึงดูที่เวลาวิ่งและรอบที่ CVT ถือ", 0));
        else
        {
            _repNotes.Add(($"กำลังสูงสุด {m0.PeakHp:0.0} » {m1.PeakHp:0.0} HP ({(dh >= 0 ? "+" : "")}{dh:0.0}) @ {m1.PeakPsRpm:N0} rpm   แรงบิดสูงสุด {m0.PeakTq:0.0} » {m1.PeakTq:0.0} N·m ({(dt >= 0 ? "+" : "")}{dt:0.0})", dh >= 0.05f ? 1 : dh < -0.05f ? 2 : 0));
            foreach (var z in zones.Take(3))
                _repNotes.Add(($"ช่วง {z.Lo / 1000f:0.0}–{z.Hi / 1000f:0.0}k rpm กำลัง{(z.Good ? "เพิ่ม" : "ลด")}สูงสุด {(z.Peak >= 0 ? "+" : "")}{z.Peak:0.0} HP", z.Good ? 1 : 2));
            if (zones.Count == 0) _repNotes.Add(("ต่างกันนิดเดียวทุกช่วงรอบ (ไม่ถึงเกณฑ์บอกเป็นช่วง)", 0));
            if (m1.PeakPsRpm != m0.PeakPsRpm) _repNotes.Add(($"พีคกำลังย้ายจาก {m0.PeakPsRpm:N0} ไป {m1.PeakPsRpm:N0} rpm ({(m1.PeakPsRpm > m0.PeakPsRpm ? "ไปรอบสูงขึ้น" : "ลงมารอบต่ำ")})", 0));
        }
        float dt402 = _repP1.T402 - _repP0.T402, dtop = _repP1.TopKmh - _repP0.TopKmh, d060 = _repP1.T060 - _repP0.T060;
        _repNotes.Add(($"402 ม. {_repP0.T402:0.00} » {_repP1.T402:0.00} วิ ({(dt402 <= 0 ? "เร็วขึ้น " : "ช้าลง ")}{MathF.Abs(dt402):0.00})   0–60 {_repP0.T060:0.00} » {_repP1.T060:0.00}   ท็อป {_repP0.TopKmh:0} » {_repP1.TopKmh:0} กม./ชม.",
            dt402 < -0.02f ? 1 : dt402 > 0.02f ? 2 : 0));
        if (m1.D.Auto == 1 && MathF.Abs(_repHold1 - _repHold0) > 20f)
        {
            float peak = m1.PeakPsRpm;
            bool nearer = MathF.Abs(_repHold1 - peak) < MathF.Abs(_repHold0 - peak);
            _repNotes.Add(($"CVT ถือรอบ {_repHold0:N0} » {_repHold1:N0} rpm (พีคกำลัง {peak:N0}) = {(nearer ? "ใกล้พีคขึ้น ดี" : "ห่างพีคขึ้น แย่ลง")}", nearer ? 1 : 2));
        }
        if (m1.KnockWot(3500f) > 0.02f && m0.KnockWot(3500f) <= 0.02f) _repNotes.Add(("เริ่มน็อกที่โหลดสูง (กำลังอัดสูงเกินน้ำมัน) — ลด CR/องศาจุด หรือใช้ออกเทนสูงขึ้น", 2));
        if (m1.D.Limiter >= m1.FloatRpm && m0.D.Limiter < m0.FloatRpm) _repNotes.Add(("รอบตัดสูงกว่ารอบวาล์วลอยแล้ว กำลังจะตกที่ปลายรอบ", 2));
        if (float.IsFinite(_repUp1) && float.IsFinite(_repUp0) && MathF.Abs(_repUp1 - _repUp0) > 0.03f)
            _repNotes.Add(($"เร่งรอบเครื่องเปล่า {_repUp0:0.00} » {_repUp1:0.00} วิ ({(_repUp1 < _repUp0 ? "ไวขึ้น" : "ยานขึ้น")})", 0));
        _showReport = true;
    }

    private void DrawReport(Graphics g)
    {
        g.FillRectangle(Br(Color.FromArgb(232, 6, 8, 11)), 0, 0, VW, VH);
        var box = new RectangleF(200, 40, 1520, 1000);
        FillRR(g, box, 6, Palette.Panel); DrawRR(g, box, 6, Palette.Accent, 2f);
        Txt(g, "สรุปการแก้ไข  (เทียบเครื่องที่ติดตั้งอยู่)", _fTitle, Palette.Accent, box.X + 36, box.Y + 18);
        TxtR(g, "คลิกที่ไหนก็ได้ / Esc / R เพื่อปิด", _fXs, Palette.Dim, box.Right - 36, box.Y + 34);

        // ซ้าย: สิ่งที่เปลี่ยน
        float lx = box.X + 36, ly = box.Y + 104, lw = 500;
        g.FillRectangle(Br(Palette.Yellow), lx, ly + 2, 4, 24);
        Txt(g, $"สิ่งที่เปลี่ยน ({_repChanges.Count} รายการ)", _fMd, Palette.Text, lx + 16, ly);
        float y = ly + 50;
        int maxRows = 14;
        for (int i = 0; i < Math.Min(maxRows, _repChanges.Count); i++)
        {
            var c = _repChanges[i];
            Txt(g, c.Name, _fCap, Palette.Dim, lx, y);
            Txt(g, $"{c.Old}  »  {c.New}", _fXs, Palette.Accent, lx, y + 20);
            g.DrawLine(Pn(Color.FromArgb(40, 46, 56), 1f), lx, y + 50, lx + lw, y + 50);
            y += 58;
        }
        if (_repChanges.Count > maxRows) Txt(g, $"... และอีก {_repChanges.Count - maxRows} รายการ", _fXs, Palette.Dim, lx, y);
        if (_repChanges.Count == 0) Txt(g, "ไม่มี", _fSm, Palette.Dim, lx, y);

        // ขวา: กราฟ + ตารางผลลัพธ์ + สรุป
        float rx = box.X + 580, rw = 900;
        if (_repM1 != null) DrawCompareChart(g, new RectangleF(rx, box.Y + 100, rw, 380), _repM1, _repM1.D.Limiter);

        float ty = box.Y + 500;
        Txt(g, "ผลเทียบ", _fCap, Palette.Dim, rx, ty);
        TxtR(g, "ก่อน", _fCap, Palette.Dim, rx + 470, ty); TxtR(g, "หลัง", _fCap, Palette.Dim, rx + 640, ty); TxtR(g, "ต่าง", _fCap, Palette.Dim, rx + rw, ty);
        ty += 26;
        void Row(string name, string a, string b, float diff, bool lowerBetter, string fmt)
        {
            Txt(g, name, _fXs, Palette.Text, rx, ty);
            TxtR(g, a, _fXs, Palette.Dim, rx + 470, ty); TxtR(g, b, _fXs, Palette.Text, rx + 640, ty);
            bool same = MathF.Abs(diff) < 0.0049f;
            bool good = lowerBetter ? diff < 0f : diff > 0f;
            TxtR(g, same ? "-" : (diff > 0 ? "▲ +" : "▼ ") + diff.ToString(fmt), _fXs, same ? Palette.Dim : good ? Palette.Green : Palette.Red, rx + rw, ty);
            ty += 28;
        }
        if (_repM0 != null && _repM1 != null)
        {
            Row("กำลังสูงสุด (HP)", $"{_repM0.PeakHp:0.0}", $"{_repM1.PeakHp:0.0}", _repM1.PeakHp - _repM0.PeakHp, false, "0.0");
            Row("แรงบิดสูงสุด (N·m)", $"{_repM0.PeakTq:0.0}", $"{_repM1.PeakTq:0.0}", _repM1.PeakTq - _repM0.PeakTq, false, "0.0");
            Row("0–60 กม./ชม. (วิ)", $"{_repP0.T060:0.00}", $"{_repP1.T060:0.00}", _repP1.T060 - _repP0.T060, true, "0.00");
            Row("100 ม. (วิ)", $"{_repP0.T100:0.00}", $"{_repP1.T100:0.00}", _repP1.T100 - _repP0.T100, true, "0.00");
            Row("402 ม. (วิ)", $"{_repP0.T402:0.00}", $"{_repP1.T402:0.00}", _repP1.T402 - _repP0.T402, true, "0.00");
            Row("ความเร็วปลาย 402 ม.", $"{_repP0.Trap:0}", $"{_repP1.Trap:0}", _repP1.Trap - _repP0.Trap, false, "0");
            Row("ท็อปสปีด (กม./ชม.)", $"{_repP0.TopKmh:0}", $"{_repP1.TopKmh:0}", _repP1.TopKmh - _repP0.TopKmh, false, "0");
            if (_repM1.D.Auto == 1)
                Row("CVT ถือรอบ (rpm)", $"{_repHold0:N0}", $"{_repHold1:N0}", _repHold1 - _repHold0, false, "0");
            string Tm(float v) => float.IsFinite(v) ? v.ToString("0.00") : "∞";
            Row("เร่งรอบเครื่องเปล่า (วิ)", Tm(_repUp0), Tm(_repUp1), float.IsFinite(_repUp0) && float.IsFinite(_repUp1) ? _repUp1 - _repUp0 : 0f, true, "0.00");
        }

        ty += 12;
        g.FillRectangle(Br(Palette.Yellow), rx, ty + 2, 4, 22);
        Txt(g, "สรุป", _fMd, Palette.Text, rx + 16, ty);
        ty += 40;
        foreach (var n in _repNotes)
        {
            Color col = n.State == 1 ? Palette.Green : n.State == 2 ? Palette.Red : Palette.Dim;
            g.DrawString((n.State == 1 ? "▲ " : n.State == 2 ? "▼ " : "• ") + n.Text, _fXs, Br(col), new RectangleF(rx, ty, rw, 70));
            ty += n.Text.Length > 70 ? 56 : 30;
            if (ty > box.Bottom - 40) break;
        }
        AddHit(new RectangleF(0, 0, VW, VH), _ => _showReport = false);
    }
}
