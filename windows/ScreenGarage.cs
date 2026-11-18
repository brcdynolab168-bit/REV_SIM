namespace RevGame;

public sealed partial class MainForm
{
    // ====================================================================
    //  โครงหน้าจอ: แถบนำทางซ้าย + หัวหน้า (ใช้ร่วมทุกหน้า)
    // ====================================================================

    private void DrawTopBar(Graphics g, string title, string hint)
    {
        const float rw = 220;
        g.FillRectangle(Br(Palette.Panel), 0, 0, rw, VH);
        g.DrawLine(Pn(Palette.Border, 1f), rw, 0, rw, VH);

        Txt(g, "REV LAB", _fTitle, Palette.Accent, 14, 20);
        Txt(g, "ห้องทดลองเครื่องยนต์", _fXs, Palette.Dim, 28, 82);
        g.DrawLine(Pn(Palette.Border, 1f), 20, 118, rw - 20, 118);

        (string Name, string Sub, Page P)[] nav =
        {
            ("ภาพรวม", "OVERVIEW", Page.Menu),
            ("อู่ออกแบบ", "WORKSHOP", Page.Garage),
            ("ห้องทดสอบ", "TEST BENCH", Page.Drive),
            ("ตั้งค่า", "SETTINGS", Page.Settings),
        };
        for (int i = 0; i < nav.Length; i++)
        {
            var r = new RectangleF(0, 136 + i * 78, rw, 72);
            bool act = _page == nav[i].P;
            bool hov = r.Contains(_mouseV);
            if (act) g.FillRectangle(Br(Palette.PanelHi), r);
            else if (hov) g.FillRectangle(Br(Color.FromArgb(26, 30, 37)), r);
            if (act) g.FillRectangle(Br(Palette.Accent), r.X, r.Y, 5, r.Height);
            Txt(g, nav[i].Name, _fMd, act ? Palette.Text : Palette.Dim, 30, r.Y + 10);
            Txt(g, nav[i].Sub, _fCap, act ? Palette.Accent : Color.FromArgb(90, 98, 110), 31, r.Y + 44);
            var pg = nav[i].P;
            AddHit(r, down: _ => GoTo(pg));
        }

        // การ์ดเครื่องที่ติดตั้ง
        var m = _eng.Model;
        float cy = VH - 320;
        g.DrawLine(Pn(Palette.Border, 1f), 20, cy, rw - 20, cy);
        Txt(g, "เครื่องที่ติดตั้ง", _fCap, Palette.Dim, 28, cy + 14);
        Txt(g, $"{m.PeakHp:0.0}", _fNumL, Palette.Accent, 24, cy + 38);
        Txt(g, "HP", _fMd, Palette.Dim, 28, cy + 118);
        Txt(g, $"@ {m.PeakPsRpm:N0} rpm", _fSm, Palette.Dim, 80, cy + 122);
        Txt(g, $"{m.Cc:0} cc  •  {m.PeakTq:0.0} N·m", _fSm, Palette.Text, 28, cy + 156);
        Txt(g, m.D.Auto == 1 ? "ออโต้ CVT" : "เกียร์ธรรมดา 4 สปีด", _fSm, Palette.Dim, 28, cy + 188);
        Btn(g, new RectangleF(20, VH - 76, rw - 40, 52), "ออกจากเกม", false, Palette.Red, () => Close(), _fSm);

        // หัวหน้า
        Txt(g, title, _fLg, Palette.Text, 252, 18);
        Txt(g, hint, _fXs, Palette.Dim, 254, 64);
        g.DrawLine(Pn(Palette.Border, 1f), rw, 98, VW, 98);
    }

    // ====================================================================
    //  อู่: โครงแท็บ
    // ====================================================================

    private int _tab;                    // 0 = อะไหล่/เช็ต, 1.. = หมวดปรับละเอียด
    private int _partRow;

