using System.Globalization;

namespace RevGame;

/// <summary>สเปกเครื่องที่ผู้เล่นออกแบบ (แก้ได้ในอู่)</summary>
public sealed class EngineDesign
{
    public float Bore = 52.4f;      // ขนาดลูกสูบ mm
    public float Stroke = 57.9f;    // ระยะชัก mm
    public float Rod = 90f;          // ความยาวก้านสูบ (ศูนย์-ศูนย์) mm
    public float CrankKg = 2.8f;    // น้ำหนักชุดข้อเหวี่ยง (ไม่รวมจานไฟ) kg
    public float FlywheelKg = 0.6f; // น้ำหนักจานไฟ (โรเตอร์) kg: หนัก = รอบขึ้นช้า/นิ่ง เบา = รอบขึ้นไว
    public int Valves = 2;          // 2 หรือ 4 วาล์ว
    public float Throttle = 20f;    // ขนาดเรือนลิ้นเร่ง mm
    public float Lsa = 114f;        // มุมแคม (lobe separation angle) องศา
    public float Duration = 225f;   // องศาแคมไอดี IN (ช่วงวาล์วเปิด @1mm) องศาข้อเหวี่ยง
    public float DurationEx = 225f; // องศาแคมไอเสีย EX
    public float DurAvg => 0.5f * (Duration + DurationEx);
    public float Lift = 7.5f;       // ระยะยกวาล์วไอดี IN mm
    public float LiftEx = 7.5f;     // ระยะยกวาล์วไอเสีย EX mm
    public float Cr = 9.3f;         // อัตราส่วนกำลังอัด (เรขาคณิต) :1 -> ประสิทธิภาพเชิงความร้อน
    public float RealHp, RealHpRpm;     // แรงม้าที่วัดจากรถจริง (HP) และรอบ (0 = ไม่ระบุ) ใช้อ้างอิง/ปรับเทียบโมเดล
    public float RealTq, RealTqRpm;     // แรงบิดจากรถจริง (N·m) และรอบ
    public float CalK = 1.0f;           // ตัวคูณปรับเทียบแรงบิดของโมเดลให้ตรงรถจริง (1.00 = ไม่ปรับ)
    public float Seal = 1.0f;       // แรงดันอัดจริงเทียบค่าที่ CR+แคมควรได้ (ซีลดี = 1.0) -> แรงดัน psi แยกจาก CR
    public float Limiter = 9500f;   // รอบตัดแรง (hard cut) rpm: ตัดไฟเด็ดขาด
    public float SoftGap = 0f;      // soft cut อยู่ใต้ hard cut กี่ rpm (0 = ไม่ใช้) เช่น hard 10500 ห่าง 100 = soft เริ่มที่ 10400
    public float ResumeGap = 80f;   // hard cut แล้วรอบต้องตกลงใต้รอบตัดกี่ rpm จึงจุดระเบิดใหม่ (รอบเด้งกลับ) เช่น ตัด 10000 กลับที่ 9800 = 200
    public float ResumeRpm => Limiter - ResumeGap;
    public float SoftRpm => SoftGap > 0f ? Limiter - SoftGap : Limiter;
    public float SprocketF = 14f;   // สเตอร์หน้า (ฟัน)
    public float SprocketR = 36f;   // สเตอร์หลัง (ฟัน)
    public float RiderKg = 65f;     // น้ำหนักคนขี่ + ของ kg
    public int Auto = 0;            // 0 = เกียร์ธรรมดา (Wave), 1 = ออโต้ (สายพาน CVT)
    public float RampDeg = 13.5f;   // องศาชามเม็ด ใบใน (ช่วงต้นทาง ตอนออกตัว) ของชุดขับหน้า
    public float RampOutDeg = 13.5f; // องศาชามเม็ด ใบนอก (ช่วงปลายทาง ตอนไล่สายพานจนสุด) ชามแต่งมีองศา 2 ช่วง เช่น 13/11
    public int Rollers = 6;         // จำนวนเม็ด (4 หรือ 6)
    public float RollerG = 8.5f;    // น้ำหนักเม็ดหลัก (กรัม)
    public float RollerG2 = 8.5f;   // น้ำหนักเม็ดสลับ: เม็ดตำแหน่งที่ 2, 4, 6 (กรัม)
    public int GearF = 13;          // เฟืองท้าย ตัวหน้า (ฟัน) รถออโต้: คู่ที่ช่างเปลี่ยนจูนทดได้
    public int GearR = 40;          // เฟืองท้าย ตัวหลัง (ฟัน)
    public float FirstRed = 3.0f;   // ลดขั้นแรกในเรือนเกียร์ (ตายตัวตามรถ: XMAX 300 = 48/18) ทดรวมหลัง CVT = FirstRed x (หลัง/หน้า)
    public float BikeKg = 107f;     // น้ำหนักรถเปล่า (กก. รวมน้ำมัน)
    public float CdA = 0.415f;      // พื้นที่ลมต้าน Cd x A (ตร.ม.) Wave+คนขี่ ~0.415, สกู๊ตเตอร์บังลม (XMAX) ~0.32
    public float RimInch = 17f;     // ขนาดขอบล้อหลัง (นิ้ว)
    public float RimW = 1.40f;      // ความกว้างขอบล้อ (นิ้ว) เช่น 1.10 / 1.20 / 1.40 / 1.60
    public float TireW = 80f;       // หน้ายาง (มม.) เช่น 70/80/90
    public float TireAsp = 90f;     // แก้มยาง % (80/90-17 = หน้า 80 แก้ม 90%)
    /// <summary>รหัสยาง เช่น 80/90-17</summary>
    public string TireCode => $"{TireW:0}/{TireAsp:0}-{RimInch:0}";
    public float SpringK = 20f;     // ความแข็งสปริงทอคชุดขับหลัง (N/มม.)
    public float Primary = 3.35f;   // อัตราทดขั้นต้น (เฟืองข้อเหวี่ยง -> คลัตช์) รถเกียร์ธรรมดา
    public float Gear1 = 2.500f;    // อัตราทดเกียร์ 1-4 (Wave 125i: 2.500 / 1.550 / 1.150 / 0.923)
    public float Gear2 = 1.550f;
    public float Gear3 = 1.150f;
    public float Gear4 = 0.923f;
    public int Pipe = 0;            // ท่อไอเสีย: 0 เดิม, 1 ผ่าหมก, 2 ผ่าเปิด, 3 ท่อสูตร
    public static readonly string[] PipeNames = { "ท่อเดิม", "ท่อผ่าหมก", "ท่อผ่าเปิด", "ท่อสูตร" };
    public int Fuel = 1;            // ชนิดน้ำมัน (ดู Fuels)
    public float Octane => Fuels[Math.Clamp(Fuel, 0, Fuels.Length - 1)].Ron;
    public float FuelPower => Fuels[Math.Clamp(Fuel, 0, Fuels.Length - 1)].PowerK;

