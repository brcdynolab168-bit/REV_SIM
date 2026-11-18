namespace RevGame;

/// <summary>แท็บ "จูนกล่อง" ในอู่: ตาราง ECU 20x20 (น้ำมัน ms / องศาฉีด / องศาจุด / ไทม์คอยล์ / AFR / MAP) แกนนอน = โหลด 5..100% แกนตั้ง = รอบ (บนลงล่าง ต่ำไปสูง)</summary>
public sealed partial class MainForm
{
    private int _ecuMode;                              // 0 = น้ำมัน ms, 1 = องศาฉีด (EOI), 2 = องศาจุด, 3 = ไทม์คอยล์ (dwell), 4 = AFR (เป้าหมาย)
    private int _ecuC0, _ecuR0 = 19, _ecuC1, _ecuR1 = 19;  // มุมเลือก (จุดเริ่ม, จุดล่าสุด)
    private const int EN = EngineModel.EcuN;
    private static readonly string[] EcuModeNames = { "น้ำมัน ms", "องศาฉีด °", "องศาจุด °", "ไทม์คอยล์ ms", "AFR", "MAP kPa" };
    private static readonly int ModeCount = 6;

    private bool EcuReadOnly() => false;

    private bool IsEcuTab => GarageTabs[_tab].Name == "จูนกล่อง";

    private void EcuRange(out int c0, out int c1, out int r0, out int r1)
    {
        c0 = Math.Min(_ecuC0, _ecuC1); c1 = Math.Max(_ecuC0, _ecuC1);
        r0 = Math.Min(_ecuR0, _ecuR1); r1 = Math.Max(_ecuR0, _ecuR1);
    }

    /// <summary>ตารางที่แก้ได้ของโหมดนั้น (ยังไม่เคยแก้ = สร้างจากค่าโรงงานของเครื่องรุ่นนี้)  โหมด AFR แก้ที่ตารางน้ำมัน</summary>
    private float[] EcuTable(int mode) => mode switch
    {
        1 => _draft.EcuEoi ??= _preview.FactoryEoiTable(),
        2 => _draft.EcuIgn ??= _preview.FactoryIgnTable(),
        3 => _draft.EcuDwell ??= _preview.FactoryDwellTable(),
        5 => _draft.EcuMap ??= _preview.FactoryMapTable(),
        _ => _draft.EcuMs ??= _preview.FactoryMsTable(),
    };
    private float[] EcuFactory(int mode) => mode switch
    {
        1 => _preview.FactoryEoiTable(), 2 => _preview.FactoryIgnTable(), 3 => _preview.FactoryDwellTable(), 5 => _preview.FactoryMapTable(), _ => _preview.FactoryMsTable(),
    };
    /// <summary>(ขั้นละเอียด, ขั้นหยาบ, ต่ำสุด, สูงสุด, ปัดเศษ)</summary>
    private static (float Fine, float Coarse, float Lo, float Hi, float Round) EcuSpec(int mode) => mode switch
    {
        1 => (5f, 20f, 0f, 560f, 1f),
        2 => (0.5f, 2f, -5f, 50f, 0.1f),
        3 => (0.1f, 0.5f, 0.5f, 9f, 0.01f),
        5 => (1f, 5f, 0f, 115f, 1f),
        _ => (0.1f, 0.5f, 0f, 40f, 0.01f),
    };