    private static readonly (string Name, string[] Keys)[] GarageTabs =
    {
        ("อะไหล่", Array.Empty<string>()),
        ("เครื่องยนต์", new[] { "bore", "stroke", "rod", "crank", "fly", "valves" }),
        ("ไอดี·แคม·ท่อ", new[] { "throttle", "lsa", "duration", "durationex", "lift", "liftex", "pipe" }),
        ("อัด·น้ำมัน", new[] { "cr", "psi", "fuel", "limiter", "resume", "softcut" }),
        ("ส่งกำลัง", new[] { "auto", "prim", "gears", "g1", "g2", "g3", "g4", "g5", "g6", "sprf", "sprr", "ramp", "rampout", "rollers", "rollerg", "rollerg2", "fdf", "fdr", "firstred", "spring" }),
        ("ตัวรถ", new[] { "rider", "bikekg", "rim", "rimw", "tirew", "tireasp", "cda" }),
        ("รถจริง", new[] { "realhp", "realhprpm", "realtq", "realtqrpm", "calk" }),
    };

    private void SetTab(int t)
    {
        _advisor = false;
        _tab = (t + GarageTabs.Length) % GarageTabs.Length;
        _sel = 0;
        RefreshVisible();
    }

    /// <summary>รายการค่าที่แสดงตามแท็บและแบบที่เลือก (เกียร์ธรรมดาซ่อนชุด CVT, ออโต้ซ่อนสเตอร์)</summary>
    private void RefreshVisible()
    {
        _vis.Clear();
        var keys = GarageTabs[_tab].Keys;
        for (int i = 0; i < EngineDesign.Params.Length; i++)
        {
            var p = EngineDesign.Params[i];
            if (Array.IndexOf(keys, p.Key) >= 0 && p.IsVisible(_draft)) _vis.Add(i);
        }
        _sel = _vis.Count == 0 ? 0 : Math.Clamp(_sel, 0, _vis.Count - 1);
    }

    private void DraftChanged()
    {
        _preview = new EngineModel(_draft);
        RefreshVisible();
    }

    private void Nudge(int row, int dir, bool coarse)
    {
        if (row < 0 || row >= _vis.Count) return;
        EngineDesign.Params[_vis[row]].Nudge(_draft, dir, coarse);
        DraftChanged();
    }

    // ====================================================================
    //  อู่: อินพุต
    // ====================================================================

    /// <summary>แถวอะไหล่: 0 = เช็ตเครื่องสำเร็จรูป, 1.. = หมวดอะไหล่</summary>
    private void PartStep(int row, int dir)
    {
        _partRow = row;
        if (row == 0)
        {
            int cur = -1;
            for (int i = 0; i < Parts.Sets.Length; i++) if (Parts.Sets[i].D.Serialize() == _draft.Serialize()) cur = i;
            int n = Parts.Sets.Length;
            int ni = cur < 0 ? (dir > 0 ? 0 : n - 1) : (cur + dir + n) % n;
            _draft = Parts.Sets[ni].D.Clone();
            DraftChanged();
            Toast("เช็ตเครื่อง: " + Parts.Sets[ni].Name);
            return;
        }
        var cat = Parts.Categories[row - 1];
        int c = cat.Current(_draft);
        int m = cat.Items.Length;
        int idx = c < 0 ? (dir > 0 ? 0 : m - 1) : Math.Clamp(c + dir, 0, m - 1);
        cat.Items[idx].Apply(_draft);
        DraftChanged();
    }

    private void GarageKey(Keys k, bool shift)
    {
        if (_advisor && (k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right)) return;     // แผงที่ปรึกษาไม่มีแถวให้ปรับ
        int n = _vis.Count;
        bool parts = _tab == 0;
        int prow = Parts.Categories.Length + 1;
        switch (k)
        {
            case Keys.Up: if (parts) _partRow = (_partRow + prow - 1) % prow; else if (n > 0) _sel = (_sel + n - 1) % n; break;
            case Keys.Down: if (parts) _partRow = (_partRow + 1) % prow; else if (n > 0) _sel = (_sel + 1) % n; break;
            case Keys.Left: if (parts) PartStep(_partRow, -1); else Nudge(_sel, -1, shift); break;
            case Keys.Right: if (parts) PartStep(_partRow, +1); else Nudge(_sel, +1, shift); break;
            case Keys.Tab: SetTab(_tab + (shift ? -1 : 1)); break;
            case Keys.D1: case Keys.NumPad1: LoadPreset(0); break;
            case Keys.D2: case Keys.NumPad2: LoadPreset(1); break;
            case Keys.D3: case Keys.NumPad3: LoadPreset(2); break;
            case Keys.D4: case Keys.NumPad4: LoadPreset(3); break;
            case Keys.D5: case Keys.NumPad5: LoadPreset(4); break;
            case Keys.D6: case Keys.NumPad6: LoadPreset(5); break;
            case Keys.D7: case Keys.NumPad7: LoadPreset(6); break;
            case Keys.D8: case Keys.NumPad8: LoadPreset(7); break;
            case Keys.T: ToggleAdvisor(); break;
            case Keys.C: CalibrateToReal(); break;
            case Keys.A: AutoTuneRollers(); break;
            case Keys.Enter: Assemble(); break;
            case Keys.G: GoTo(Page.Drive); break;
            case Keys.Escape: GoTo(Page.Menu); break;
        }
    }