    public readonly record struct FuelDef(string Name, float Ron, float PowerK, string Note);
    public static readonly FuelDef[] Fuels =
    {
        new("เบนซิน 91", 91f, 0.99f, "ปั๊มธรรมดา ทนกำลังอัดต่ำ"),
        new("เบนซิน 95", 95f, 1.00f, "มาตรฐานรถทั่วไป"),
        new("แก๊สโซฮอล์ E20", 98f, 1.01f, "เอทานอล 20% ทนน็อกดีขึ้นเล็กน้อย"),
        new("น้ำมันแข่ง RON 100", 100f, 1.015f, "ทนกำลังอัดสูง ราคาแพง"),
        new("E85 (เอทานอล 85%)", 105f, 1.045f, "ออกเทนสูงมาก เย็นกว่า ฉีดหนาขึ้นได้กำลังเพิ่ม"),
        new("เมทานอล", 109f, 1.08f, "รถแข่ง/ดรัก กำลังอัด 14-16:1 ได้ ระบายความร้อนดี"),
        new("เมทานอล + ไนโตร 20%", 120f, 1.22f, "ดรักสุดโหด ทนกำลังอัดสูงสุด กำลังพุ่ง"),
    };

    /// <summary>ความดันอัดตอนสตาร์ท (psi) ประมาณจากอัตราส่วนกำลังอัด: Wave 125i 9.3:1 ≈ 190 psi</summary>
    public static float CrankPsi(float cr) => 14.7f * MathF.Pow(cr, 1.15f);
    public static float CrFromPsi(float psi) => MathF.Pow(psi / 14.7f, 1f / 1.15f);

    /// <summary>แรงดันอัดที่วัดได้ (psi) = จาก CR+แคม x ความแน่นของซีล</summary>
    public float Psi => CrankPsi(CrDynamic) * Seal;

    public float CamLossFactor => 1f - 0.0020f * MathF.Max(0f, Duration - 220f);
    public float CrDynamic => Cr * CamLossFactor;

    public EngineDesign Clone() => (EngineDesign)MemberwiseClone();

    public static readonly (string Name, EngineDesign D)[] Presets =
    {
        // Honda Wave 125i (สเปกมาเลเซีย 2018): 52.4x57.9 มม., กำลังอัด 9.3, 6.83 kW/7,500, 10.2 N·m/5,500 รอบ, อัตราทด/สเตอร์ 14/36 ตามคู่มือ
        // แคม/ยกวาล์ว/เรือน/LSA เป็นค่าประมาณ ปรับให้กำลังและแรงบิดและทรงกราฟตรงสามจุดนี้ (ผลโมเดล 6.91 kW/7,250, 10.1 N·m/5,600)
        ("Wave 125i เดิม", new EngineDesign()),
        ("เวฟ 110 เดิม*", new EngineDesign
        {
            Bore = 50f, Stroke = 55.6f, Rod = 89f, CrankKg = 2.6f, FlywheelKg = 0.6f, Throttle = 22f, Cr = 9.0f, Limiter = 9000f,
        }),
        ("ลูก 53 แคมแต่ง*", new EngineDesign
        {
            Bore = 53f, Stroke = 55.6f, Rod = 89f, CrankKg = 2.2f, FlywheelKg = 0.5f, Valves = 2, Throttle = 26f,
            Lsa = 108f, Duration = 245f, DurationEx = 245f, Lift = 7.0f, LiftEx = 7.0f, Cr = 10.0f, Limiter = 10500f,
        }),
        ("ลูก 67 ซิ่ง*", new EngineDesign
        {
            Bore = 67f, Stroke = 55.6f, Rod = 89f, CrankKg = 1.4f, FlywheelKg = 0.4f, Valves = 2, Throttle = 34f,
            Lsa = 104f, Duration = 290f, DurationEx = 290f, Lift = 9.0f, LiftEx = 9.0f, Cr = 11.5f, Limiter = 12800f,
            SprocketF = 14f, SprocketR = 36f,
        }),
        ("ออโต้ 125 (CVT)*", new EngineDesign
        {
            Auto = 1, RampDeg = 13.5f, Rollers = 6, RollerG = 8.5f, RollerG2 = 8.5f,
            GearF = 13, GearR = 40, FirstRed = 3.0f, BikeKg = 113f, RimInch = 14f, RimW = 1.60f, TireW = 90f, TireAsp = 90f,
        }),
        // Yamaha XMAX 300: สเปกจริง (คู่มือเจ้าของรถ/ผู้ผลิต) 292cc 70x75.8 กำลังอัด 10.9 4 วาล์ว 28 PS/7,250 29 N·m/5,750
        // ลดขั้นแรก 48/18 (ตายตัว) x เฟืองท้าย 37/13 = 7.59, ยางหลัง 140/70-14, รถ 179 กก. ส่วนแคม/เรือน/ก้านสูบ/ข้อเหวี่ยง/เม็ด/สปริงเป็นค่าประมาณ ปรับให้ได้กำลังตรงสเปก
        ("XMAX 300 (CVT)", new EngineDesign
        {
            Bore = 70f, Stroke = 75.8f, Rod = 118f, CrankKg = 4.7f, FlywheelKg = 0.8f, Valves = 4, Throttle = 30f,
            Lsa = 110f, Duration = 225f, Lift = 7.5f, LiftEx = 7.5f, Cr = 10.9f, Limiter = 8800f,
            Auto = 1, RampDeg = 13.5f, Rollers = 6, RollerG = 13.5f, RollerG2 = 13.5f, SpringK = 20f,
            GearF = 13, GearR = 37, FirstRed = 48f / 18f, BikeKg = 180f, RimInch = 14f, RimW = 3.50f, TireW = 140f, TireAsp = 70f, RiderKg = 75f, CdA = 0.32f,
        }),
    };

    // ---------- บันทึก/โหลดแบบข้อความง่าย ๆ ----------
    public string Serialize() => string.Join(";", Params.Where(p => p.Persist).Select(p => p.Key + "=" + p.Get(this).ToString(CultureInfo.InvariantCulture)));

