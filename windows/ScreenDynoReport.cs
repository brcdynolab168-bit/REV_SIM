namespace RevGame;

/// <summary>รายงานไดโน (DYNO REPORT): เปรียบเทียบหลายรอบวัด (Run) บนกราฟแกนคู่ HP / N·m พร้อมการ์ดพีค ตารางรอบวัด และบันทึกช่วงรอบที่ดีขึ้น/แย่ลง</summary>
public sealed partial class MainForm
{
    private sealed class DynoRun
    {
        public int N;
        public DynoResult R = null!;
        public string Vehicle = "";
        public string Spec = "";
        public float Inertia, CalK;
        public DateTime When;
        public bool Visible = true;
    }

    private readonly List<DynoRun> _dynoRuns = new();
    private int _dynoRunCounter;
    private static readonly Color[] RunColors =
    {
        Color.FromArgb(0, 122, 224), Color.FromArgb(224, 54, 66), Color.FromArgb(70, 200, 120), Color.FromArgb(240, 170, 40),
        Color.FromArgb(170, 110, 240), Color.FromArgb(40, 210, 220),
    };

    private void AddDynoRun(DynoResult r)
    {
        var d = r.Model.D;
        string name = "ออกแบบเอง";
        string ser = d.Serialize();
        foreach (var p in EngineDesign.Presets) if (p.D.Serialize() == ser) { name = p.Name.TrimEnd('*'); break; }
        _dynoRuns.Add(new DynoRun
        {
            N = ++_dynoRunCounter, R = r, Vehicle = name, When = DateTime.Now, Inertia = r.Model.Inertia, CalK = d.CalK,
            Spec = $"{r.Model.Cc:0} cc  {d.Bore:0.#}x{d.Stroke:0.#}  CR {d.Cr:0.#}  {(d.Auto == 1 ? "CVT" : $"{d.Gears} สปีด")}",
        });
        while (_dynoRuns.Count > 5) _dynoRuns.RemoveAt(0);
        // แสดงสองรอบล่าสุดเป็นค่าเริ่มต้น (รอบก่อนหน้า vs รอบนี้)
        for (int i = 0; i < _dynoRuns.Count; i++) _dynoRuns[i].Visible = i >= _dynoRuns.Count - 2;
    }

    private static float RunHp(DynoResult r, int i) => float.IsNaN(r.Tq[i]) ? float.NaN : EngineModel.Hp(r.Tq[i], i * 100f);

    private static float NiceStep(float maxVal)
    {
        float[] s = { 1f, 2f, 4f, 5f, 10f, 20f, 25f, 40f, 50f, 100f, 200f, 250f, 500f };
        foreach (var v in s) if (v * 5f >= maxVal) return v;
        return 1000f;
    }