    /// <summary>ปรับน้ำหนักเม็ด CVT ให้ถือรอบตรงพีคกำลังของเครื่องตอนนี้ (รถออโต้)</summary>
    private void AutoTuneRollers()
    {
        if (_draft.Auto != 1) { Toast("จูนเม็ดอัตโนมัติใช้กับรถออโต้เท่านั้น"); return; }
        var (g, hold) = Cvt.SuggestRoller(_preview);
        _draft.RollerG = g;
        _draft.RollerG2 = g;
        DraftChanged();
        string edge = g <= 4f ? " (ถึงขอบเขตเบาสุดแล้ว ลองเพิ่มองศาชาม/ลดจำนวนเม็ด)" : g >= 20f ? " (ถึงขอบเขตหนักสุดแล้ว)" : "";
        Toast($"จูนเม็ดเป็น {g:0.#} กรัม ทุกเม็ด  CVT ถือรอบ ~{hold:N0} rpm (พีคกำลัง {_preview.PeakPsRpm:N0}){edge}");
    }

    /// <summary>ปรับเทียบโมเดลให้พีคตรงแรงม้า/แรงบิดรถจริงที่ใส่ไว้ (เก็บเป็นตัวคูณคงที่ เปลี่ยนอะไหล่แล้วยังเทียบได้)</summary>
    private void CalibrateToReal()
    {
        var k = EngineModel.Calibrate(_draft);
        if (k == null) { Toast("ใส่แรงม้าหรือแรงบิดของรถจริง (แท็บ รถจริง) ก่อน"); return; }
        _draft.CalK = k.Value;
        DraftChanged();
        Toast($"ปรับเทียบแล้ว: ตัวคูณ {k.Value:0.00}  โมเดล {_preview.PeakHp:0.0} HP / {_preview.PeakTq:0.0} N·m");
    }

    private void DrawCalibrateButtons(Graphics g)
    {
        float y = 184 + 5 * Math.Min(88f, 700f / 5f) + 20;
        Btn(g, new RectangleF(GL, y, 520, 60), "C  ปรับเทียบโมเดลให้ตรงรถจริง", Math.Abs(_draft.CalK - 1f) > 0.005f, Palette.Green, CalibrateToReal, _fSm);
        Btn(g, new RectangleF(GL + 536, y, GW - 536, 60), "ล้างค่ารถจริง/ปรับเทียบ", false, Palette.Red, () =>
        {
            _draft.RealHp = _draft.RealHpRpm = _draft.RealTq = _draft.RealTqRpm = 0f;
            _draft.CalK = 1f;
            DraftChanged();
            Toast("ล้างค่ารถจริงแล้ว");
        }, _fSm);
    }

    private void LoadPreset(int i)
    {
        _draft = EngineDesign.Presets[i].D.Clone();
        DraftChanged();
        Toast("โหลดแบบ: " + EngineDesign.Presets[i].Name);
    }

    // ====================================================================
    //  อู่: วาด
    // ====================================================================

    private const float GL = 252, GW = 896;      // คอลัมน์ซ้ายของอู่

