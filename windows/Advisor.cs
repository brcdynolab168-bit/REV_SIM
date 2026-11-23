namespace RevGame;

/// <summary>ข้อสังเกตจากผลทดสอบ (level: 0 ข้อมูล, 1 ดี, 2 ควรแก้)</summary>
public sealed record Tip(int Level, string Text);

/// <summary>ผลประมาณการสมรรถนะของแบบเครื่อง (จำลองวิ่งตรงเปิดคันเร่งสุด ไม่รวมความผิดพลาดของคนขับ)</summary>
public readonly record struct Perf(float T060, float T100, float T402, float Trap, float TopKmh, float TopRpm);

/// <summary>ข้อเสนอแนะการจูน: เปลี่ยนค่าไหน จากเท่าไรเป็นเท่าไร แล้วผลประมาณการเป็นอย่างไร</summary>
public sealed class Suggestion
{
    public string Label = "", Note = "";
    public EngineDesign Design = null!;
    public Perf Before, After;
    public float Gain;
}

/// <summary>โจทย์ของผู้เล่น: รอบที่อยากใช้ (ตั้งรอบตัดตามนี้), อะไหล่/ค่าที่ล็อกไว้ไม่ให้ที่ปรึกษาแตะ, ใช้ถนน (เดินเบาต้องนิ่ง)</summary>
public sealed class AdvConstraints
{
    public float RpmCap;                    // 0 = ไม่จำกัด
    public int LockMask;                    // บิตของ Advisor.Groups ที่ล็อก
    public bool Street;                     // ห้ามแนะนำที่ทำให้เดินเบาสะดุด/หอบ
    public bool Locked(int group) => (LockMask & (1 << group)) != 0;
}

/// <summary>ข้อมูลผลทดสอบจริงที่ผู้เล่นวัดมา (ใช้วิเคราะห์ร่วมกับแบบจำลอง)</summary>
public sealed class TestData
{
    public float PeakRpm, TopKmh, Last060 = -1f, Last100 = -1f, Last402 = -1f, Trap402;
    public DynoResult? Dyno;
    public RunStats? Before;
    public RunStats Cur = new();
}

/// <summary>
/// ผู้ช่วยจูน: (1) อ่านผลทดสอบจริงแล้วบอกอาการ (2) ลองปรับทีละค่าในแบบจำลอง วิ่งจำลอง 0-60/100 ม./402 ม./ท็อปสปีด
/// แล้วจัดอันดับว่าปรับอะไรได้ผลที่สุดตามเป้าหมาย โดยตัดตัวเลือกที่ทำให้น็อก/วาล์วลอย/เดินเบาแย่ลงออก
/// </summary>
public static class Advisor
{
    /// <summary>กลุ่มที่ล็อกได้ในโจทย์ (ชื่อ, คีย์พารามิเตอร์)</summary>
    public static readonly (string Name, string[] Keys)[] Groups =
    {
        ("สเตอร์/เกียร์", new[] { "sprr", "sprf", "g1", "gtop" }),
        ("เฟืองท้าย/CVT", new[] { "fdr", "fdf", "ramp", "rampout", "rollerg", "spring" }),
        ("เรือนลิ้น", new[] { "throttle" }),
        ("แคม", new[] { "lsa", "duration", "durationex", "lift", "liftex" }),
        ("ท่อ", new[] { "pipe" }),
        ("CR", new[] { "cr" }),
        ("ข้อเหวี่ยง/จานไฟ", new[] { "crank", "fly" }),
        ("ล้อ/ยาง", new[] { "tireasp", "rim" }),
        ("รอบตัด", new[] { "limiter" }),
    };

    public const int GoalQuarter = 0, GoalTop = 1, GoalLaunch = 2;
    public static readonly string[] GoalNames = { "402 ม. (เร่งไว)", "ท็อปสปีด", "0–60 (ออกตัว)" };

