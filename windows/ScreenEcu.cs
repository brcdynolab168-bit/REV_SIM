namespace RevGame;

/// <summary>แท็บ "จูนกล่อง" ในอู่: ตาราง ECU 20x20 (น้ำมัน % / องศาจุดระเบิด / AFR) คอลัมน์ = รอบ, แถว = โหลด 5..100%</summary>
public sealed partial class MainForm
{
    private int _ecuMode;                              // 0 = น้ำมัน %, 1 = องศาจุด, 2 = AFR (เป้าหมาย)
    private int _ecuC0, _ecuR0 = 19, _ecuC1, _ecuR1 = 19;  // มุมเลือก (จุดเริ่ม, จุดล่าสุด)
    private const int EN = EngineModel.EcuN;
    private static readonly string[] EcuModeNames = { "น้ำมัน (ms)", "องศาจุด (°)", "AFR" };

    private bool IsEcuTab => GarageTabs[_tab].Name == "จูนกล่อง";

    private void EcuRange(out int c0, out int c1, out int r0, out int r1)
    {
        c0 = Math.Min(_ecuC0, _ecuC1); c1 = Math.Max(_ecuC0, _ecuC1);
        r0 = Math.Min(_ecuR0, _ecuR1); r1 = Math.Max(_ecuR0, _ecuR1);
    }

    private void EnsureFuel() => _draft.EcuMs ??= _preview.FactoryMsTable();
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
                if (_ecuMode == 0) _draft.EcuMs![i] = Math.Clamp(MathF.Round((_draft.EcuMs[i] + (coarse ? 0.5f : 0.1f) * dir) * 100f) / 100f, 0.3f, 40f);
                else
                {
                    // โหมด AFR: ตั้ง AFR เป้าหมายของช่อง แล้วแปลงเป็นเวลาฉีด (ms)
                    float rpmC = m.EcuRpmAt(c), loadR = EngineModel.EcuLoadAt(r);
                    float cur = m.EcuAt(rpmC, loadR).Afr;
                    _draft.EcuMs![i] = Math.Clamp(MathF.Round(m.MsForAfr(rpmC, loadR, cur + (coarse ? 0.5f : 0.1f) * dir) * 100f) / 100f, 0.3f, 40f);
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
        var t = ign ? _draft.EcuIgn! : _draft.EcuMs!;
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
        var t = ign ? _draft.EcuIgn! : _draft.EcuMs!;
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
            var facMs = _preview.FactoryMsTable();
            EnsureFuel();
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) _draft.EcuMs![r * EN + c] = facMs[r * EN + c];
        }
        DraftChanged();
    }

    private void EcuFactoryAll()
    {
        if (_ecuMode == 1) _draft.EcuIgn = null; else _draft.EcuMs = null;
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
            case Keys.OemCloseBrackets: case Keys.PageUp: EcuAdjust(1, shift || k == Keys.PageUp); return true;
            case Keys.OemOpenBrackets: case Keys.PageDown: EcuAdjust(-1, shift || k == Keys.PageDown); return true;
            case Keys.K: _eng.Ignition = !_eng.Ignition; return true;
            case Keys.Oem2: case Keys.F1: _showEcuHelp = true; return true;
            case Keys.D: _ecuMode = (_ecuMode + 1) % 3; return true;
        }
        return false;
    }

}