    private void DrawGarage(Graphics g)
    {
        DrawTopBar(g, "อู่ออกแบบเครื่อง",
            "↑ ↓ เลือก  •  ← → ปรับ (Shift = เร็ว)  •  Tab เปลี่ยนหมวด  •  1-6 แบบเดิม  •  A จูนเม็ด  •  T ที่ปรึกษาจูน  •  Enter ประกอบแล้วไปทดสอบ");

        // แท็บ
        float tw = (GW - 136) / GarageTabs.Length;
        for (int i = 0; i < GarageTabs.Length; i++)
        {
            int idx = i;
            var r = new RectangleF(GL + i * tw, 112, tw - 4, 52);
            bool act = i == _tab && !_advisor;
            bool hov = r.Contains(_mouseV);
            g.FillRectangle(Br(act ? Palette.PanelHi : (hov ? Color.FromArgb(26, 30, 37) : Palette.Panel)), r);
            if (act) g.FillRectangle(Br(Palette.Accent), r.X, r.Bottom - 4, r.Width, 4);
            TxtC(g, GarageTabs[i].Name, _fXs, act ? Palette.Text : Palette.Dim, r.X + r.Width / 2, r.Y + 26);
            AddHit(r, down: _ => { _advisor = false; SetTab(idx); });
        }
        Btn(g, new RectangleF(GL + GW - 132, 112, 132, 52), "T ที่ปรึกษา", _advisor, Palette.Blue, ToggleAdvisor, _fSm);

        if (_advisor) DrawAdvisor(g);
        else if (_tab == 0) DrawParts(g);
        else
        {
            DrawSpecRows(g);
            if (Array.IndexOf(GarageTabs[_tab].Keys, "realhp") >= 0) DrawCalibrateButtons(g);
        }

        DrawGarageStats(g);
    }

    private int _specScroll, _partScroll;       // แถวแรกที่เห็นในรายการ (เลื่อนเมื่อมีแถวเกินจอ จะได้ไม่เบียดกัน)

    /// <summary>แถบเลื่อนบาง ๆ ขวาสุดของรายการ + ข้อความ "แสดง x–y จาก n"</summary>
    private void DrawListScroll(Graphics g, float y0, float height, int first, int shown, int total)
    {
        if (total <= shown) return;
        float th = Math.Max(30f, height * shown / total);
        float ty = y0 + (height - th) * first / Math.Max(1, total - shown);
        g.FillRectangle(Br(Color.FromArgb(40, 46, 56)), GL + GW + 6, y0, 5, height);
        g.FillRectangle(Br(Palette.Accent), GL + GW + 6, ty, 5, th);
        TxtR(g, $"แสดง {first + 1}–{first + shown} จาก {total}  (เลื่อนลูกกลิ้ง / ↑ ↓)", _fXs, Palette.Dim, GL + GW, y0 + height + 8);
    }

    private void DrawSpecRows(Graphics g)
    {
        const float y0 = 184, rowH = 84, listH = 672;
        const int visible = 8;
        var ps = EngineDesign.Params;
        int total = _vis.Count;
        _specScroll = Math.Clamp(_specScroll, 0, Math.Max(0, total - visible));
        if (_sel < _specScroll) _specScroll = _sel;
        if (_sel >= _specScroll + visible) _specScroll = _sel - visible + 1;
        int shown = Math.Min(visible, total - _specScroll);
        AddHit(new RectangleF(GL, y0, GW, listH), wheel: dir => _specScroll = Math.Clamp(_specScroll - dir, 0, Math.Max(0, total - visible)));
        for (int k = 0; k < shown; k++)
        {
            int i = _specScroll + k;
            int row = i;
            var def = ps[_vis[i]];
            float v = def.Get(_draft);
            float y = y0 + k * rowH;
            var rowRect = new RectangleF(GL, y, GW, rowH - 8);
            bool selected = i == _sel;
            if (selected) { g.FillRectangle(Br(Palette.PanelHi), rowRect); g.FillRectangle(Br(Palette.Accent), rowRect.X, rowRect.Y, 4, rowRect.Height); }
            else if (rowRect.Contains(_mouseV)) g.FillRectangle(Br(Color.FromArgb(24, 28, 35)), rowRect);
            AddHit(rowRect, down: _ => _sel = row);

            Txt(g, def.Name, _fMd, selected ? Palette.Text : Palette.Dim, GL + 20, y + 8);
            TxtR(g, def.Display(_draft), _fNum, selected ? Palette.Accent : Palette.Text, GL + GW - 20, y + 6);

            float sy = y + 40;
            SmallBtn(g, new RectangleF(GL + 16, sy, 40, 30), "−", () => { _sel = row; Nudge(row, -1, ModifierKeys.HasFlag(Keys.Shift)); });
            float frac = (v - def.Min) / (def.Max - def.Min);
            var d0 = def;
            var sliderRect = new RectangleF(GL + 70, sy - 2, GW - 140, 34);
            AddHit(sliderRect, wheel: dir => { _sel = row; Nudge(row, dir, ModifierKeys.HasFlag(Keys.Shift)); });   // ลูกกลิ้งบนแถบ = ปรับค่า
            Slider(g, sliderRect, frac, selected ? Palette.Accent : Color.FromArgb(150, 100, 25), f =>
            {
                _sel = row;
                float nv = d0.Min + f * (d0.Max - d0.Min);
                nv = MathF.Round(nv / d0.Step) * d0.Step;
                d0.Set(_draft, Math.Clamp(nv, d0.Min, d0.Max));
                DraftChanged();
            });
            SmallBtn(g, new RectangleF(GL + GW - 56, sy, 40, 30), "+", () => { _sel = row; Nudge(row, +1, ModifierKeys.HasFlag(Keys.Shift)); });
        }
        DrawListScroll(g, y0, listH - 8, _specScroll, shown, total);

        if (_vis.Count > 0)
        {
            var sel = ps[_vis[_sel]];
            DrawHintBox(g, sel.Name + ": " + sel.Hint);
        }

        // แรงดันอัด: เทียบเกณฑ์ (แท็บอัด · น้ำมัน)
        if (_tab == 3) DrawPsiScale(g, 184 + total * rowH + 10);
    }