    public static EngineDesign Deserialize(string s)
    {
        var d = new EngineDesign();
        bool hasNew = s.Contains("firstred=");
        foreach (var kv in s.Split(';'))
        {
            var parts = kv.Split('=');
            if (parts.Length != 2) continue;
            var p = Params.FirstOrDefault(x => x.Key == parts[0]);
            if (p != null && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                p.Set(d, Math.Clamp(v, p.Min, p.Max));
        }
        if (s.Contains("wheelmm=") && !s.Contains("rim="))       // เซฟเก่าเก็บเป็นรัศมีล้อ: เลือกชุดล้อใกล้เคียง
        {
            if (d.Auto == 1 && MathF.Abs(d.Bore - 70f) < 0.3f) { d.RimInch = 14f; d.RimW = 3.5f; d.TireW = 140f; d.TireAsp = 70f; }
            else if (d.Auto == 1) { d.RimInch = 14f; d.RimW = 1.6f; d.TireW = 90f; d.TireAsp = 90f; }
        }
        if (!s.Contains("rampout=")) d.RampOutDeg = d.RampDeg;       // เซฟเก่า: ใบนอกเท่าใบใน
        if (!s.Contains("liftex=")) d.LiftEx = d.Lift;              // เซฟเก่า: ยกไอเสียเท่าไอดี
        if (!s.Contains("durex=")) d.DurationEx = d.Duration;       // เซฟเก่า: ไอเสียเท่าไอดี
        if (!s.Contains("fly="))
        { d.FlywheelKg = 0.6f; d.CrankKg = MathF.Max(1.2f, d.CrankKg - 0.6f); }   // เซฟเก่า: นน.เดิมรวมจานไฟ
        if (!hasNew && d.Auto == 1 && MathF.Abs(d.Bore - 70f) < 0.3f && MathF.Abs(d.Stroke - 75.8f) < 0.3f)
        { d.FirstRed = 48f / 18f; d.CdA = 0.32f; }      // เซฟ XMAX 300 เก่าที่ยังไม่เก็บลดขั้นแรก/CdA
        return d;
    }

    /// <summary>รายการค่าที่ปรับได้ในอู่ (ชื่อ, หน่วย, ช่วง, ขั้น)</summary>
    public static readonly ParamDef[] Params =
    {
        new("bore", "ขนาดลูกสูบ", "มม.", 47f, 120f, 0.5f, "0.0", d => d.Bore, (d, v) => d.Bore = v,
            "ลูกใหญ่ = ซีซีเยอะ แรงขึ้น แต่น็อกง่ายขึ้น"),
        new("stroke", "ระยะชัก", "มม.", 49.5f, 100f, 0.1f, "0.0", d => d.Stroke, (d, v) => d.Stroke = v,
            "ชักยาว = แรงบิดดี แต่ลูกสูบวิ่งเร็ว ลากรอบสูงไม่ได้"),
        new("rod", "ก้านสูบ", "มม.", 80f, 200f, 0.5f, "0.0", d => d.Rod, (d, v) => d.Rod = v,
            "ก้านยาว = แรงเสียดทาน/แรงกระชากลูกสูบน้อยลง"),
        new("crank", "นน.ข้อเหวี่ยง", "กก.", 1.2f, 16f, 0.1f, "0.0", d => d.CrankKg, (d, v) => d.CrankKg = v,
            "เบา = รอบขึ้น-ลงไว แต่เดินเบาไม่นิ่ง"),
        new("fly", "นน.จานไฟ", "กก.", 0.2f, 3f, 0.05f, "0.00", d => d.FlywheelKg, (d, v) => d.FlywheelKg = v,
            "จานไฟหนัก = ความเฉื่อยสูง รอบขึ้นช้า/ออกตัวนิ่ง เดินเบาเรียบ  เบา = เร่งรอบไว แต่เดินเบาสะดุด"),
        new("valves", "จำนวนวาล์ว", "วาล์ว", 2f, 4f, 2f, "0", d => d.Valves, (d, v) => d.Valves = (int)v,
            "4 วาล์ว = ไอดีเข้าเยอะ วาล์วเบา ลากรอบสูงได้"),
        new("throttle", "ขนาดเรือน", "มม.", 20f, 60f, 1f, "0", d => d.Throttle, (d, v) => d.Throttle = v,
            "เรือนใหญ่ = ปลายไหล แต่ต้องมีวาล์ว/แคมรองรับ"),
        new("pipe", "ท่อไอเสีย", "", 0f, 3f, 1f, "0", d => d.Pipe, (d, v) => d.Pipe = (int)v,
            "เดิม = เงียบ ลมไหลดีพอใช้  ผ่าหมก = ถอดไส้บางส่วน ปลายขึ้นนิดหน่อย เสียงดังขึ้น  ผ่าเปิด = ไอเสียออกเต็มที่ ปลายดีแต่รอบต่ำตก เสียงแผดดังมาก  ท่อสูตร = จูนรอบเดียว พีคแรงขึ้นชัดแต่ช่วงล่างหาย ต้องคู่แคม/รอบที่ตรงกัน",
            text: v => PipeNames[Math.Clamp((int)MathF.Round(v), 0, 3)]),
        new("lsa", "มุมแคม (LSA)", "°", 98f, 118f, 1f, "0", d => d.Lsa, (d, v) => d.Lsa = v,
            "มุมแคบ = แรงบิดพีคสูงแต่ช่วงแคบ โอเวอร์แลปมาก เดินเบาสะดุด"),
        new("duration", "องศาแคม ไอดี (IN)", "°", 200f, 320f, 2f, "0", d => d.Duration, (d, v) => d.Duration = v,
            "องศามาก = แรงย้ายไปรอบสูง เดินเบาสั่น  ไอดีปิดช้า = กำลังอัดจริงลด"),
        new("realhp", "แรงม้ารถจริง (อ้างอิง)", "HP", 0f, 500f, 0.5f, "0.0", d => d.RealHp, (d, v) => d.RealHp = v,
            "ใส่แรงม้าสูงสุดที่วัดได้จากรถจริง (0 = ไม่ระบุ) แล้วกด C ปรับเทียบ ให้โมเดลคำนวณแรงบิดตรงกับรถจริง  จากนั้นปรับอะไหล่แล้วดูผลต่างจากรถจริงได้",
            text: v => v <= 0f ? "ไม่ระบุ" : $"{v:0.0} HP"),
        new("realhprpm", "รอบที่ได้แรงม้าสูงสุด", "rpm", 0f, 16000f, 100f, "0", d => d.RealHpRpm, (d, v) => d.RealHpRpm = v,
            "รอบที่รถจริงได้แรงม้าสูงสุด (0 = ไม่ระบุ) ใช้เทียบกับพีคของโมเดล",
            text: v => v <= 0f ? "ไม่ระบุ" : $"{v:N0} rpm"),
        new("realtq", "แรงบิดรถจริง (อ้างอิง)", "N·m", 0f, 500f, 0.1f, "0.0", d => d.RealTq, (d, v) => d.RealTq = v,
            "ใส่แรงบิดสูงสุดที่วัดได้จากรถจริง (0 = ไม่ระบุ) ถ้าไม่ได้ใส่แรงม้า ใช้แรงบิดนี้ปรับเทียบแทน",
            text: v => v <= 0f ? "ไม่ระบุ" : $"{v:0.0} N·m"),
        new("realtqrpm", "รอบที่ได้แรงบิดสูงสุด", "rpm", 0f, 16000f, 100f, "0", d => d.RealTqRpm, (d, v) => d.RealTqRpm = v,
            "รอบที่รถจริงได้แรงบิดสูงสุด (0 = ไม่ระบุ) ใช้เทียบกับพีคของโมเดล",
            text: v => v <= 0f ? "ไม่ระบุ" : $"{v:N0} rpm"),
        new("calk", "ตัวคูณปรับเทียบโมเดล", "x", 0.3f, 3f, 0.01f, "0.00", d => d.CalK, (d, v) => d.CalK = v,
            "คูณแรงบิดของโมเดลทั้งช่วงรอบ (1.00 = ไม่ปรับ) ปกติให้กด C ปรับเทียบอัตโนมัติจากแรงม้า/แรงบิดรถจริง  ค่านี้คงที่เมื่อเปลี่ยนอะไหล่ จึงเทียบก่อน/หลังได้"),
        new("durationex", "องศาแคม ไอเสีย (EX)", "°", 200f, 320f, 2f, "0", d => d.DurationEx, (d, v) => d.DurationEx = v,
            "ไอเสียยาวกว่าไอดี = ไอเสียไหลดี ปลายไปไกล แต่โอเวอร์แลปมาก เดินเบาสะดุด  สั้นกว่าไอดีมาก = ไอเสียอั้น ปลายตก"),
        new("lift", "ระยะยกแคม ไอดี (IN)", "มม.", 5f, 16f, 0.1f, "0.0", d => d.Lift, (d, v) => d.Lift = v,
            "ยกสูง = ไอดีไหลดี แต่วาล์วลอยที่รอบต่ำลง"),
        new("liftex", "ระยะยกแคม ไอเสีย (EX)", "มม.", 5f, 16f, 0.1f, "0.0", d => d.LiftEx, (d, v) => d.LiftEx = v,
            "ยกไอเสียน้อยกว่าไอดีมาก = ไอเสียอั้น ปลายตก  ยกมากกว่า = ไอเสียไหลดีขึ้นเล็กน้อย แต่วาล์วลอยง่ายขึ้น"),
        new("cr", "อัตราส่วนกำลังอัด (CR)", ":1", 7f, 20f, 0.1f, "0.0", d => d.Cr, (d, v) => d.Cr = v,
            "ค่าเรขาคณิต (ปริมาตรห้องเผาไหม้) สูง = ประสิทธิภาพเชิงความร้อนดี เสียงแผด  แยกจากแรงดันอัด (psi) ที่ปรับอีกบรรทัด",
            text: v => $"{v:0.0} : 1"),
        new("psi", "แรงดันอัด (วัดจริง)", "psi", 100f, 480f, 5f, "0", d => d.Psi, (d, v) => d.Seal = Math.Clamp(v / CrankPsi(d.CrDynamic), 0.6f, 1.5f),
            "ค่าที่เกจวัดตอนสตาร์ท ปรับแยกจาก CR: ซีลแน่น = แรงบิดดีขึ้นแต่น็อกง่าย  ต่ำ = รั่ว กำลังตก จุดไม่ติด (Wave ~190 psi, แต่งถนน 200-230, แข่ง 260-300+)",
            text: v => $"{v:0} psi", persist: false),
        new("seal", "ความแน่นซีล", "x", 0.6f, 1.5f, 0.01f, "0.00", d => d.Seal, (d, v) => d.Seal = v, "", d => false),
        new("fuel", "ชนิดน้ำมัน", "", 0f, 6f, 1f, "0", d => d.Fuel, (d, v) => d.Fuel = (int)v,
            "ออกเทนสูง = ทนกำลังอัดสูงก่อนน็อก  E85/เมทานอล/ไนโตร ฉีดหนาขึ้นได้กำลังเพิ่มด้วย",
            text: v => Fuels[Math.Clamp((int)MathF.Round(v), 0, Fuels.Length - 1)].Name + $"  (RON {Fuels[Math.Clamp((int)MathF.Round(v), 0, Fuels.Length - 1)].Ron:0})"),
        new("limiter", "รอบตัด (CDI)", "rpm", 6000f, 15000f, 100f, "0", d => d.Limiter, (d, v) => d.Limiter = v,
            "รอบตัดแรง (hard cut): ถึงรอบนี้ตัดไฟทั้งหมดทันที ตั้งเกินที่เครื่องรับไหว = พัง"),
        new("resume", "รอบเด้งกลับหลัง hard cut", "rpm", 20f, 3000f, 10f, "0", d => d.ResumeGap, (d, v) => d.ResumeGap = v,
            "ตัดไฟที่รอบตัดแล้ว รอบต้องตกลงมาถึงรอบนี้จึงจุดระเบิดใหม่  เช่น ตัด 10000 เด้งกลับ 9800 = ตกลง 200  ห่างน้อย = ตัดสลับถี่ เสียงรัว  ห่างมาก = ตัดนาน รอบตกลึกก่อนติดใหม่",
            textD: (d, v) => $"{d.Limiter - v:0} rpm  (ตกลง {v:0})"),
        new("softcut", "Soft cut ห่างใต้ hard cut", "rpm", 0f, 2000f, 50f, "0", d => d.SoftGap, (d, v) => d.SoftGap = v,
            "soft cut เริ่มหน่วงไฟ/ข้ามจังหวะระเบิดเป็นช่วง ๆ ตั้งแต่ใต้ hard cut ลงมาตามค่านี้ แล้วค่อย ๆ เพิ่มจนถึง hard cut  เช่น hard 10500 ห่าง 100 = soft เริ่มที่ 10400  0 = ไม่ใช้ soft cut (ตัดแรงอย่างเดียว)",
            textD: (d, v) => v <= 0f ? "ปิด" : $"{d.Limiter - v:0} rpm  (ห่าง {v:0})"),
        new("prim", "อัตราทดขั้นต้น", ":1", 2.5f, 4.5f, 0.01f, "0.000", d => d.Primary, (d, v) => d.Primary = v,
            "ทดขั้นต้นสูง = ทุกเกียร์ทดจัดขึ้นเท่ากันหมด (Wave 125i = 3.350)", d => d.Auto == 0),
        new("g1", "อัตราทดเกียร์ 1", ":1", 1.6f, 4.0f, 0.01f, "0.000", d => d.Gear1, (d, v) => d.Gear1 = v,
            "ตัวเลขมาก = ทดจัด ออกตัวแรง ความเร็วต่อเกียร์ต่ำ (Wave 2.500)", d => d.Auto == 0),
        new("g2", "อัตราทดเกียร์ 2", ":1", 1.1f, 2.8f, 0.01f, "0.000", d => d.Gear2, (d, v) => d.Gear2 = v,
            "ช่วงห่างเกียร์ 1-2 กว้าง = รอบตกมากตอนเปลี่ยน (Wave 1.550)", d => d.Auto == 0),
        new("g3", "อัตราทดเกียร์ 3", ":1", 0.8f, 2.0f, 0.01f, "0.000", d => d.Gear3, (d, v) => d.Gear3 = v,
            "เกียร์ชิด (ช่วงห่างน้อย) ไล่เกียร์ต่อเนื่อง แต่ท็อปสปีดไม่ไกลเท่าเกียร์ห่าง (Wave 1.150)", d => d.Auto == 0),
        new("g4", "อัตราทดเกียร์ 4", ":1", 0.6f, 1.5f, 0.01f, "0.000", d => d.Gear4, (d, v) => d.Gear4 = v,
            "ต่ำกว่า 1 = โอเวอร์ไดรฟ์ ปลายยาว (Wave 0.923) ถ้ายาวเกินกำลังไม่พอลากถึงรอบ", d => d.Auto == 0),
        new("sprf", "สเตอร์หน้า", "ฟัน", 11f, 18f, 1f, "0", d => d.SprocketF, (d, v) => d.SprocketF = v,
            "เล็กลง = ออกตัวดี/ไล่เกียร์ตึง แต่ปลายสั้น (ท็อปสปีดต่ำ)", d => d.Auto == 0),
        new("sprr", "สเตอร์หลัง", "ฟัน", 28f, 46f, 1f, "0", d => d.SprocketR, (d, v) => d.SprocketR = v,
            "ใหญ่ขึ้น = อัตราทดจัด ออกตัวแรง ท็อปสปีดต่ำ  เล็กลง = ปลายยาว", d => d.Auto == 0),
        new("rider", "น้ำหนักคนขี่", "กก.", 40f, 110f, 1f, "0", d => d.RiderKg, (d, v) => d.RiderKg = v,
            "น้ำหนักรวมมาก = ออกตัวช้า ไต่เนินช้า"),
        new("bikekg", "น้ำหนักรถเปล่า", "กก.", 80f, 260f, 1f, "0", d => d.BikeKg, (d, v) => d.BikeKg = v,
            "รถหนัก = ออกตัวและไต่ความเร็วช้า แต่นิ่งกว่า (Wave ~107, สกู๊ตเตอร์ 125 ~113, XMAX 300 ~180)"),
        new("cda", "พื้นที่ลมต้าน (CdA)", "ตร.ม.", 0.25f, 0.80f, 0.01f, "0.00", d => d.CdA, (d, v) => d.CdA = v,
            "ต่ำ = ลู่ลม ท็อปสปีดสูง (สกู๊ตเตอร์บังลมใหญ่ ~0.32, รถซิ่งนั่งตรง ~0.42-0.5, ฝาครอบเต็มคัน ~0.3)"),
        new("rim", "ขนาดขอบล้อ", "นิ้ว", 10f, 19f, 1f, "0", d => d.RimInch, (d, v) => d.RimInch = v,
            "ขอบใหญ่ = รัศมีล้อโต ความเร็วต่อรอบสูงขึ้น (เท่ากับทดท้ายยาวขึ้น) ล้อหนักขึ้น  ขอบเล็ก = ออกตัวไว (Wave 17 นิ้ว, สกู๊ตเตอร์ 14 นิ้ว)"),
        new("rimw", "ความกว้างขอบล้อ", "นิ้ว", 1.0f, 4.0f, 0.05f, "0.00", d => d.RimW, (d, v) => d.RimW = v,
            "ใส่ให้พอดีกับหน้ายาง (1.10 / 1.20 / 1.40 / 1.60 …) ขอบแคบหรือกว้างเกินจากที่ยางต้องการ = แรงเกาะตก ยางบิดตัว"),
        new("tirew", "หน้ายาง", "มม.", 50f, 190f, 5f, "0", d => d.TireW, (d, v) => d.TireW = v,
            "ยางหน้ากว้าง = เกาะถนนดีขึ้น ออกตัวไม่ฟรี แต่ต้านการกลิ้งมากขึ้นและล้อหนักขึ้น"),
        new("tireasp", "แก้มยาง", "%", 40f, 100f, 5f, "0", d => d.TireAsp, (d, v) => d.TireAsp = v,
            "แก้มสูง (ตัวเลขมาก) = รัศมีล้อโต ความเร็วต่อรอบสูงขึ้น นุ่ม  แก้มต่ำ = รัศมีเล็ก ทดจัดขึ้น หน้าตักแข็ง"),
        new("auto", "ระบบส่งกำลัง", "", 0f, 1f, 1f, "0", d => d.Auto, (d, v) => d.Auto = (int)v,
            "ออโต้ = สายพาน CVT ไม่ต้องเปลี่ยนเกียร์ ปรับชามเม็ด/น้ำหนักเม็ดเพื่อจูนการไล่รอบ",
            text: v => v > 0.5f ? "ออโต้ (CVT)" : "เกียร์ธรรมดา"),
        new("ramp", "องศาชาม ใบใน", "°", 11f, 16f, 0.5f, "0.0", d => d.RampDeg, (d, v) => d.RampDeg = v,
            "ช่วงต้นทาง (ออกตัว): องศาน้อย = เม็ดดันชุดขับแรง ไล่รอบเร็ว ถือรอบต่ำ  องศามาก = ถือรอบสูง ออกตัวจัด", d => d.Auto == 1),
        new("rampout", "องศาชาม ใบนอก", "°", 11f, 16f, 0.5f, "0.0", d => d.RampOutDeg, (d, v) => d.RampOutDeg = v,
            "ช่วงปลายทาง (ไล่สายพานใกล้สุด): องศาน้อยกว่าใบใน = ปลายดันแรง ไล่ถึงปลายไว รอบปลายต่ำลง  มากกว่าใบใน = ปลายถือรอบสูงขึ้น ท็อปไกล (ชามแต่งองศา 2 ช่วง เช่น 13/11)", d => d.Auto == 1),
        new("rollers", "จำนวนเม็ด", "เม็ด", 4f, 6f, 2f, "0", d => d.Rollers, (d, v) => d.Rollers = (int)v,
            "6 เม็ด = มวลรวมมาก ไล่รอบเร็ว นุ่ม  4 เม็ด = มวลน้อยลง ถือรอบสูงกว่า", d => d.Auto == 1),
        new("rollerg", "น้ำหนักเม็ด", "กรัม", 4f, 20f, 0.5f, "0.0", d => d.RollerG, (d, v) => d.RollerG = v,
            "เม็ดหนัก = ไล่รอบไว ถือรอบต่ำ ปลายตกเร็ว  เม็ดเบา = ถือรอบสูง ปลายไปไกล", d => d.Auto == 1),
        new("rollerg2", "น้ำหนักเม็ดสลับ", "กรัม", 4f, 20f, 0.5f, "0.0", d => d.RollerG2, (d, v) => d.RollerG2 = v,
            "เม็ดตำแหน่งที่ 2, 4, 6 ใช้น้ำหนักนี้ (เท่าเม็ดหลัก = เรียงเม็ดเท่ากันหมด) ผสมหนัก/เบาเพื่อจูนละเอียด", d => d.Auto == 1),
        new("fdf", "เฟืองท้าย ตัวหน้า", "ฟัน", 10f, 20f, 1f, "0", d => d.GearF, (d, v) => d.GearF = (int)v,
            "คู่เฟืองท้ายที่ช่างเปลี่ยนจูน (เช่น 15/38) ฟันหน้าน้อย/ฟันหลังมาก = ทดจัด ออกตัวแรง ท็อปสปีดลด  ตรงข้ามคือปลายยาว", d => d.Auto == 1),
        new("fdr", "เฟืองท้าย ตัวหลัง", "ฟัน", 28f, 50f, 1f, "0", d => d.GearR, (d, v) => d.GearR = (int)v,
            "ฟันมาก = ทดจัดขึ้น ออกตัวไว ท็อปสปีดลด  ฟันน้อย = ปลายยาว ออกตัวอืด", d => d.Auto == 1),
        new("firstred", "ลดขั้นแรก (ในเรือนเกียร์)", ":1", 2.0f, 4.0f, 0.01f, "0.000", d => d.FirstRed, (d, v) => d.FirstRed = v,
            "ตายตัวตามรถ (XMAX 300 = 48/18 = 2.667) ทดรวมหลังสายพาน = ค่านี้ x เฟืองท้ายหลัง/หน้า", d => d.Auto == 1),
        new("spring", "สปริงทอค (ความแข็ง)", "N/มม.", 10f, 40f, 1f, "0", d => d.SpringK, (d, v) => d.SpringK = v,
            "แข็ง = ชุดขับหลังหนีบแน่น CVT ไล่รอบช้า ถือรอบสูง ออกตัว/ไต่เนินดี  นิ่ม = ไล่รอบไว ถือรอบต่ำ ปลายยาว", d => d.Auto == 1),
    };
}

public sealed class ParamDef
{
    public readonly string Key, Name, Unit, Format, Hint;
    public readonly bool Persist;
    public readonly float Min, Max, Step;
    public readonly Func<EngineDesign, float> Get;
    public readonly Action<EngineDesign, float> Set;
    private readonly Func<EngineDesign, bool>? _visible;
    private readonly Func<float, string>? _text;
    private readonly Func<EngineDesign, float, string>? _textD;