    private void DrawDynoResult(Graphics g)
    {
        var runs = _dynoRuns.Where(r => r.Visible).ToList();
        if (runs.Count == 0 && _dynoRuns.Count > 0) { _dynoRuns[^1].Visible = true; runs = new List<DynoRun> { _dynoRuns[^1] }; }
        // พื้นที่คลิกเต็มจอ ลงทะเบียนก่อน (ปุ่ม/แถวตารางลงทีหลังจึงกดได้)
        AddHit(new RectangleF(0, 0, VW, VH), _ => _showDyno = false);
        FillRR(g, new RectangleF(0, 0, VW, VH), 0, Color.FromArgb(205, 0, 0, 0));

        var box = new RectangleF(70, 24, 1780, 1032);
        FillRR(g, box, 4, Color.FromArgb(252, 15, 18, 24));
        DrawRR(g, box, 4, Palette.Border, 1.5f);
        AddHit(box, _ => { });                                                                      // คลิกในรายงานไม่ปิด

        // ---------- หัวรายงาน ----------
        g.FillRectangle(Br(Color.FromArgb(8, 11, 18)), box.X, box.Y, box.Width, 84);
        g.FillRectangle(Br(Color.FromArgb(0, 122, 224)), box.X, box.Y + 84, box.Width, 4);
        Txt(g, "REV LAB  DYNO REPORT", _fTitle, Color.White, box.X + 36, box.Y + 14);
        var last = _dynoRuns[^1];
        TxtR(g, "Rev Lab Dyno", _fMd, Color.White, box.Right - 36, box.Y + 12);
        TxtR(g, last.When.ToString("yyyy-MM-dd  HH:mm"), _fSm, Palette.Dim, box.Right - 36, box.Y + 48);

        // ---------- ข้อมูลรถ ----------
        float ix = box.X + 48, iy = box.Y + 112;
        void Info(string label, string val, float x, float w)
        {
            Txt(g, label, _fCap, Palette.Dim, x, iy);
            Txt(g, val, _fSm, Palette.Text, x, iy + 26);
            g.DrawLine(Pn(Palette.Border, 1f), x, iy + 64, x + w, iy + 64);
        }
        Info("VEHICLE", last.Vehicle, ix, 520);
        Info("ENGINE", last.Spec, ix + 560, 560);
        Info("ROTATING INERTIA", $"{last.Inertia:0.0000} kg·m²", ix + 1160, 280);
        Info("CALIBRATION", Math.Abs(last.CalK - 1f) < 0.005f ? "none (x1.000)" : $"CalK x{last.CalK:0.000}", ix + 1480, 250);

        // ---------- การ์ดพีค ----------
        var newest = runs.OrderBy(r => r.N).Last();
        var oldest = runs.OrderBy(r => r.N).First();
        float cy = box.Y + 200, ch = 120, cw = 404;
        void Card(float x, string title, string big, string unit, string sub, Color accent, Color bigCol)
        {
            var r = new RectangleF(x, cy, cw, ch);
            FillRR(g, r, 5, Color.FromArgb(22, 26, 34)); DrawRR(g, r, 5, Palette.Border, 1f);
            g.FillRectangle(Br(accent), r.X, r.Y + 8, 5, r.Height - 16);
            Txt(g, title, _fCap, Palette.Dim, r.X + 24, r.Y + 10);
            Txt(g, big, _fNumL, bigCol, r.X + 22, r.Y + 20);
            Txt(g, unit, _fSm, Palette.Dim, r.X + 30 + 40 * big.Length, r.Y + 62);
            Txt(g, sub, _fCap, Palette.Dim, r.X + 24, r.Bottom - 26);
        }
        float hpN = newest.R.PeakPs * EngineModel.PsToHp, hpO = oldest.R.PeakPs * EngineModel.PsToHp;
        Card(box.X + 48, "PEAK POWER", $"{hpN:0.0}", "HP", $"@ {newest.R.PeakPsRpm:N0} rpm  (Run {newest.N})", Palette.Green, Palette.Green);
        Card(box.X + 48 + 436, "PEAK TORQUE", $"{newest.R.PeakTq:0.0}", "N·m", $"@ {newest.R.PeakTqRpm:N0} rpm  (Run {newest.N})", Palette.Red, Palette.Red);
        bool two = oldest.N != newest.N;
        float dP = hpN - hpO, dT = newest.R.PeakTq - oldest.R.PeakTq;
        Color gc = Color.FromArgb(40, 170, 90);
        Card(box.X + 48 + 872, two ? $"Run {newest.N} vs Run {oldest.N}  POWER" : "POWER (เทียบ)", two ? $"{(dP >= 0 ? "+" : "")}{dP:0.0}" : "-", two ? "HP" : "", two ? $"{(dP >= 0 ? "ดีขึ้น" : "แย่ลง")} {(hpO > 0 ? dP / hpO * 100f : 0f):+0.0;-0.0}%" : "เปิดอย่างน้อย 2 รอบ", Palette.Border, two ? (dP >= 0 ? gc : Palette.Red) : Palette.Dim);
        Card(box.X + 48 + 1308, "TORQUE", two ? $"{(dT >= 0 ? "+" : "")}{dT:0.0}" : "-", two ? "N·m" : "", two ? $"{(dT >= 0 ? "ดีขึ้น" : "แย่ลง")} {(oldest.R.PeakTq > 0 ? dT / oldest.R.PeakTq * 100f : 0f):+0.0;-0.0}%" : "", Palette.Border, two ? (dT >= 0 ? gc : Palette.Red) : Palette.Dim);

        // ---------- กราฟแกนคู่ ----------
        var plot = new RectangleF(box.X + 100, box.Y + 392, box.Width - 200, 236);
        int iMin = int.MaxValue, iMax = -1;
        float maxHp = 1f, maxTq = 1f;
        foreach (var run in runs)
            for (int i = 0; i < run.R.Tq.Length; i++)
            {
                if (float.IsNaN(run.R.Tq[i])) continue;
                iMin = Math.Min(iMin, i); iMax = Math.Max(iMax, i);
                maxHp = MathF.Max(maxHp, RunHp(run.R, i)); maxTq = MathF.Max(maxTq, run.R.Tq[i]);
            }
        if (iMax < 0) { iMin = 20; iMax = 100; }
        float xMin = MathF.Floor(iMin * 100f / 500f) * 500f, xMax = MathF.Max(xMin + 1000f, MathF.Ceiling(iMax * 100f / 500f) * 500f);
        float hpStep = NiceStep(maxHp * 1.08f), tqStep = NiceStep(maxTq * 1.08f);
        float XP(float rpm) => plot.X + (rpm - xMin) / (xMax - xMin) * plot.Width;
        FillRR(g, plot, 3, Color.FromArgb(14, 17, 22)); DrawRR(g, plot, 3, Palette.Border, 1f);
        Txt(g, "HP", _fCap, Color.FromArgb(70, 200, 120), plot.X - 60, plot.Y - 30);
        Txt(g, "N·m", _fCap, Color.FromArgb(235, 90, 100), plot.Right + 14, plot.Y - 30);
        for (int k = 0; k <= 5; k++)
        {
            float y = plot.Bottom - k / 5f * plot.Height;
            g.DrawLine(Pn(Color.FromArgb(34, 40, 50), 1f), plot.X, y, plot.Right, y);
            TxtR(g, (k * hpStep).ToString("0"), _fXs, Color.FromArgb(70, 200, 120), plot.X - 10, y - 12);
            Txt(g, (k * tqStep).ToString("0"), _fXs, Color.FromArgb(235, 90, 100), plot.Right + 10, y - 12);
        }
        float xs = (xMax - xMin) > 6000f ? 1000f : 500f;
        for (float r = xMin; r <= xMax + 1; r += xs)
        {
            float x = XP(r);
            g.DrawLine(Pn(Color.FromArgb(34, 40, 50), 1f), x, plot.Y, x, plot.Bottom);
            TxtC(g, r.ToString("0"), _fXs, Palette.Dim, x, plot.Bottom + 18);
        }
        TxtC(g, "RPM", _fXs, Palette.Dim, plot.X + plot.Width / 2, plot.Bottom + 42);
        foreach (var run in runs)
        {
            Color c = RunColors[(run.N - 1) % RunColors.Length];
            using var penHp = new Pen(c, 3.4f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            using var penTq = new Pen(c, 2.4f) { DashPattern = new[] { 4f, 3f } };
            var ph = new List<PointF>(); var pt = new List<PointF>();
            for (int i = 0; i < run.R.Tq.Length; i++)
            {
                if (float.IsNaN(run.R.Tq[i])) continue;
                float rpm = i * 100f, x = XP(rpm);
                ph.Add(new PointF(x, plot.Bottom - Math.Clamp(RunHp(run.R, i) / (hpStep * 5f), 0f, 1f) * plot.Height));
                pt.Add(new PointF(x, plot.Bottom - Math.Clamp(run.R.Tq[i] / (tqStep * 5f), 0f, 1f) * plot.Height));
            }
            if (pt.Count > 1) g.DrawLines(penTq, pt.ToArray());
            if (ph.Count > 1) g.DrawLines(penHp, ph.ToArray());
            // จุดพีคกำลัง + ตัวเลข
            float px = XP(run.R.PeakPsRpm), py = plot.Bottom - Math.Clamp(run.R.PeakPs * EngineModel.PsToHp / (hpStep * 5f), 0f, 1f) * plot.Height;
            g.FillEllipse(Br(Color.FromArgb(15, 18, 24)), px - 7, py - 7, 14, 14); g.DrawEllipse(Pn(c, 3f), px - 7, py - 7, 14, 14);
            TxtC(g, $"{run.R.PeakPs * EngineModel.PsToHp:0.0}", _fSm, c, px, py - 24);
        }
        // คำอธิบาย (Run / HP / N·m)
        float lx = plot.X + 24, ly = plot.Y + 22;
        foreach (var run in runs.OrderBy(r => r.N))
        {
            Color c = RunColors[(run.N - 1) % RunColors.Length];
            g.DrawLine(Pn(c, 4f), lx, ly, lx + 34, ly); Txt(g, $"Run {run.N}", _fXs, Palette.Text, lx + 44, ly - 13); lx += 150;
        }
        g.DrawLine(Pn(Palette.Dim, 2.4f), lx + 20, ly, lx + 54, ly); Txt(g, "HP", _fXs, Palette.Dim, lx + 62, ly - 13);
        using (var dp = new Pen(Palette.Dim, 2.4f) { DashPattern = new[] { 4f, 3f } }) g.DrawLine(dp, lx + 120, ly, lx + 154, ly);
        Txt(g, "N·m", _fXs, Palette.Dim, lx + 162, ly - 13);

        // ---------- ตารางรอบวัด ----------
        float ty = plot.Bottom + 72;
        g.FillRectangle(Br(Color.FromArgb(24, 28, 37)), box.X + 48, ty, box.Width - 96, 30);
        float[] cx = { box.X + 90, box.X + 540, box.X + 860, box.X + 1150, box.X + 1490, box.X + 1660 };
        string[] hd = { "RUN", "PEAK POWER (HP)", "@ RPM", "PEAK TORQUE (N·m)", "@ RPM", "SAMPLES" };
        for (int i = 0; i < hd.Length; i++) Txt(g, hd[i], _fCap, Palette.Dim, cx[i], ty + 5);
        ty += 36;
        foreach (var run in _dynoRuns.OrderBy(r => r.N))
        {
            var rr = run; Color c = RunColors[(run.N - 1) % RunColors.Length];
            var row = new RectangleF(box.X + 48, ty, box.Width - 96, 30);
            if (!run.Visible) g.FillRectangle(Br(Color.FromArgb(14, 16, 21)), row);
            g.FillRectangle(Br(run.Visible ? c : Color.FromArgb(70, 76, 88)), box.X + 60, ty + 7, 16, 16);
            Color tc = run.Visible ? Palette.Text : Palette.Dim;
            Txt(g, $"Run {run.N}   {(run.Vehicle)}", _fXs, tc, cx[0], ty + 3);
            Txt(g, $"{run.R.PeakPs * EngineModel.PsToHp:0.0}", _fXs, tc, cx[1], ty + 3);
            Txt(g, $"{run.R.PeakPsRpm:N0}", _fXs, Palette.Dim, cx[2], ty + 3);
            Txt(g, $"{run.R.PeakTq:0.0}", _fXs, tc, cx[3], ty + 3);
            Txt(g, $"{run.R.PeakTqRpm:N0}", _fXs, Palette.Dim, cx[4], ty + 3);
            Txt(g, $"{run.R.Tq.Count(t => !float.IsNaN(t))}", _fXs, Palette.Dim, cx[5], ty + 3);
            g.DrawLine(Pn(Palette.Border, 1f), row.X, row.Bottom, row.Right, row.Bottom);
            AddHit(row, _ => { rr.Visible = !rr.Visible; if (!_dynoRuns.Any(q => q.Visible)) rr.Visible = true; });
            ty += 31;
        }
        Btn(g, new RectangleF(box.Right - 48 - 190, ty + 6, 190, 34), "ล้างประวัติรอบวัด", false, Palette.Red, () => { var keep = _dynoRuns[^1]; _dynoRuns.Clear(); _dynoRuns.Add(keep); keep.Visible = true; }, _fCap);
        Txt(g, "คลิกแถวในตารางเพื่อเปิด/ปิดเส้นของรอบนั้น  •  เส้นทึบ = HP  เส้นประ = N·m", _fCap, Palette.Dim, box.X + 52, ty + 14);

        // ---------- NOTES ----------
        float ny = ty + 58;
        g.FillRectangle(Br(Palette.Yellow), box.X + 40, ny + 2, 4, 20);
        Txt(g, "NOTES", _fCap, Palette.Dim, box.X + 56, ny);
        var notes = new List<(string, Color)>();
        if (two)
        {
            var o = oldest.R; var n = newest.R;
            var rpmA = new List<float>(); var h1 = new List<float>(); var h0 = new List<float>();
            for (int i = 0; i < Math.Min(o.Tq.Length, n.Tq.Length); i++)
            {
                if (float.IsNaN(n.Tq[i])) continue;
                rpmA.Add(i * 100f); h1.Add(RunHp(n, i)); h0.Add(RunHp(o, i));
            }
            foreach (var z in ChartZones(rpmA.ToArray(), h1.ToArray(), h0.ToArray()).Take(3))
                notes.Add(($"Run {newest.N} เทียบ Run {oldest.N}: ช่วง {z.Lo / 1000f:0.0}–{z.Hi / 1000f:0.0}k rpm กำลัง{(z.Good ? "เพิ่ม" : "ลด")}สูงสุด {(z.Peak >= 0 ? "+" : "")}{z.Peak:0.0} HP", z.Good ? Palette.Green : Palette.Red));
            if (notes.Count == 0) notes.Add(("สองรอบต่างกันน้อยมากในทุกช่วงรอบ", Palette.Dim));
            if (newest.R.PeakPsRpm != oldest.R.PeakPsRpm) notes.Add(($"พีคกำลังย้ายจาก {oldest.R.PeakPsRpm:N0} ไป {newest.R.PeakPsRpm:N0} rpm", Palette.Dim));
        }
        else notes.Add(("วิ่งไดโนอีกครั้งหลังแก้ค่า แล้วจะเทียบกับรอบนี้ให้ทั้งกราฟและตาราง", Palette.Dim));
        var rd = newest.R.Model.D;
        if (rd.RealHp > 0f || rd.RealTq > 0f)
        {
            string cmp = "เทียบรถจริง:  ";
            if (rd.RealHp > 0f) cmp += $"แรงม้า เกม {hpN:0.0} / จริง {rd.RealHp:0.0} HP ({(hpN / rd.RealHp - 1f) * 100f:+0;-0;0}%)   ";
            if (rd.RealTq > 0f) cmp += $"แรงบิด เกม {newest.R.PeakTq:0.0} / จริง {rd.RealTq:0.0} N·m ({(newest.R.PeakTq / rd.RealTq - 1f) * 100f:+0;-0;0}%)";
            notes.Add((cmp, Palette.Yellow));
        }
        float nyy = ny + 30;
        int nMax = Math.Max(1, Math.Min(3, (int)((box.Bottom - 52 - nyy) / 28)));
        foreach (var (t, c) in notes.Take(nMax)) { Txt(g, t, _fXs, c, box.X + 60, nyy); nyy += 28; }
        TxtR(g, "Generated by REV LAB (simulated dyno)  •  Enter / Esc / คลิกนอกรายงานเพื่อปิด", _fCap, Palette.Dim, box.Right - 36, box.Bottom - 28);
    }
}