    private void DrawHintBox(Graphics g, string text)
    {
        var box = new RectangleF(GL, 900, GW, 120);
        FillRR(g, box, 4, Palette.Panel);
        DrawRR(g, box, 4, Palette.Border, 1f);
        g.FillRectangle(Br(Palette.Yellow), box.X + 14, box.Y + 14, 4, 20);
        Txt(g, "คำอธิบาย", _fCap, Palette.Dim, box.X + 28, box.Y + 14);
        g.DrawString(text, _fSm, Br(Palette.Yellow), new RectangleF(box.X + 20, box.Y + 42, box.Width - 40, box.Height - 46));
    }

    /// <summary>แถบเทียบแรงดันอัดที่วัดได้กับเกณฑ์: ต่ำ / เดิม / แต่ง / สูงมาก</summary>
    private void DrawPsiScale(Graphics g, float y)
    {
        var m = _preview;
        float psi = m.Psi;
        var box = new RectangleF(GL, y, GW, 150);
        FillRR(g, box, 4, Palette.Panel);
        DrawRR(g, box, 4, Palette.Border, 1f);
        g.FillRectangle(Br(Palette.Accent), box.X + 14, box.Y + 14, 4, 20);
        Txt(g, "เกจแรงดันอัด (วัดตอนสตาร์ท)", _fSm, Palette.Dim, box.X + 28, box.Y + 10);
        TxtR(g, $"{psi:0} psi  =  {psi * 6.895f:0} kPa  =  {psi / 14.5038f:0.0} bar", _fNumS, Palette.Text, box.Right - 20, box.Y + 12);

        const float lo = 100f, hi = 480f;
        float bx = box.X + 24, bw = box.Width - 48, by = box.Y + 64;
        (float a, float b, Color c, string n)[] zones =
        {
            (100f, 150f, Palette.Red, "ต่ำ/สึก"), (150f, 220f, Palette.Green, "เดิมโรงงาน"),
            (220f, 300f, Palette.Yellow, "แต่งแรง"), (300f, 480f, Palette.Red, "สูงจัด"),
        };
        foreach (var z in zones)
        {
            float x1 = bx + (z.a - lo) / (hi - lo) * bw, x2 = bx + (z.b - lo) / (hi - lo) * bw;
            g.FillRectangle(Br(Color.FromArgb(120, z.c)), x1, by, x2 - x1 - 2, 18);
            TxtC(g, z.n, _fXs, Palette.Dim, (x1 + x2) / 2, by + 36);
        }
        float mx = bx + Math.Clamp((psi - lo) / (hi - lo), 0f, 1f) * bw;
        g.FillRectangle(Br(Palette.Text), mx - 2, by - 12, 4, 42);
        g.FillPolygon(Br(Palette.Text), new[] { new PointF(mx - 8, by - 20), new PointF(mx + 8, by - 20), new PointF(mx, by - 8) });
        Txt(g, $"CR {_draft.Cr:0.0}:1 สถิต  →  {m.CrDyn:0.0}:1 จริงในกระบอก (แคม IN {_draft.Duration:0}° / EX {_draft.DurationEx:0}°)", _fXs, Palette.Dim, box.X + 24, box.Bottom - 30);
    }