    public ParamDef(string key, string name, string unit, float min, float max, float step, string format,
        Func<EngineDesign, float> get, Action<EngineDesign, float> set, string hint,
        Func<EngineDesign, bool>? visible = null, Func<float, string>? text = null, bool persist = true,
        Func<EngineDesign, float, string>? textD = null)
    {
        Key = key; Name = name; Unit = unit; Min = min; Max = max; Step = step; Format = format;
        Get = get; Set = set; Hint = hint; _visible = visible; _text = text; Persist = persist; _textD = textD;
    }

    public bool IsVisible(EngineDesign d) => _visible == null || _visible(d);

    /// <summary>ข้อความค่าที่แสดงในอู่</summary>
    public string Display(EngineDesign d)
    {
        float v = Get(d);
        if (_textD != null) return _textD(d, v);
        return _text != null ? _text(v) : v.ToString(Format) + (Unit.Length > 0 ? " " + Unit : "");
    }

    public void Nudge(EngineDesign d, int dir, bool coarse)
    {
        float step = Step * (coarse && Step < 2f ? 10f : 1f);
        float v = Get(d) + dir * step;
        v = MathF.Round(v / Step) * Step;
        Set(d, Math.Clamp(v, Min, Max));
    }
}

/// <summary>
/// คำนวณสมรรถนะจากสเปก (ไม่เปลี่ยนแปลงหลังสร้าง ปลอดภัยต่อการอ่านข้ามเธรด)
/// โมเดลง่าย ๆ แต่ทิศทางตรงกับเครื่องจริง: ปริมาตร × ประสิทธิภาพการประจุ (VE) × ประสิทธิภาพเชิงความร้อน
/// </summary>
public sealed class EngineModel
{
    public const float MaxRpm = 16000f;
    private const int Step = 50;
    private const int N = (int)(MaxRpm / Step) + 2;