    private static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>ปรับค่าที่เลือกในโหมดปัจจุบัน dir = ±1</summary>
    private void EcuAdjust(int dir, bool coarse)
    {
        if (EcuReadOnly()) return;
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        var m = _preview;
        if (_ecuMode == 4)
        {
            var t = EcuTable(0);
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++)
            {
                // โหมด AFR: ตั้ง AFR เป้าหมายของช่อง แล้วแปลงเป็นเวลาฉีด (ms)
                float rpmC = m.EcuRpmAt(c), tpsR = EngineModel.TpsAxis(r), loadR = m.LoadOf(tpsR, rpmC);
                float cur = m.EcuAt(rpmC, tpsR).Afr;
                t[r * EN + c] = Math.Clamp(MathF.Round(m.MsForAfr(rpmC, loadR, cur + (coarse ? 0.5f : 0.1f) * dir) * 100f) / 100f, 0f, 40f);
            }
        }
        else
        {
            var sp = EcuSpec(_ecuMode);
            var t = EcuTable(_ecuMode);
            float step = (coarse ? sp.Coarse : sp.Fine) * dir;
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++)
                t[r * EN + c] = Math.Clamp(MathF.Round((t[r * EN + c] + step) / sp.Round) * sp.Round, sp.Lo, sp.Hi);
        }
        DraftChanged();
    }

    private int EditMode => _ecuMode == 4 ? 0 : _ecuMode;

    private void EcuSmooth()
    {
        if (EcuReadOnly()) return;
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        var sp = EcuSpec(EditMode);
        var t = EcuTable(EditMode);
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
            t[r * EN + c] = MathF.Round(s / n / sp.Round) * sp.Round;
        }
        DraftChanged();
        Toast("เกลี่ยค่าที่เลือกแล้ว");
    }

    private void EcuEqualize()
    {
        if (EcuReadOnly()) return;
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        var t = EcuTable(EditMode);
        float v = t[_ecuR0 * EN + _ecuC0];
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) t[r * EN + c] = v;
        DraftChanged();
    }

    private void EcuFactorySel()
    {
        if (EcuReadOnly()) return;
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        var fac = EcuFactory(EditMode);
        var t = EcuTable(EditMode);
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) t[r * EN + c] = fac[r * EN + c];
        DraftChanged();
    }

    /// <summary>ตัดน้ำมันช่องที่เลือก (เวลาฉีด 0 ms = ไม่มีการเผาไหม้) ใช้ทำอาการเดินหอบ/ทดลองตัดช่วงรอบ</summary>
    private void EcuCutFuel()
    {
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        var t = EcuTable(0);
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) t[r * EN + c] = 0f;
        DraftChanged();
        Toast("ตัดน้ำมันช่องที่เลือกแล้ว (0 ms)  ตัดช่วงเดินเบาแล้วลองเปิดเครื่องจูนสด: จะเดินหอบ");
    }

    /// <summary>ทำอาการเดินหอบ: ตัดน้ำมันทุกโหลดในช่วงรอบเหนือรอบเดินเบาขึ้นไป (~0.95-1.55 เท่า) รอบจะตกลงใต้ช่วงที่ตัด น้ำมันกลับมา รอบขึ้นเข้าช่วงตัดอีก วนเป็นจังหวะ</summary>
    private void EcuHunt()
    {
        var m = _preview;
        var t = EcuTable(0);
        float lo = m.IdleRpm * 0.95f, hi = m.IdleRpm * 1.55f;
        for (int c = 0; c < EN; c++)
        {
            float rpm = m.EcuRpmAt(c);
            if (rpm < lo || rpm > hi) continue;
            for (int r = 0; r < EN; r++) t[r * EN + c] = 0f;
        }
        DraftChanged();
        Toast($"ตัดน้ำมันช่วงรอบ {lo:N0}-{hi:N0} ทุกโหลดแล้ว เปิดเครื่องจูนสดแล้วดูรอบเดินเบาเหวี่ยง (เดินหอบ)");
    }

    private void EcuFactoryAll()
    {
        if (EcuReadOnly()) return;
        switch (EditMode) { case 1: _draft.EcuEoi = null; break; case 2: _draft.EcuIgn = null; break; case 3: _draft.EcuDwell = null; break; case 5: _draft.EcuMap = null; break; default: _draft.EcuMs = null; break; }
        DraftChanged();
        Toast("คืนตารางเป็นค่าโรงงานแล้ว");
    }

    /// <summary>ตั้งองศาจุดของช่องที่เลือกให้ใกล้ MBT (ต่ำกว่า 1°) ตามหลักการจริง: ได้แรงบิดสูงสุดโดยยังไม่ล้ำเกินจนน็อก</summary>
    private void EcuNearMbt()
    {
        if (EcuReadOnly()) return;
        EcuRange(out int c0, out int c1, out int r0, out int r1);
        var t = EcuTable(2);
        var m = _preview;
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++)
        {
            var e = m.EcuAt(m.EcuRpmAt(c), EngineModel.TpsAxis(r));
            t[r * EN + c] = MathF.Round((e.Mbt - 1f) * 2f) / 2f;
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
                int dr = k == Keys.Left ? -1 : k == Keys.Right ? 1 : 0, dc = k == Keys.Up ? -1 : k == Keys.Down ? 1 : 0;
                _ecuC1 = Math.Clamp(_ecuC1 + dc, 0, EN - 1); _ecuR1 = Math.Clamp(_ecuR1 + dr, 0, EN - 1);
                if (!shift) { _ecuC0 = _ecuC1; _ecuR0 = _ecuR1; }
                return true;
            }
            case Keys.OemCloseBrackets: case Keys.PageUp: EcuAdjust(1, shift || k == Keys.PageUp); return true;
            case Keys.OemOpenBrackets: case Keys.PageDown: EcuAdjust(-1, shift || k == Keys.PageDown); return true;
            case Keys.K: _eng.Ignition = !_eng.Ignition; return true;
            case Keys.E: if (_benchApplied) Shift(+1); return true;
            case Keys.Q: if (_benchApplied) Shift(-1); return true;
            case Keys.N: if (_benchApplied) SelectGear(0); return true;
            case Keys.Oem2: case Keys.F1: _showEcuHelp = true; return true;
            case Keys.D: _ecuMode = (_ecuMode + 1) % ModeCount; return true;
        }
        return false;
    }

}