    private void DrawParts(Graphics g)
    {
        const float y0 = 184, rowH = 64, listH = 640;
        int rows = Parts.Categories.Length + 1;
        int visible = (int)(listH / rowH);
        _partScroll = Math.Clamp(_partScroll, 0, Math.Max(0, rows - visible));
        if (_partRow < _partScroll) _partScroll = _partRow;
        if (_partRow >= _partScroll + visible) _partScroll = _partRow - visible + 1;
        int shown = Math.Min(visible, rows - _partScroll);
        AddHit(new RectangleF(GL, y0, GW, listH), wheel: dir => _partScroll = Math.Clamp(_partScroll - dir, 0, Math.Max(0, rows - visible)));
        for (int k = 0; k < shown; k++)
        {
            int r = _partScroll + k;
            int row = r;
            float y = y0 + k * rowH;
            var rowRect = new RectangleF(GL, y, GW, rowH - 8);
            bool selected = r == _partRow;
            if (selected) { g.FillRectangle(Br(Palette.PanelHi), rowRect); g.FillRectangle(Br(Palette.Accent), rowRect.X, rowRect.Y, 4, rowRect.Height); }
            else if (rowRect.Contains(_mouseV)) g.FillRectangle(Br(Color.FromArgb(24, 28, 35)), rowRect);
            AddHit(rowRect, down: _ => _partRow = row);

            string name, shownTxt;
            if (r == 0)
            {
                name = "เช็ตเครื่องสำเร็จรูป";
                int cur = -1;
                for (int i = 0; i < Parts.Sets.Length; i++) if (Parts.Sets[i].D.Serialize() == _draft.Serialize()) cur = i;
                shownTxt = cur < 0 ? "ปรับเอง  (ใช้ลูกศรซ้าย-ขวาเลือกเช็ต)" : Parts.Sets[cur].Name;
            }
            else
            {
                var cat = Parts.Categories[r - 1];
                name = cat.Name;
                int c = cat.Current(_draft);
                shownTxt = c < 0 ? "ปรับเอง  (ไม่ตรงชุดสำเร็จรูป)" : cat.Items[c].Name;
            }
            float cy = y + (rowH - 8) / 2;
            Txt(g, name, _fSm, selected ? Palette.Text : Palette.Dim, GL + 20, cy - 14);
            SmallBtn(g, new RectangleF(GL + 300, y + 4, 52, rowH - 16), "◀", () => PartStep(row, -1));
            FillRR(g, new RectangleF(GL + 360, y + 4, 480, rowH - 16), 4, Color.FromArgb(14, 17, 21));
            TxtC(g, shownTxt, _fSm, selected ? Palette.Accent : Palette.Text, GL + 600, cy);
            SmallBtn(g, new RectangleF(GL + 848, y + 4, 40, rowH - 16), "▶", () => PartStep(row, +1));
        }
        DrawListScroll(g, y0, listH - 8, _partScroll, shown, rows);
        string hint = _partRow == 0
            ? "เลือกเช็ตเครื่องทั้งชุด (* = ตัวอย่างสมมติ ไม่ใช่สเปกรถจริง) แล้วสลับอะไหล่ทีละชิ้นต่อได้"
            : Parts.Categories[_partRow - 1].Name + ": " + Parts.Categories[_partRow - 1].Hint;
        DrawHintBox(g, hint);
    }

    // ====================================================================
    //  อู่: แผงสมรรถนะ (ขวา)
    // ====================================================================