    public readonly EngineDesign D;
    public readonly float Cc;                // ซีซี
    public readonly float RodRatio;          // ก้าน / ชัก
    public readonly float Overlap;           // องศาโอเวอร์แลป
    public readonly float RpmCam;            // รอบที่แคมทำงานดีสุด
    public readonly float RpmFlow;           // รอบที่ไอดีเริ่มอั้น (วาล์ว + เรือน)
    public readonly float FloatRpm;          // รอบที่วาล์วเริ่มลอย
    public readonly float CrLimit;           // กำลังอัดไดนามิกสูงสุดก่อนน็อก (ขึ้นกับน้ำมันและขนาดลูก)
    public readonly float CrDyn;             // กำลังอัดไดนามิกจริงในกระบอก (แคมองศามากปิดวาล์วไอดีช้า ความดันรั่วกลับ จึงต่ำกว่าสถิต)
    public readonly float SoundBright;       // ความแผดของเสียงเทียบเสียงต้นแบบ: + แผด/คม  - นุ่ม/ทึบ
    public readonly float PitchStretch;      // เครื่องเล็กพัลส์สั้น/แหลม เครื่องใหญ่พัลส์ยาว/ทุ้ม (เทียบ 196cc ของเสียงต้นแบบ)
    public readonly float CrAmp;             // กำลังอัดสูง = แรงดันพุ่งออกท่อแรงกว่า
    public readonly float LowCrMisfire;      // กำลังอัดต่ำ เดินเบาจุดไม่ติดบางรอบ
    private readonly float[] _veN = new float[N];
    public readonly float Inertia;           // kg·m² (ข้อเหวี่ยง + จานไฟ)
    public readonly float IdleSteady;        // จานไฟหนัก = เดินเบานิ่ง
    public readonly float Psi;               // แรงดันอัดจริง (psi)
    public readonly float CrEff;             // กำลังอัดสถิตเทียบเท่าตามแรงดันจริง
    public readonly float SealGain;          // ตัวคูณแรงบิดจากความแน่นซีล
    public readonly float IdleRpm;
    public readonly float AirK;              // ความไวของไอดีต่อการเปิดลิ้น
    public readonly float Lope;              // 0..1 เดินเบาสะดุด/ลูปแคม
    public readonly float Loudness;
    public readonly float Hunt;              // 0..1 เดินหอบ (แคมองศามาก รอบเดินเบาเหวี่ยงขึ้น-ลงเป็นจังหวะ)
    public readonly float RpmPipe;           // รอบที่ท่อสูตรทำงานดีสุด
    public readonly float FrictionScale;