    // ---------- วิ่งจำลอง ----------
    public static Perf Predict(EngineModel m)
    {
        var d = m.D;
        bool auto = d.Auto == 1;
        float r = Vehicle.WheelRadius(d), mEff = Vehicle.EffMass(d), fmax = Vehicle.TractionMax(d);
        float lim = d.Limiter - 100f;
        float hold = auto ? Cvt.HoldRpm(m) : 0f;
        float launch = auto ? MathF.Max(3500f, hold * 0.75f) : Math.Clamp(m.PeakTqRpm * 1.1f, 3000f, lim);
        float shiftRpm = MathF.Min(lim, m.PeakPsRpm * 1.07f);
        float fin = auto ? Cvt.Final(d) : 0f;

        int gear = 1;
        float v = 0f, x = 0f, t = 0f, shift = 0f, t060 = -1f, t100 = -1f, t402 = -1f, trap = 0f;
        const float dt = 0.01f;
        while (t < 40f && t402 < 0f)
        {
            float rpm, ratio, wheelRpmFactor;
            if (auto)
            {
                float k = v / r * 9.5493f * fin;
                rpm = Math.Clamp(hold, k * Cvt.RHigh, k * Cvt.RLow);
                rpm = MathF.Min(MathF.Max(rpm, launch), lim);
                ratio = Math.Clamp(rpm / 9.5493f * r / MathF.Max(v, 0.1f), Cvt.RHigh * fin, Cvt.RLow * fin);
                wheelRpmFactor = 0f;
            }
            else
            {
                ratio = Vehicle.Total(d, gear);
                float wr = v / r * ratio * 9.5493f;
                rpm = MathF.Max(wr, gear == 1 ? launch : 2500f);
                if (rpm >= shiftRpm && gear < 4 && shift <= 0f) { gear++; shift = 0.2f; }
                wheelRpmFactor = wr;
                rpm = MathF.Min(rpm, lim);
            }
            float tq = MathF.Max(0f, m.BrakeTorqueWot(rpm));
            float f = shift > 0f ? 0f : MathF.Min(tq * ratio * Vehicle.DriveEff / r, fmax);
            if (!auto && wheelRpmFactor > lim) f = 0f;
            float net = f - Vehicle.Resist(d, v, 0f);
            v = MathF.Max(0f, v + net / mEff * dt);
            x += v * dt;
            t += dt;
            shift -= dt;
            float kmh = v * 3.6f;
            if (t060 < 0f && kmh >= 60f) t060 = t;
            if (t100 < 0f && x >= 100f) t100 = t;
            if (x >= 402.3f) { t402 = t; trap = kmh; }
        }
        var top = auto ? Vehicle.TopSpeedAuto(m) : Vehicle.TopSpeed(m, m.D.Gears);
        return new Perf(t060 < 0f ? 99f : t060, t100 < 0f ? 99f : t100, t402 < 0f ? 99f : t402, trap, top.Kmh, top.Rpm);
    }

    private static bool Knocks(EngineModel m) => m.KnockWot(3500f) > 0.02f;

    // ---------- ตัวเลือกที่ลองปรับ ----------
    private static readonly (string Key, float[] Deltas, bool Auto, bool Manual)[] Tweaks =
    {
        ("sprr", new[] { -2f, -1f, 1f, 2f }, false, true),
        ("sprf", new[] { -1f, 1f }, false, true),
        ("gtop", new[] { -0.06f, 0.06f }, false, true),
        ("g1", new[] { -0.2f, 0.2f }, false, true),
        ("fdr", new[] { -2f, -1f, 1f, 2f }, true, false),
        ("fdf", new[] { -1f, 1f }, true, false),
        ("ramp", new[] { -1f, -0.5f, 0.5f, 1f }, true, false),
        ("rampout", new[] { -2f, -1f, 1f, 2f }, true, false),
        ("rollerg", new[] { -2f, -1f, 1f, 2f }, true, false),
        ("spring", new[] { -4f, -2f, 2f, 4f }, true, false),
        ("throttle", new[] { 2f, 4f, -2f }, true, true),
        ("lsa", new[] { -4f, -2f, 2f, 4f }, true, true),
        ("duration", new[] { -10f, -6f, 6f, 10f }, true, true),
        ("durationex", new[] { -10f, -6f, 6f, 10f }, true, true),
        ("lift", new[] { -0.5f, 0.5f, 1f }, true, true),
        ("liftex", new[] { -0.5f, 0.5f, 1f }, true, true),
        ("cr", new[] { -0.5f, 0.5f, 1f }, true, true),
        ("pipe", new[] { 1f, 2f, 3f, -1f }, true, true),
        ("limiter", new[] { -500f, 500f, 1000f }, true, true),
        ("crank", new[] { -0.4f, -0.8f, 0.4f }, true, true),
        ("fly", new[] { -0.2f, -0.4f, 0.2f }, true, true),
        ("tireasp", new[] { -5f, 5f, 10f }, true, true),
        ("rim", new[] { -1f, 1f }, true, true),
    };