    private void DrawGarageStats(Graphics g)
    {
        var m = _preview;
        var d = _draft;
        const float X = 1180, W = 700;

        // ตัวเลขใหญ่
        PanelBox(g, new RectangleF(X, 112, W, 200), "สมรรถนะ (คาดการณ์)");
        Txt(g, $"{m.PeakHp:0.0}", _fNumL, Palette.Accent, X + 24, 150);
        Txt(g, $"HP @ {m.PeakPsRpm:N0} rpm", _fSm, Palette.Dim, X + 28, 232);
        Txt(g, $"{m.PeakTq:0.0}", _fNumL, Palette.Blue, X + 360, 150);
        Txt(g, $"N·m @ {m.PeakTqRpm:N0} rpm", _fSm, Palette.Dim, X + 364, 232);
        Txt(g, $"{m.Cc:0.0} cc   •   ก้าน/ชัก {m.RodRatio:0.00}   •   รถ+คน {Vehicle.Mass(d):0} กก.   •   {m.PeakHp / m.Cc * 1000f:0} HP/ลิตร",
            _fXs, Palette.Dim, X + 28, 272);
        if (d.RealHp > 0f || d.RealTq > 0f)
        {
            var sbr = new System.Text.StringBuilder("รถจริง:  ");
            if (d.RealHp > 0f) sbr.Append($"{d.RealHp:0.0} HP{(d.RealHpRpm > 0f ? $" @ {d.RealHpRpm:N0}" : "")} (โมเดลต่าง {(m.PeakHp / d.RealHp - 1f) * 100f:+0;-0;0}%)   ");
            if (d.RealTq > 0f) sbr.Append($"{d.RealTq:0.0} N·m{(d.RealTqRpm > 0f ? $" @ {d.RealTqRpm:N0}" : "")} (ต่าง {(m.PeakTq / d.RealTq - 1f) * 100f:+0;-0;0}%)");
            Txt(g, sbr.ToString(), _fXs, Math.Abs(d.CalK - 1f) > 0.005f ? Palette.Green : Palette.Yellow, X + 28, 292);
        }

        // กราฟ
        var rpms = new List<float>();
        var tq = new List<float>();
        var hp = new List<float>();
        for (float r = 2000f; r <= d.Limiter; r += 100f)
        {
            float t = m.BrakeTorqueWot(r);
            rpms.Add(r); tq.Add(MathF.Max(0f, t)); hp.Add(MathF.Max(0f, EngineModel.Hp(t, r)));
        }
        DrawChart(g, new RectangleF(X, 326, W, 250), rpms.ToArray(), tq.ToArray(), hp.ToArray(), d.Limiter);

        // ตรวจสภาพ
        PanelBox(g, new RectangleF(X, 592, W, 378), "ตรวจสภาพเครื่อง");
        float y = 640;
        void Line(string s, int state)
        {
            Color dot = state == 2 ? Palette.Yellow : state == 1 ? Palette.Green : Color.FromArgb(90, 98, 110);
            g.FillEllipse(Br(dot), X + 22, y + 9, 12, 12);
            Txt(g, s, _fSm, state == 2 ? Palette.Yellow : Palette.Text, X + 46, y);
            y += 32;
        }

        Line($"เดินเบา ~{m.IdleRpm:N0} rpm  •  โอเวอร์แลป {m.Overlap:0}°" + (m.Overlap > 40f ? "  (สะดุด)" : ""), m.Overlap > 40f ? 2 : 1);

        bool flowLimit = m.RpmFlow < m.PeakPsRpm;
        Line($"ไอดีเริ่มอั้นที่ {m.RpmFlow:N0} rpm" + (flowLimit ? "  (เรือน/วาล์วเล็กไป)" : ""), flowLimit ? 2 : 1);

        bool floatBad = d.Limiter >= m.FloatRpm;
        Line($"วาล์วลอยที่ {m.FloatRpm:N0} rpm" + (floatBad ? "  รอบตัดสูงไป" : ""), floatBad ? 2 : 1);

        float vpL = m.PistonSpeed(d.Limiter);
        Line($"ลูกสูบเฉลี่ย {vpL:0.0} ม./วิ ที่รอบตัด" + (vpL > 24f ? "  (ชักยาวเกิน กำลังตก)" : ""), vpL > 24f ? 2 : vpL > 22f ? 0 : 1);

        float kn = m.Knock(3500f, m.Load(1f, 3500f));
        float margin = m.CrLimit - m.CrDyn;
        string fuel = EngineDesign.Fuels[Math.Clamp(d.Fuel, 0, EngineDesign.Fuels.Length - 1)].Name;
        Line(kn > 0.02f
                ? $"น็อก! CR {d.Cr:0.0} ({m.Psi:0} psi) เกินพิกัด {fuel} ({m.CrLimit:0.0})"
                : $"CR {d.Cr:0.0}  •  {m.Psi:0} psi  •  พิกัด {fuel} {m.CrLimit:0.0} (เหลือ {margin:0.0})",
            kn > 0.02f || margin < 0.5f ? 2 : 1);
        if (d.Cr < 8.8f) Line("กำลังอัดต่ำ: เดินเบาจุดไม่ติดเป็นพัก ๆ", 2);

        if (m.Hunt > 0.25f) Line($"แคมองศามาก: เดินเบาหอบ (รอบเหวี่ยงขึ้น-ลง ~{m.Hunt * 100f:0}%)", 2);
        string idleTxt = m.Lope < 0.12f ? "เดินเบานิ่ง" : m.Lope < 0.5f ? "เดินเบาสะดุดเบา ๆ" : "เดินเบาลูปจัด";
        string tone = m.SoundBright < -0.35f ? "นุ่ม" : m.SoundBright > 0.55f ? "แผดคม" : "กลาง";
        string size = m.PitchStretch < 0.92f ? "  •  เสียงเล็กแหลม" : m.PitchStretch > 1.08f ? "  •  เสียงใหญ่ทุ้ม" : "";
        Line($"เสียง: {idleTxt}  •  โทน{tone}{size}", 0);

        if (d.Auto == 1)
        {
            float hold = Cvt.HoldRpm(m);
            bool ok = hold >= m.PeakPsRpm * 0.93f && hold <= m.PeakPsRpm * 1.08f;
            string v = hold < m.PeakPsRpm * 0.93f ? "ถือรอบต่ำกว่าพีค" : hold > m.PeakPsRpm * 1.08f ? "ถือรอบเกินพีค" : "ใกล้รอบพีค (ดี)";
            var top = Vehicle.TopSpeedAuto(m);
            Line($"CVT ถือ ~{hold:N0} rpm: {v}  •  ท็อป {top.Kmh:0} กม./ชม.", ok ? 1 : 2);
        }
        else
        {
            var spl = new List<string>();
            for (int gi = 1; gi <= d.Gears; gi++) spl.Add($"{d.Limiter * 2f * MathF.PI / 60f * Vehicle.WheelRadius(d) / Vehicle.Total(d, gi) * 3.6f:0}");
            Line("ที่รอบตัด (กม./ชม.) เกียร์ 1-" + d.Gears + ": " + string.Join(" / ", spl), 0);
            var top = Vehicle.TopSpeed(m, d.Gears);
            string v = top.Rpm >= d.Limiter - 150f ? "  รอบตัดมาก่อน ปลายสั้น" : top.Rpm < m.PeakPsRpm * 0.88f ? "  ปลายยาว ลากไม่ถึงพีค" : "";
            Line($"ท็อปเกียร์ {d.Gears}: {top.Kmh:0} กม./ชม. @ {top.Rpm:N0} rpm{v}", v.Length > 0 ? 2 : 1);
        }

        // ปุ่ม
        bool same = _save.Design.Serialize() == d.Serialize();
        if (d.Auto == 1)
        {
            Btn(g, new RectangleF(X, 984, 230, 70), "A  จูนเม็ด", false, Palette.Blue, AutoTuneRollers, _fMd);
            Btn(g, new RectangleF(X + 246, 984, W - 246, 70), same ? "Enter  ไปทดสอบ (ติดตั้งอยู่)" : "Enter  ประกอบแล้วไปทดสอบ", !same, Palette.Green, Assemble, _fMd);
        }
        else
            Btn(g, new RectangleF(X, 984, W, 70), same ? "Enter  ไปทดสอบ (แบบนี้ติดตั้งอยู่)" : "Enter  ประกอบแล้วไปทดสอบ", !same, Palette.Green, Assemble, _fMd);
    }
}