    public readonly float PeakPs, PeakPsRpm, PeakTq, PeakTqRpm;
    private readonly float[] _tInd = new float[N];
    private readonly float[] _fric = new float[N];

    public EngineModel(EngineDesign d)
    {
        D = d.Clone();
        float bore = D.Bore, stroke = D.Stroke;
        Cc = MathF.PI / 4f * bore * bore * stroke / 1000f;
        RodRatio = D.Rod / stroke;
        Overlap = MathF.Max(0f, D.DurAvg - 2f * D.Lsa);

        // ----- แคม: องศามาก = ทำงานรอบสูง, มุมแคบ = พีคแรงขึ้นแต่ช่วงแคบ -----
        RpmCam = 6300f + (D.DurAvg - 220f) * 82f + (D.Lsa - 108f) * 60f;
        float lsaN = D.Lsa / 108f;
        float wLow = RpmCam * 0.55f * MathF.Pow(lsaN, 1.5f);
        float wHigh = RpmCam * 0.42f * lsaN;
        float peakBoost = 1f + (108f - D.Lsa) * 0.006f;

        // ----- ทางไอดี: วาล์ว (ม่านวาล์ว) ต่ออนุกรมกับเรือนลิ้นเร่ง -----
        int nIn = D.Valves == 4 ? 2 : 1;
        float vd = bore * (D.Valves == 4 ? 0.40f : 0.52f);
        float area = nIn * MathF.PI / 4f * vd * vd;
        float curtain = nIn * MathF.PI * vd * D.Lift;
        float valveEff = 0.6f * MathF.Min(area, curtain);
        float tbEff = 0.85f * MathF.PI / 4f * D.Throttle * D.Throttle;
        float aEff = 1f / MathF.Sqrt(1f / (valveEff * valveEff) + 1f / (tbEff * tbEff));
        RpmFlow = 4770f * aEff / Cc;
        // ไอเสียเทียบไอดี: สั้นกว่าไอดี = ไอเสียอั้น ปลายตก  ยาวกว่า = ไหลดีขึ้นเล็กน้อย (เท่ากัน = ไม่เปลี่ยน)
        float exDiff = D.DurationEx - D.Duration;
        float exLift = D.LiftEx / D.Lift;
        RpmFlow *= exLift < 1f ? MathF.Max(0.6f, 1f - 0.8f * (1f - exLift)) : 1f + 0.15f * MathF.Min(exLift - 1f, 0.4f);
        RpmFlow *= exDiff < 0f ? MathF.Max(0.6f, 1f + 0.012f * exDiff) : 1f + 0.0025f * MathF.Min(exDiff, 40f);

        // ระยะยกเทียบขนาดวาล์ว: ~0.28 ถือว่าพอดี
        float ld = D.Lift / vd;
        float liftF = MathF.Sqrt(MathF.Min(1f, ld / 0.28f));
        // เครื่องแต่งแรง (แคมองศามาก + 4 วาล์ว) หายใจดีกว่าเครื่องบ้าน: ท่อไอดี/ไอเสียจูนรอบจัด ทำ VE เกิน 1 ได้
        // ปรับให้ตรงเครื่องจริง: 4 วาล์วแข่ง ~120-135 PS/L (CRF150R/250R), 2 วาล์วแต่งแคม ~105-120 PS/L
        float camPower = Math.Clamp((D.DurAvg - 225f) / 65f, 0f, 1f);
        float valveGain = D.Valves == 4 ? 1.10f + 0.12f * camPower : 1f;
        float camGain = 1f + 0.07f * Math.Clamp((D.DurAvg - 250f) / 50f, 0f, 1f);
        float vePeak = 0.92f * liftF * valveGain * camGain * peakBoost;

        // ----- วาล์วลอย -----
        FloatRpm = 14200f - 400f * (MathF.Max(D.Lift, D.LiftEx) - 6f) + (D.Valves == 4 ? 2500f : 0f) + (D.DurAvg - 220f) * 6f;

        // ----- น็อก: ลูกใหญ่เปลวไฟวิ่งไกล น็อกง่าย -----
        // พิกัดน็อก: ลูกใหญ่ เปลวไฟวิ่งไกลน็อกง่าย, ออกเทนสูงขึ้นทน +0.30 ต่อ 1 RON (95 = ฐาน)
        CrLimit = 12.6f - 0.05f * (bore - 50f) + 0.30f * (D.Octane - 95f);
        // ไดนามิก CR: องศาแคมมาก (วาล์วไอดีปิดช้า) ทำให้อัดจริงน้อยลง ~0.20% ต่อองศา เหนือ 220°
        CrDyn = D.CrDynamic * MathF.Pow(D.Seal, 1f / 1.15f);      // น็อกดูจากแรงดันจริงในกระบอก
        Psi = D.Psi;
        CrEff = D.Cr * MathF.Pow(D.Seal, 1f / 1.15f);
        SealGain = D.Seal < 1f ? MathF.Max(0.3f, 1f - 0.8f * (1f - D.Seal)) : 1f + 0.2f * (D.Seal - 1f);
        CrAmp = Math.Clamp(0.90f + 0.035f * (CrEff - 9.5f), 0.75f, 1.35f);
        LowCrMisfire = Math.Clamp((8.8f - CrEff) * 0.12f, 0f, 0.3f);
        PitchStretch = Math.Clamp(MathF.Pow(Cc / 196f, 0.25f), 0.8f, 1.5f);
        // ความแผด: กำลังอัด, ลูกกว้าง/ชักสั้น (bore/stroke), องศาแคม, 4 วาล์ว, ก้านสั้นหยาบ  เทียบเครื่องต้นแบบ (67/55.6 กำลังอัด 11.5 แคม 290 2 วาล์ว)
        float bright = 0.07f * D.Cr + 0.6f * (bore / stroke) + 0.0018f * D.DurAvg + (D.Valves == 4 ? 0.12f : 0f) - 0.25f * (RodRatio - 1.55f);
        const float brightRef = 0.07f * 11.5f + 0.6f * (67f / 55.6f) + 0.0018f * 290f - 0.25f * (89f / 55.6f - 1.55f);
        SoundBright = Math.Clamp((bright - brightRef) * 1.5f + 0.3f + (D.Pipe switch { 1 => 0.25f, 2 => 0.5f, 3 => 0.7f, _ => 0f }), -0.9f, 1.7f);   // +0.3: เครื่องบ้านไม่ทึบเกินไป
        float eff = 1f - MathF.Pow(D.Cr, -0.3f);

        // ----- แรงเสียดทาน -----
        float rodF = Math.Clamp(1f + 0.25f * (1.65f - RodRatio), 0.85f, 1.3f);
        FrictionScale = MathF.Pow(Cc / 150f, 0.6f) * (D.Valves == 4 ? 1.08f : 1f) * rodF;

        Inertia = 0.0076f + D.CrankKg * 0.0016f + D.FlywheelKg * 0.0040f;
        IdleSteady = Math.Clamp(0.0144f / Inertia, 0.55f, 1.8f);
        IdleRpm = 1400f + MathF.Max(0f, Overlap - 10f) * 10f;
        AirK = 40000f * (D.Throttle / 28f) * (D.Throttle / 28f) * (Cc / 110f);
        Lope = Math.Clamp((Overlap - 20f) / 70f + (D.Pipe == 3 ? 0.12f : 0f), 0f, 1f);
        Hunt = Math.Clamp((D.DurAvg - 245f) / 45f, 0f, 1f);
        RpmPipe = RpmCam * 1.04f;
        float pipeLoud = D.Pipe switch { 1 => 1.25f, 2 => 1.55f, 3 => 1.7f, _ => 1f };
        Loudness = Math.Min(2.0f, MathF.Pow(Cc / 196f, 0.45f) * (0.85f + 0.15f * D.Throttle / 34f) * pipeLoud);


        float dispL = Cc / 1000f;
        float peakPs = 0, peakPsRpm = 0, peakTq = 0, peakTqRpm = 0;
        for (int i = 0; i < N; i++)
        {
            float rpm = i * Step;
            float dw = (rpm - RpmCam) / (rpm < RpmCam ? wLow : wHigh);
            float ve = vePeak * (0.55f + 0.45f * MathF.Exp(-dw * dw));
            ve /= MathF.Pow(1f + MathF.Pow(rpm / RpmFlow, 4f), 0.25f);
            if (rpm > FloatRpm)
            {
                float f = (rpm - FloatRpm) / 900f;
                ve *= MathF.Exp(-f * f);
            }
            ve *= PipeGain(rpm);
            _veN[i] = ve;
            _tInd[i] = ve * dispL * 245f * eff * D.FuelPower * SealGain;
            // ความเร็วลูกสูบเฉลี่ย (ม./วิ) เกิน ~24 แรงเสียดทาน/แรงเฉื่อยพุ่ง (ชักยาว รอบจัดลากไม่ไหว)
            float vp = 2f * stroke * rpm / 60000f;
            float vpPen = vp > 24f ? 0.35f * (vp - 24f) * (vp - 24f) * FrictionScale : 0f;
            _fric[i] = (0.35f + 0.00018f * rpm + 1.0e-8f * rpm * rpm) * FrictionScale + vpPen;

            if (rpm >= 2000f && rpm <= D.Limiter)
            {
                float tq = BrakeTorqueWot(rpm);
                float ps = tq * rpm / 7023f;
                if (tq > peakTq) { peakTq = tq; peakTqRpm = rpm; }
                if (ps > peakPs) { peakPs = ps; peakPsRpm = rpm; }
            }
        }
        float veMax = 1e-6f;
        for (int i = 0; i < N; i++) veMax = MathF.Max(veMax, _veN[i]);
        for (int i = 0; i < N; i++) _veN[i] /= veMax;
        PeakPs = peakPs; PeakPsRpm = peakPsRpm; PeakTq = peakTq; PeakTqRpm = peakTqRpm;
    }

