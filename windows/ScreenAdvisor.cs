namespace RevGame;

public sealed partial class MainForm
{
    private bool _advisor;                          // เปิดแผงผู้ช่วยจูนในอู่
    private int _advGoal;
    private readonly AdvConstraints _advC = new();
    private string _advKey = "";
    private List<Tip> _advTips = new();
    private List<Suggestion> _advList = new();

    private void ToggleAdvisor()
    {
        _advisor = !_advisor;
        _advKey = "";
    }

    private TestData CollectTests() => new()
    {
        PeakRpm = _peakRpm, TopKmh = _topSession, Last060 = _last060, Last100 = _last100, Last402 = _last402, Trap402 = _trap402,
        Dyno = _dyno, Before = _before, Cur = _cur,
    };

    private void RefreshAdvisor()
    {
        string key = $"{_draft.Serialize()}|{_advGoal}|{_advC.RpmCap}|{_advC.LockMask}|{_advC.Street}|{_save.Design.Serialize()}|{_last402:0.00}|{_topSession:0}|{_peakRpm:0}|{(_dyno == null ? 0 : _dyno.PeakPs):0.0}|{_before?.B402:0.00}";
        if (key == _advKey) return;
        _advKey = key;
        _advList = Advisor.Suggest(_draft, _advGoal, 5, _advC);
        // ฐานของการวิเคราะห์โจทย์ = แบบที่ใช้รอบตัดตามเป้าหมายแล้ว
        var capped = _draft;
        if (_advC.RpmCap > 0f && _advList.Count > 0 && _advList[0].Gain >= 999f) capped = _advList[0].Design;
        _advTips = Advisor.CapTips(capped, _advC);
        _advTips.AddRange(Advisor.Observe(_eng.Model, CollectTests()));
    }

    private void StepCap(int delta)
    {
        float cur = _advC.RpmCap > 0f ? _advC.RpmCap : MathF.Round(_draft.Limiter / 100f) * 100f;
        _advC.RpmCap = Math.Clamp(cur + delta, 6000f, 15000f);
        _advKey = "";
    }

