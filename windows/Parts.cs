namespace RevGame;

/// <summary>อะไหล่ที่เลือกใส่ได้ในอู่ (หนึ่งชิ้น = ชุดค่าที่เปลี่ยนในแบบเครื่อง)</summary>
public sealed class PartItem
{
    public readonly string Name;
    public readonly Action<EngineDesign> Apply;
    public readonly Func<EngineDesign, bool> Is;
    public PartItem(string name, Action<EngineDesign> apply, Func<EngineDesign, bool> isFit) { Name = name; Apply = apply; Is = isFit; }
}

public sealed class PartCategory
{
    public readonly string Name, Hint;
    public readonly PartItem[] Items;
    public PartCategory(string name, string hint, PartItem[] items) { Name = name; Hint = hint; Items = items; }

    /// <summary>ชิ้นที่ติดตั้งอยู่ตอนนี้ (-1 = ปรับเอง ไม่ตรงชุดไหน)</summary>
    public int Current(EngineDesign d)
    {
        for (int i = 0; i < Items.Length; i++) if (Items[i].Is(d)) return i;
        return -1;
    }
}

public static class Parts
{
    private static bool Near(float a, float b, float tol) => MathF.Abs(a - b) <= tol;

    public static readonly PartCategory[] Categories =
    {
        new("ลูกสูบ + กระบอกสูบ", "ขนาดลูก (มม.) ยิ่งโตยิ่งซีซีเยอะ แต่น็อกง่ายขึ้นและต้องใช้เรือน/แคมรองรับ",
            new[] { 47f, 50f, 52.4f, 54f, 57f, 59f, 62f, 65f, 67f, 70f, 75f, 80f, 90f, 100f, 110f, 120f }
                .Select(b => new PartItem($"ลูก {b:0.#} มม.", d => d.Bore = b, d => Near(d.Bore, b, 0.26f))).ToArray()),

        new("ชุดข้อเหวี่ยง + ก้านสูบ", "ชัก (มม.) / ก้าน / น้ำหนักข้อเหวี่ยง (ไม่รวมจานไฟ)  ชักยาว = แรงบิดเยอะ แต่ลากรอบสูงไม่ไหว",
            new (float S, float R, float K)[] { (49.5f, 80f, 2.2f), (55.6f, 89f, 2.6f), (57.9f, 90f, 2.8f), (63f, 100f, 3.4f),
                (68f, 108f, 4.0f), (75.8f, 118f, 4.7f), (80f, 124f, 5.6f), (90f, 140f, 8f), (100f, 155f, 11f) }
                .Select(c => new PartItem($"ชัก {c.S:0.#} / ก้าน {c.R:0} / {c.K:0.0} กก.",
                    d => { d.Stroke = c.S; d.Rod = c.R; d.CrankKg = c.K; },
                    d => Near(d.Stroke, c.S, 0.15f) && Near(d.Rod, c.R, 0.6f) && Near(d.CrankKg, c.K, 0.06f))).ToArray()),

        new("จานไฟ (โรเตอร์)", "จานไฟหนัก = ความเฉื่อยสูง รอบขึ้นช้า ออกตัวนิ่ง เดินเบาเรียบ  เบา = เร่งรอบไว แต่เดินเบาสะดุด",
            new[] { 0.3f, 0.45f, 0.6f, 0.8f, 1.0f, 1.3f, 1.6f, 2.0f, 2.5f }
                .Select(w => new PartItem($"จานไฟ {w:0.00} กก.", d => d.FlywheelKg = w, d => Near(d.FlywheelKg, w, 0.026f))).ToArray()),

        new("ฝาสูบ (วาล์ว)", "4 วาล์วหายใจดี วาล์วเบา ลากรอบสูงได้ แต่แรงเสียดทานมากกว่าเล็กน้อย",
            new[]
            {
                new PartItem("2 วาล์ว", d => d.Valves = 2, d => d.Valves == 2),
                new PartItem("4 วาล์ว", d => d.Valves = 4, d => d.Valves == 4),
            }),

        new("แคมชาฟท์", "มุมแคม (LSA) / องศาแคม / ยกวาล์ว  องศามาก = แรงย้ายไปรอบสูง เดินเบาสั่นสะดุด",
            new (string N, float L, float D, float H)[]
            {
                ("แคมเดิมโรงงาน", 114f, 225f, 7.5f), ("แคมแต่งเบา", 112f, 235f, 8.0f), ("แคมแต่งถนน", 108f, 245f, 8.5f),
                ("แคมแข่ง", 106f, 270f, 9.5f), ("แคมซิ่ง", 104f, 290f, 10.0f), ("แคมสายแข่งหนัก", 102f, 310f, 12.0f),
            }.Select(c => new PartItem($"{c.N}  {c.L:0}°/{c.D:0}°/{c.H:0.#} มม.",
                d => { d.Lsa = c.L; d.Duration = c.D; d.DurationEx = c.D; d.Lift = c.H; d.LiftEx = c.H; },
                d => Near(d.Lsa, c.L, 0.6f) && Near(d.Duration, c.D, 1.1f) && Near(d.DurationEx, c.D, 1.1f) && Near(d.Lift, c.H, 0.06f) && Near(d.LiftEx, c.H, 0.06f))).ToArray()),

        new("แคมไอเสีย (EX) แยก", "ปรับองศาไอเสียแยกจากไอดี (IN)  สั้นกว่าไอดี = ไอเสียอั้น  ยาวกว่า = ปลายไหลดี โอเวอร์แลปเพิ่ม",
            new[] { 215f, 225f, 235f, 245f, 255f, 270f, 290f, 310f }
                .Select(x => new PartItem($"ไอเสีย {x:0}°", d => d.DurationEx = x, d => Near(d.DurationEx, x, 1.1f))).ToArray()),

        new("ยกไอเสีย (EX) แยก", "ปรับระยะยกไอเสียแยกจากไอดี (IN)  น้อยกว่าไอดีมาก = ไอเสียอั้น  มากกว่า = ปลายไหลดี",
            new[] { 6f, 7f, 7.5f, 8f, 8.5f, 9f, 9.5f, 10f, 11f, 12f, 13f }
                .Select(x => new PartItem($"ยกไอเสีย {x:0.#} มม.", d => d.LiftEx = x, d => Near(d.LiftEx, x, 0.06f))).ToArray()),

        new("เรือนลิ้นเร่ง", "เรือนใหญ่ = ปลายไหล แต่ต้องมีวาล์ว/แคมรองรับ ไม่งั้นรอบล่างตก",
            new[] { 20f, 22f, 24f, 26f, 28f, 30f, 32f, 34f, 38f, 42f, 46f, 50f, 54f, 60f }
                .Select(t => new PartItem($"เรือน {t:0} มม.", d => d.Throttle = t, d => Near(d.Throttle, t, 0.5f))).ToArray()),

        new("อัตราส่วนกำลังอัด (CR)", "ค่าเรขาคณิต: สูง = ประสิทธิภาพดี แต่ต้องใช้น้ำมันออกเทนสูง  ไม่เกี่ยวกับแรงดัน psi ด้านล่าง",
            new[] { 8.5f, 9.3f, 10f, 10.5f, 11f, 11.5f, 12.5f, 13.5f, 14.5f, 16f }
                .Select(c => new PartItem($"CR {c:0.0}:1", d => d.Cr = c, d => Near(d.Cr, c, 0.05f))).ToArray()),

        new("แรงดันอัด (วัดเป็น psi)", "แยกจาก CR: ซีลแน่น = แรงบิดเพิ่มแต่น็อกง่าย  ต่ำ = รั่ว กำลังตก  (Wave ~190, แต่งถนน 200-230, แข่ง 260-300+)",
            new[] { 140f, 160f, 180f, 200f, 230f, 260f, 300f, 350f, 400f }
                .Select(p => new PartItem($"{p:0} psi",
                    d => d.Seal = Math.Clamp(p / EngineDesign.CrankPsi(d.CrDynamic), 0.6f, 1.5f),
                    d => Near(d.Psi, p, 7f))).ToArray()),

        new("ขนาดล้อ (ขอบ + กว้างขอบ + ยาง)", "ขอบล้อ (นิ้ว) / กว้างขอบ (นิ้ว) / ยาง หน้า/แก้ม  ขอบใหญ่/แก้มสูง = ความเร็วต่อรอบสูง  ยางกว้าง = เกาะดี ต้านการกลิ้งมาก",
            new (float R, float W, float T, float A)[]
            {
                (17f, 1.20f, 70f, 90f), (17f, 1.40f, 80f, 90f), (17f, 1.40f, 90f, 80f), (17f, 1.60f, 100f, 80f), (17f, 1.85f, 110f, 70f),
                (17f, 2.15f, 120f, 70f), (17f, 1.60f, 90f, 80f), (14f, 1.40f, 80f, 90f), (14f, 1.60f, 90f, 90f), (14f, 2.15f, 110f, 80f),
                (14f, 3.50f, 140f, 70f), (12f, 1.40f, 100f, 90f), (10f, 1.60f, 90f, 90f),
            }.Select(c => new PartItem($"{c.T:0}/{c.A:0}-{c.R:0}  ขอบ {c.W:0.00}",
                d => { d.RimInch = c.R; d.RimW = c.W; d.TireW = c.T; d.TireAsp = c.A; },
                d => Near(d.RimInch, c.R, 0.1f) && Near(d.RimW, c.W, 0.03f) && Near(d.TireW, c.T, 2f) && Near(d.TireAsp, c.A, 2f))).ToArray()),

        new("ชนิดน้ำมัน", "ออกเทนสูง = ทนกำลังอัดสูงก่อนน็อก  E85/เมทานอล/ไนโตร ฉีดหนาขึ้นได้กำลังเพิ่ม",
            EngineDesign.Fuels.Select((f, i) => new PartItem($"{f.Name}  RON {f.Ron:0}", d => d.Fuel = i, d => d.Fuel == i)).ToArray()),

        new("ชุดเกียร์ (4 / 5 / 6 สปีด)", "เลือกชุดเกียร์ทั้งชุด รวมจำนวนสปีด  ชิด = ไล่เกียร์ต่อเนื่อง  ห่าง = ออกตัวแรงแต่รอบตกมากตอนเปลี่ยน (ชุด 5-6 สปีดเป็นค่าประมาณตามรถประเภทเดียวกัน)",
            new (string N, float P, float[] G)[]
            {
                ("4 สปีด เดิม Wave 125i", 3.35f, new[] { 2.500f, 1.550f, 1.150f, 0.923f }), ("4 สปีด ชิด", 3.35f, new[] { 2.400f, 1.700f, 1.300f, 1.050f }),
                ("4 สปีด ออกตัวจัด", 3.35f, new[] { 2.900f, 1.800f, 1.300f, 1.000f }), ("4 สปีด ปลายยาว", 3.35f, new[] { 2.500f, 1.550f, 1.150f, 0.850f }),
                ("4 สปีด ดรัก (ชิดจัด)", 3.60f, new[] { 2.700f, 1.900f, 1.450f, 1.150f }),
                ("5 สปีด มาตรฐาน", 3.20f, new[] { 2.833f, 1.882f, 1.409f, 1.130f, 0.958f }), ("5 สปีด ชิด", 3.20f, new[] { 2.600f, 1.850f, 1.450f, 1.200f, 1.020f }),
                ("6 สปีด MX King/R15", 3.05f, new[] { 2.833f, 1.875f, 1.364f, 1.087f, 0.920f, 0.815f }), ("6 สปีด ชิด", 3.05f, new[] { 2.700f, 1.900f, 1.500f, 1.250f, 1.080f, 0.950f }),
                ("6 สปีด ออกตัวจัด", 3.05f, new[] { 3.100f, 2.000f, 1.450f, 1.150f, 0.950f, 0.820f }),
            }.Select(c => new PartItem($"{c.N}  " + string.Join("/", c.G.Select(x => x.ToString("0.00"))),
                d =>
                {
                    d.Primary = c.P; d.Gears = c.G.Length;
                    d.Gear1 = c.G[0]; d.Gear2 = c.G[1]; d.Gear3 = c.G[2]; d.Gear4 = c.G[3];
                    if (c.G.Length > 4) d.Gear5 = c.G[4];
                    if (c.G.Length > 5) d.Gear6 = c.G[5];
                },
                d => d.Gears == c.G.Length && Near(d.Primary, c.P, 0.006f) && Enumerable.Range(1, c.G.Length).All(i => Near(d.GearAt(i), c.G[i - 1], 0.006f)))).ToArray()),

        new("ชามเม็ด (องศาใน/นอก)", "ชามแต่งมี 2 องศา: ใบใน (ออกตัว) / ใบนอก (ปลายทาง)  ใบนอกน้อยกว่า = ปลายไล่ไว  ใบนอกมากกว่า = ปลายถือรอบสูง (ใช้กับรถออโต้)",
            new (float I, float O)[] { (13.5f, 13.5f), (13f, 12f), (14f, 12f), (12f, 14f), (15f, 11f), (12f, 12f), (14f, 14f), (11f, 13f), (16f, 12f) }
                .Select(c => new PartItem($"ชาม {c.I:0.#}° / {c.O:0.#}°",
                    d => { d.RampDeg = c.I; d.RampOutDeg = c.O; },
                    d => Near(d.RampDeg, c.I, 0.05f) && Near(d.RampOutDeg, c.O, 0.05f))).ToArray()),

        new("ท่อไอเสีย", "เดิม = เงียบ / ผ่าหมก = ปลายขึ้นเล็กน้อย / ผ่าเปิด = ปลายดี รอบต่ำตก เสียงดัง / ท่อสูตร = พีคแรงเฉพาะรอบ (จูนตามแคม)",
            EngineDesign.PipeNames.Select((n, i) => new PartItem(n, d => d.Pipe = i, d => d.Pipe == i)).ToArray()),

        new("กล่อง/รอบตัด", "ตั้งสูงเกินที่วาล์ว/ความเร็วลูกสูบรับไหว = กำลังตก",
            new[] { 8000f, 9000f, 9500f, 10500f, 11500f, 12800f, 14000f, 15000f }
                .Select(r => new PartItem($"รอบตัด {r:N0}", d => d.Limiter = r, d => Near(d.Limiter, r, 60f))).ToArray()),
        new("รอบเด้งกลับหลัง hard cut", "ตัดที่รอบตัดแล้วต้องตกลงมาเท่าไรจึงติดใหม่  น้อย = ตัดถี่ เสียงรัว  มาก = ตัดนาน รอบตกลึก",
            new[] { 30f, 80f, 150f, 200f, 300f, 500f, 800f, 1200f }
                .Select(g => new PartItem($"ตกลง {g:0} rpm", d => d.ResumeGap = g, d => Near(d.ResumeGap, g, 5f))).ToArray()),

        new("Soft cut (ห่างใต้ hard cut)", "หน่วงไฟก่อนถึงรอบตัดแรง เช่น hard 10500 / ห่าง 100 = เริ่มที่ 10400  ห่างมาก = ไล่ตัดนุ่ม เสียงรัวเป็นช่วง ๆ",
            new[] { 0f, 50f, 100f, 200f, 300f, 500f, 800f }
                .Select(g => new PartItem(g <= 0f ? "ไม่ใช้ (hard cut อย่างเดียว)" : $"ห่าง {g:0} rpm", d => d.SoftGap = g, d => Near(d.SoftGap, g, 1f))).ToArray()),
    };