    private float PipeGain(float rpm)
    {
        switch (D.Pipe)
        {
            case 1: return 0.99f + 0.04f * Math.Clamp((rpm - 3500f) / 5000f, 0f, 1f);
            case 2: return 0.96f + 0.09f * Math.Clamp((rpm - 4000f) / 5000f, 0f, 1f);
            case 3:
                float dw = (rpm - RpmPipe) / (0.28f * RpmPipe);
                return 0.89f + 0.21f * MathF.Exp(-dw * dw);
            default: return 1f;
        }
    }

    private static float Lerp(float[] t, float rpm)
    {
        float x = Math.Clamp(rpm, 0f, MaxRpm) / Step;
        int i = (int)x;
        float f = x - i;
        return t[i] + (t[i + 1] - t[i]) * f;
    }

    /// <summary>แรงบิดจากการเผาไหม้ (ยังไม่หักแรงเสียดทาน) ที่ไอดีเต็ม</summary>
    public float TorqueInd(float rpm) => Lerp(_tInd, rpm) * D.CalK;

    public float Friction(float rpm) => Lerp(_fric, rpm);

    /// <summary>ปริมาณไอดีต่อรอบตามการเปิดลิ้น (0..1)</summary>
    public float Load(float throttleEff, float rpm) => 1f - MathF.Exp(-throttleEff * AirK / MathF.Max(rpm, 400f));