    private void DrawAdvisor(Graphics g)
    {
        RefreshAdvisor();

        // เป้าหมาย
        float bw = (GW - 20) / 3f;
        for (int i = 0; i < Advisor.GoalNames.Length; i++)
        {
            int gi = i;
            Btn(g, new RectangleF(GL + i * (bw + 10), 176, bw, 46), "เป้าหมาย: " + Advisor.GoalNames[i], _advGoal == i, Palette.Accent,
                () => { _advGoal = gi; _advKey = ""; }, _fSm);
        }

        // โจทย์: รอบที่อยากใช้ + ใช้ถนน
        Txt(g, "รอบที่อยากใช้", _fXs, Palette.Dim, GL + 4, 242);
        SmallBtn(g, new RectangleF(GL + 150, 232, 44, 34), "−", () => { StepCap(-100); });
        FillRR(g, new RectangleF(GL + 200, 232, 180, 34), 4, Color.FromArgb(14, 17, 21));
        TxtC(g, _advC.RpmCap <= 0f ? "ไม่จำกัด" : $"{_advC.RpmCap:N0} rpm", _fSm, _advC.RpmCap > 0f ? Palette.Accent : Palette.Dim, GL + 290, 250);
        SmallBtn(g, new RectangleF(GL + 386, 232, 44, 34), "+", () => { StepCap(+100); });
        Btn(g, new RectangleF(GL + 450, 232, 150, 34), _advC.RpmCap > 0f ? "ยกเลิกรอบ" : "ใช้รอบเครื่องนี้", false, Palette.Blue,
            () => { _advC.RpmCap = _advC.RpmCap > 0f ? 0f : MathF.Round(_draft.Limiter / 100f) * 100f; _advKey = ""; }, _fXs);
        Btn(g, new RectangleF(GL + 616, 232, GW - 616, 34), _advC.Street ? "โจทย์ใช้ถนน: เปิด (เดินเบาต้องนิ่ง)" : "โจทย์ใช้ถนน: ปิด", _advC.Street, Palette.Green,
            () => { _advC.Street = !_advC.Street; _advKey = ""; }, _fXs);

        // ล็อกอะไหล่/ค่าที่ไม่ให้ที่ปรึกษาแตะ (ตั้งไว้ในอู่ตามโจทย์เราแล้วล็อก)
        Txt(g, "ล็อก (ใช้อะไหล่ตามที่เราตั้ง ที่ปรึกษาไม่แตะ):", _fXs, Palette.Dim, GL + 4, 278);
        const int perRow = 5;
        float cw = (GW - (perRow - 1) * 6) / perRow;
        for (int i = 0; i < Advisor.Groups.Length; i++)
        {
            int gi = i;
            var r = new RectangleF(GL + (i % perRow) * (cw + 6), 304 + (i / perRow) * 40, cw, 34);
            bool on = _advC.Locked(i);
            Btn(g, r, (on ? "[ล็อก] " : "") + Advisor.Groups[i].Name, on, Palette.Yellow, () => { _advC.LockMask ^= 1 << gi; _advKey = ""; }, _fXs);
        }

        // ข้อสังเกตจากผลทดสอบ
        var box = new RectangleF(GL, 392, GW, 240);
        PanelBox(g, box, "วิเคราะห์จากผลทดสอบ (เครื่องที่ติดตั้งอยู่)");
        float y = box.Y + 50;
        foreach (var tip in _advTips)
        {
            var col = tip.Level == 2 ? Palette.Yellow : tip.Level == 1 ? Palette.Green : Palette.Text;
            float w = box.Width - 64;
            var size = g.MeasureString(tip.Text, _fXs, (int)w);
            if (y + size.Height > box.Bottom - 8) { Txt(g, "…", _fXs, Palette.Dim, box.X + 24, y - 4); break; }
            g.FillEllipse(Br(tip.Level == 2 ? Palette.Yellow : tip.Level == 1 ? Palette.Green : Color.FromArgb(90, 98, 110)), box.X + 20, y + 6, 10, 10);
            g.DrawString(tip.Text, _fXs, Br(col), new RectangleF(box.X + 42, y, w, size.Height + 4));
            y += size.Height + 8;
        }

        // ข้อเสนอแนะ: ลองปรับทีละค่าในแบบจำลองแล้วจัดอันดับ
        var sb = new RectangleF(GL, 642, GW, 372);
        PanelBox(g, sb, "ควรปรับอะไร  (ตามโจทย์ จัดอันดับตามเป้าหมาย)");
        if (_advList.Count == 0)
        {
            g.DrawString("ไม่พบการปรับค่าเดี่ยว ๆ ที่ดีขึ้นอย่างมีนัยสำคัญโดยไม่ทำให้น็อก/วาล์วลอย — ลองเปลี่ยนหลายค่าพร้อมกัน เช่น ท่อ+แคม+รอบตัด",
                _fSm, Br(Palette.Dim), new RectangleF(sb.X + 24, sb.Y + 64, sb.Width - 48, 120));
            return;
        }
        float ry = sb.Y + 50;
        const float rh = 62;
        foreach (var s in _advList)
        {
            var sug = s;
            var row = new RectangleF(sb.X + 10, ry, sb.Width - 20, rh - 6);
            if (row.Contains(_mouseV)) g.FillRectangle(Br(Color.FromArgb(24, 28, 35)), row);
            Txt(g, sug.Label, _fSm, Palette.Text, row.X + 10, row.Y + 2);
            var a = sug.After; var b = sug.Before;
            string line = $"402 ม. {b.T402:0.00}→{a.T402:0.00} วิ  •  0–60 {b.T060:0.00}→{a.T060:0.00}  •  ท็อป {b.TopKmh:0}→{a.TopKmh:0} กม./ชม.";
            Txt(g, line, _fXs, Palette.Green, row.X + 10, row.Y + 30);
            Btn(g, new RectangleF(row.Right - 120, row.Y + 4, 112, row.Height - 8), "ใช้ค่านี้", false, Palette.Green, () =>
            {
                _draft = sug.Design.Clone();
                DraftChanged();
                _advKey = "";
                Toast("ปรับตามคำแนะนำ: " + sug.Label);
            }, _fXs);
            ry += rh;
        }
        if (_advList.Any(s => s.Note.Length > 0))
        {
            // หมายเหตุของข้อที่มีผลข้างเคียง แสดงท้ายแผง
            var n = _advList.First(s => s.Note.Length > 0);
            g.DrawString("ข้อควรระวัง: " + n.Note, _fXs, Br(Palette.Yellow), new RectangleF(sb.X + 20, sb.Bottom - 34, sb.Width - 40, 28));
        }
    }
}