    /// <summary>เช็ตเครื่องสำเร็จรูป: แบบเดิมทั้ง 6 + เช็ตใหญ่ (ใช้ค่าจินตนาการ ไม่ใช่สเปกรถจริง)</summary>
    public static readonly (string Name, EngineDesign D)[] Sets = EngineDesign.Presets.Concat(new (string, EngineDesign)[]
    {
        ("สตรีท 150 ลูก 58*", new EngineDesign
        {
            Bore = 58f, Stroke = 57.9f, Rod = 90f, CrankKg = 2.5f, FlywheelKg = 0.5f, Throttle = 28f,
            Lsa = 110f, Duration = 240f, DurationEx = 240f, Lift = 8.0f, LiftEx = 8.0f, Cr = 10.5f, Limiter = 10500f, Fuel = 2,
        }),
        ("เดี่ยว 300 ลูก 80*", new EngineDesign
        {
            Bore = 80f, Stroke = 60f, Rod = 100f, CrankKg = 4.8f, FlywheelKg = 0.7f, Valves = 4, Throttle = 38f,
            Lsa = 108f, Duration = 255f, DurationEx = 255f, Lift = 9.5f, LiftEx = 9.5f, Cr = 11.5f, Limiter = 11000f, Fuel = 3, BikeKg = 120f,
        }),
        ("เดี่ยว 600 ลูก 100*", new EngineDesign
        {
            Bore = 100f, Stroke = 76.5f, Rod = 125f, CrankKg = 7.2f, FlywheelKg = 1.3f, Valves = 4, Throttle = 46f,
            Lsa = 108f, Duration = 250f, DurationEx = 250f, Lift = 10.5f, LiftEx = 10.5f, Cr = 12.5f, Limiter = 10500f, Fuel = 3, BikeKg = 140f,
        }),
        ("ดรักเมทานอล 1000 ลูก 120*", new EngineDesign
        {
            Bore = 120f, Stroke = 88f, Rod = 150f, CrankKg = 10f, FlywheelKg = 2.0f, Valves = 4, Throttle = 60f,
            Lsa = 106f, Duration = 270f, DurationEx = 270f, Lift = 13f, LiftEx = 13f, Cr = 14.5f, Limiter = 9500f, Fuel = 5, BikeKg = 160f,
        }),
    }).ToArray();
}