    private static float Metric(Perf p, int goal) => goal switch
    {
        GoalTop => -p.TopKmh,      // ยิ่งน้อย (ติดลบมาก) ยิ่งดี
        GoalLaunch => p.T060,
        _ => p.T402,
    };

    /// <summary>ลองปรับทีละค่าจากแบบ d แล้วคืนรายการที่ดีขึ้นตามเป้าหมาย เรียงจากดีมากไปน้อย</summary>
    public static List<Suggestion> Suggest(EngineDesign d0, int goal, int max = 7, AdvConstraints? c = null)
    {
        c ??= new AdvConstraints();
        var d = d0.Clone();
        var best = new List<Suggestion>();
        bool limiterLocked = c.Locked(Array.FindIndex(Groups, g => g.Name == "รอบตัด"));
        if (c.RpmCap > 0f && !limiterLocked)
        {
            var lim = Array.Find(EngineDesign.Params, p => p.Key == "limiter")!;
            float cap = Math.Clamp(MathF.Round(c.RpmCap / lim.Step) * lim.Step, lim.Min, lim.Max);
            if (MathF.Abs(d.Limiter - cap) > 0.5f)
            {
                lim.Set(d, cap);
                var pa = Predict(new EngineModel(d0));
                var pb = Predict(new EngineModel(d));
                best.Add(new Suggestion
                {
                    Label = $"ตั้งรอบตัดตามเป้าหมาย:  {lim.Display(d0)}  »  {lim.Display(d)}",
                    Design = d.Clone(), Before = pa, After = pb, Gain = 999f, Note = "ตั้งตามโจทย์ (ใช้เป็นฐานของข้อแนะนำด้านล่าง)",
                });
            }
        }
        var m0 = new EngineModel(d);
        var p0 = Predict(m0);
        bool knock0 = Knocks(m0), float0 = d.Limiter >= m0.FloatRpm;
        bool auto = d.Auto == 1;

        foreach (var (key, deltas, forAuto, forManual) in Tweaks)
        {
            if (auto ? !forAuto : !forManual) continue;
            int gi = Array.FindIndex(Groups, g => Array.IndexOf(g.Keys, key) >= 0);
            if (gi >= 0 && c.Locked(gi)) continue;                                      // ล็อกไว้ตามโจทย์
            if (key == "limiter" && c.RpmCap > 0f) continue;                            // รอบตัดตามโจทย์แล้ว
            var def = Array.Find(EngineDesign.Params, p => p.Key == key);
            if (def == null) continue;
            Suggestion? top = null;
            foreach (float delta in deltas)
            {
                var d1 = d.Clone();
                float cur = def.Get(d);
                float nv = cur + delta;
                nv = MathF.Round(nv / def.Step) * def.Step;
                nv = Math.Clamp(nv, def.Min, def.Max);
                if (MathF.Abs(nv - cur) < def.Step * 0.5f) continue;
                def.Set(d1, nv);
                if (key == "rollerg" && MathF.Abs(d.RollerG - d.RollerG2) < 0.01f) d1.RollerG2 = d1.RollerG;

                var m1 = new EngineModel(d1);
                if (!knock0 && Knocks(m1)) continue;                              // ทำให้น็อก
                if (!float0 && d1.Limiter >= m1.FloatRpm) continue;               // รอบตัดเกินวาล์วลอย
                if (c.Street && ((m1.Overlap > 40f && m0.Overlap <= 40f) || (m1.Hunt > 0.25f && m0.Hunt <= 0.25f))) continue;   // โจทย์ถนน: เดินเบาต้องนิ่ง
                var p1 = Predict(m1);
                float gain = Metric(p0, goal) - Metric(p1, goal);                 // + = ดีขึ้น
                float minGain = goal == GoalTop ? 0.3f : 0.01f;
                if (gain < minGain) continue;
                if (top != null && gain <= top.Gain) continue;

                var notes = new List<string>();
                if (m1.Overlap > 40f && m0.Overlap <= 40f) notes.Add("เดินเบาสะดุดขึ้น");
                if (m1.Hunt > 0.25f && m0.Hunt <= 0.25f) notes.Add("เดินหอบ");
                if (m1.Psi > m0.Psi + 15f) notes.Add($"แรงดันอัดเพิ่มเป็น {m1.Psi:0} psi");
                if (m1.PeakPs < m0.PeakPs * 0.97f) notes.Add($"แรงม้าสูงสุดลด {m0.PeakPs * EngineModel.PsToHp:0.0}»{m1.PeakPs * EngineModel.PsToHp:0.0} HP");
                if (m1.PeakPs > m0.PeakPs * 1.02f) notes.Add($"แรงม้าสูงสุด {m0.PeakPs * EngineModel.PsToHp:0.0}»{m1.PeakPs * EngineModel.PsToHp:0.0} HP");
                top = new Suggestion
                {
                    Label = $"{def.Name}:  {def.Display(d)}  »  {def.Display(d1)}",
                    Design = d1, Before = p0, After = p1, Gain = gain,
                    Note = string.Join("  •  ", notes),
                };
            }
            if (top != null) best.Add(top);
        }
        best.Sort((a, b) => b.Gain.CompareTo(a.Gain));      // ข้อตั้งรอบตัดตามโจทย์ (Gain 999) อยู่บนสุดเสมอ
        if (best.Count > max) best.RemoveRange(max, best.Count - max);
        return best;
    }