    /// <summary>ประสิทธิภาพการหายใจที่รอบ rpm เทียบรอบที่ดีที่สุดของเครื่องนี้ (0..1) ใช้กำหนดความแรงของพัลส์ไอเสีย</summary>
    public float VeNorm(float rpm) => Lerp(_veN, rpm);

    /// <summary>ความแรงของการน็อก 0..1 (ภาระสูง รอบต่ำ น็อกง่ายสุด)</summary>
    public float Knock(float rpm, float load)
    {
        float k = (CrDyn - CrLimit) / 1.5f;
        if (k <= 0f) return 0f;
        return Math.Clamp(k, 0f, 1f) * load * load * Math.Clamp(1.4f - rpm / 8000f, 0.3f, 1f);
    }

    /// <summary>แรงบิดที่ล้อไดโนเห็น ตอนเปิดคันเร่งสุด</summary>
    public float BrakeTorqueWot(float rpm)
    {
        float load = Load(1f, rpm);
        float kn = Knock(rpm, load);
        return TorqueInd(rpm) * load * (1f - 0.5f * kn) - Friction(rpm);
    }

    public static float Ps(float torque, float rpm) => torque * rpm / 7023f;

    /// <summary>แรงม้า HP (อังกฤษ 745.7 W) จากแรงบิด N·m และรอบ</summary>
    public static float Hp(float torque, float rpm) => torque * rpm / 7121f;
    public const float PsToHp = 0.98632f;
    public float PeakHp => PeakPs * PsToHp;

    /// <summary>
    /// หาตัวคูณปรับเทียบ CalK ให้พีคของโมเดลตรงรถจริง (ใช้แรงม้าก่อน ถ้าไม่มีใช้แรงบิด) คืน null ถ้าไม่ได้ใส่ค่ารถจริง
    /// </summary>
    public static float? Calibrate(EngineDesign d)
    {
        if (d.RealHp <= 0f && d.RealTq <= 0f) return null;
        var t = d.Clone();
        t.CalK = 1f;
        for (int i = 0; i < 8; i++)
        {
            var m = new EngineModel(t);
            float have = d.RealHp > 0f ? m.PeakHp : m.PeakTq;
            float want = d.RealHp > 0f ? d.RealHp : d.RealTq;
            if (have < 0.01f) return null;
            t.CalK = Math.Clamp(t.CalK * want / have, 0.3f, 3f);
        }
        return t.CalK;
    }

    /// <summary>ความเร็วลูกสูบเฉลี่ย (ม./วิ) ที่รอบ rpm</summary>
    public float PistonSpeed(float rpm) => 2f * D.Stroke * rpm / 60000f;
}