    /// <summary>วิเคราะห์เครื่องเมื่อใช้รอบตามโจทย์ (ที่รอบตัด cap) ว่าพีค/ท็อปสปีดอยู่ตรงไหน</summary>
    public static List<Tip> CapTips(EngineDesign d, AdvConstraints c)
    {
        var tips = new List<Tip>();
        if (c.RpmCap <= 0f) return tips;
        var m = new EngineModel(d);
        var p = Predict(m);
        float cap = d.Limiter;
        float ps = EngineModel.PsToHp;
        if (m.PeakPsRpm > cap * 1.02f)
            tips.Add(new Tip(2, $"โจทย์รอบ {cap:N0}: เครื่องนี้พีคกำลังที่ {m.PeakPsRpm:N0} สูงกว่ารอบที่ใช้ ใช้กำลังไม่เต็ม  » ลดองศา/ยกแคม หรือใช้ท่อเดิม/ผ่าหมก ให้พีคลงมาใกล้ {cap:N0}"));
        else if (m.PeakPsRpm < cap - 1500f)
            tips.Add(new Tip(2, $"โจทย์รอบ {cap:N0}: กำลังพีคที่ {m.PeakPsRpm:N0} ต่ำกว่ารอบที่ใช้มาก ลากเกินพีคเปลืองเปล่า  » เพิ่มองศา/ยกแคม เรือนใหญ่ขึ้น หรือท่อสูตรให้พีคขยับขึ้นไปหา {cap:N0}"));
        else tips.Add(new Tip(1, $"โจทย์รอบ {cap:N0}: พีคกำลัง {m.PeakHp:0.0} HP @ {m.PeakPsRpm:N0} สัมพันธ์กับรอบที่ใช้ดี"));
        if (p.TopRpm >= cap - 150f) tips.Add(new Tip(2, $"ท็อปสปีด {p.TopKmh:0} กม./ชม. ชนรอบ {cap:N0} ก่อนความเร็วจบ  » ทดท้ายให้ยาวขึ้นเล็กน้อยจะได้ท็อปมากกว่านี้"));
        else tips.Add(new Tip(0, $"ท็อปสปีดประมาณ {p.TopKmh:0} กม./ชม. @ {p.TopRpm:N0} rpm (ใช้รอบไม่ถึง {cap:N0})"));
        return tips;
    }

    // ---------- วิเคราะห์ผลทดสอบจริง ----------
    public static List<Tip> Observe(EngineModel m, TestData t)
    {
        var d = m.D;
        var tips = new List<Tip>();
        var pred = Predict(m);
        float lim = d.Limiter;
        float ps = EngineModel.PsToHp;

        bool any = t.PeakRpm > 0f || t.TopKmh > 0f || t.Last402 > 0f || t.Dyno != null;
        if (!any) tips.Add(new Tip(0, "ยังไม่มีผลทดสอบ: ลองวิ่ง 0–60, 100 ม., 402 ม. และวัดไดโน (D) ที่ห้องทดสอบ แล้วกลับมาดูคำแนะนำจากผลจริง"));

        // เทียบผลจริงกับที่แบบจำลองคาด
        if (t.Last402 > 0f)
        {
            float diff = t.Last402 - pred.T402;
            if (diff > pred.T402 * 0.06f)
                tips.Add(new Tip(2, $"402 ม. วัดได้ {t.Last402:0.00} วิ ช้ากว่าที่เครื่องนี้ทำได้ ~{pred.T402:0.00} วิ  » ออกตัว/เปลี่ยนเกียร์ยังไม่ดี (ปล่อยคลัตช์ที่รอบแรงบิดสูงสุด ~{m.PeakTqRpm:N0}, ไล่เกียร์ที่ ~{m.PeakPsRpm * 1.05f:N0})"));
            else tips.Add(new Tip(1, $"402 ม. วัดได้ {t.Last402:0.00} วิ ใกล้ค่าที่เครื่องนี้ทำได้ ~{pred.T402:0.00} วิ แล้ว ต่อไปต้องแก้ที่เครื่อง/อัตราทด"));
        }
        if (t.TopKmh > 0f)
        {
            float dv = t.TopKmh - pred.TopKmh;
            if (dv < -pred.TopKmh * 0.05f)
                tips.Add(new Tip(2, $"ท็อปสปีดที่ทำได้ {t.TopKmh:0} กม./ชม. ต่ำกว่าศักยภาพ ~{pred.TopKmh:0} (@ {pred.TopRpm:N0} rpm)  » ลองลากให้นานขึ้น/ทางราบ หรือรอบถึงรอบตัดก่อน"));
        }

        // รอบ
        if (t.PeakRpm >= lim - 100f)
            tips.Add(new Tip(2, $"รอบชนรอบตัด ({t.PeakRpm:N0}) : ปลายสั้น/ล็อกรอบก่อนความเร็วสุด  » ทดท้ายให้ยาวขึ้น (สเตอร์หลังลด / เฟืองท้ายยาวขึ้น) หรือเพิ่มรอบตัดถ้าวาล์วไม่ลอย ({m.FloatRpm:N0})"));
        else if (t.PeakRpm > 0f && t.TopKmh > 0f && t.PeakRpm < m.PeakPsRpm * 0.9f)
            tips.Add(new Tip(2, $"รอบสูงสุดที่ใช้ {t.PeakRpm:N0} ยังไม่ถึงพีคกำลัง ({m.PeakPsRpm:N0})  » ทดจัดขึ้นเพื่อให้ได้รอบพีคก่อนจบ (สเตอร์หลังเพิ่ม / เฟืองท้ายทดจัด)"));

        // ไดโน
        if (t.Dyno != null && t.Dyno.Completed)
        {
            var dy = t.Dyno;
            if (dy.PeakPsRpm >= lim - 400f)
                tips.Add(new Tip(2, $"ไดโน: กำลังยังขึ้นไปจนถึงรอบตัด ({dy.PeakPs * ps:0.0} HP @ {dy.PeakPsRpm:N0})  » รอบตัดต่ำไปสำหรับเครื่องนี้ เพิ่มรอบตัดหรือใช้ทดท้ายยาวขึ้น"));
            else if (dy.PeakPsRpm < lim - 2500f)
                tips.Add(new Tip(2, $"ไดโน: กำลังตกหลัง {dy.PeakPsRpm:N0} ทั้งที่รอบตัด {lim:N0}  » ใช้งานเกินพีคเปลืองเปล่า ลดรอบตัด/จูนปลาย (เรือน, แคม EX, ยก, ท่อ) ให้ลากได้ไกลกว่านี้"));
            else tips.Add(new Tip(1, $"ไดโน: พีค {dy.PeakPs * ps:0.0} HP @ {dy.PeakPsRpm:N0} สัมพันธ์กับรอบตัด {lim:N0} พอดี"));
            if (dy.PeakTqRpm > dy.PeakPsRpm * 0.85f)
                tips.Add(new Tip(0, $"แรงบิดพีค {dy.PeakTq:0.0} N·m @ {dy.PeakTqRpm:N0} ใกล้พีคกำลัง: ช่วงกำลังแคบ ต้องไล่เกียร์/CVT ให้อยู่รอบสูงตลอด"));
        }

        // ช่วงห่างเกียร์ (เกียร์ธรรมดา)
        if (d.Auto == 0)
        {
            float sr = MathF.Min(lim - 100f, m.PeakPsRpm * 1.07f);
            float[] g = Enumerable.Range(1, d.Gears).Select(d.GearAt).ToArray();
            for (int i = 0; i < d.Gears - 1; i++)
            {
                float after = sr * g[i + 1] / g[i];
                if (after < m.PeakTqRpm * 0.85f)
                    tips.Add(new Tip(2, $"เปลี่ยนเกียร์ {i + 1}»{i + 2} รอบตกเหลือ ~{after:N0} ต่ำกว่าพีคแรงบิด ({m.PeakTqRpm:N0}) ห่างเกียร์ไป  » ปรับเกียร์ {i + 2} ให้ทดจัดขึ้น หรือเปลี่ยนรอบเปลี่ยนเกียร์ให้สูงขึ้น"));
            }
        }
        else
        {
            float hold = Cvt.HoldRpm(m);
            if (hold < m.PeakPsRpm * 0.93f) tips.Add(new Tip(2, $"CVT ถือรอบ {hold:N0} ต่ำกว่าพีคกำลัง ({m.PeakPsRpm:N0})  » เม็ดเบาลง/สปริงแข็งขึ้น/องศาชามมากขึ้น (กด A ในอู่ให้จูนเม็ดอัตโนมัติ)"));
            else if (hold > m.PeakPsRpm * 1.08f) tips.Add(new Tip(2, $"CVT ถือรอบ {hold:N0} เกินพีคกำลัง ({m.PeakPsRpm:N0})  » เม็ดหนักขึ้น/สปริงนิ่มลง"));
            else tips.Add(new Tip(1, $"CVT ถือรอบ {hold:N0} ใกล้พีคกำลัง ({m.PeakPsRpm:N0}) ดีแล้ว"));
        }

        // ออกตัว: แรงเกาะ
        float launchRpm = Math.Clamp(m.PeakTqRpm * 1.1f, 3000f, lim);
        float ratio1 = d.Auto == 1 ? Cvt.RLow * Cvt.Final(d) : Vehicle.Total(d, 1);
        float fLaunch = MathF.Max(0f, m.BrakeTorqueWot(launchRpm)) * ratio1 * Vehicle.DriveEff / Vehicle.WheelRadius(d);
        if (fLaunch > Vehicle.TractionMax(d) * 1.05f)
            tips.Add(new Tip(2, $"ออกตัวล้อฟรี: แรงขับ {fLaunch:0} N เกินที่ยางเกาะได้ {Vehicle.TractionMax(d):0} N  » หน้ายางกว้างขึ้น/ขอบให้พอดี หรือทดเกียร์ 1 ให้ยาวขึ้น"));

        // สุขภาพเครื่อง
        if (Knocks(m)) tips.Add(new Tip(2, $"น็อก! แรงดันอัด {m.Psi:0} psi เกินพิกัดน้ำมัน ({m.CrLimit:0.0}) ลด CR/แรงดันอัด หรือเปลี่ยนน้ำมันออกเทนสูง"));
        if (lim >= m.FloatRpm) tips.Add(new Tip(2, $"รอบตัด {lim:N0} สูงเกินรอบวาล์วลอย ({m.FloatRpm:N0}) ลดรอบตัด/ลดยก/เพิ่ม 4 วาล์ว"));
        if (m.RpmFlow < m.PeakPsRpm) tips.Add(new Tip(2, $"ไอดีเริ่มอั้นที่ {m.RpmFlow:N0} ต่ำกว่ารอบพีค: เพิ่มขนาดเรือน/ระยะยก/จำนวนวาล์ว"));

        // เทียบก่อน/หลังแก้
        if (t.Before != null && t.Cur != null)
        {
            var b = t.Before; var c = t.Cur;
            var parts = new List<string>();
            if (b.B402 > 0f && c.B402 > 0f) parts.Add($"402 ม. {b.B402:0.00}»{c.B402:0.00} วิ ({(b.B402 - c.B402 >= 0 ? "+" : "")}{b.B402 - c.B402:0.00})");
            if (b.B060 > 0f && c.B060 > 0f) parts.Add($"0–60 {b.B060:0.00}»{c.B060:0.00} วิ");
            if (b.Top > 0f && c.Top > 0f) parts.Add($"ท็อป {b.Top:0}»{c.Top:0} กม./ชม.");
            if (b.Hp > 0f && c.Hp > 0f) parts.Add($"{b.Hp:0.0}»{c.Hp:0.0} HP");
            if (parts.Count > 0) tips.Add(new Tip(0, "ผลเทียบก่อน»หลังแก้: " + string.Join("  •  ", parts)));
        }
        return tips;
    }
}
