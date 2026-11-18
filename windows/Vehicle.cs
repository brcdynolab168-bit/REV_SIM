namespace RevGame;

/// <summary>
/// ตัวรถ 2 แบบ
/// - เกียร์ธรรมดา: อัตราทดตามสเปก Honda Wave 125i (คู่มือ Boon Siew Honda มาเลเซีย 2018)
///   เกียร์ 4 สปีด 2.500 / 1.550 / 1.150 / 0.923, อัตราทดขั้นต้น 3.350 (67/20), สเตอร์เดิม 14/36 (2.571), น้ำหนักรถ 107 กก.
/// - ออโต้: สายพาน CVT (ดู Cvt) ค่ารถประมาณจากสกู๊ตเตอร์ 125 ซีซีทั่วไป ไม่ใช่สเปกทางการ
/// </summary>
public static class Vehicle
{
    public static readonly float[] GearRatio = { 2.500f, 1.550f, 1.150f, 0.923f };
    public const float Primary = 3.350f;
    public const float WheelInertia = 0.9f;      // ล้อหน้า+หลัง รวมที่ผิวยาง (kg·m²) ของชุดล้อเวฟ แปลงเป็นมวลเทียบเท่า (ปรับตามขนาดล้อ ดู WheelInertiaFor)
    public const float DriveEff = 0.93f;         // ประสิทธิภาพขับเคลื่อน (เกียร์/สายพาน + โซ่)
    public const float Crr = 0.012f;             // แรงต้านการกลิ้ง (ยางมอเตอร์ไซค์บนถนนแห้ง)
    public const float CdA = 0.42f;              // พื้นที่ลม × สัมประสิทธิ์ลม (ตัวรถ + คนขี่) m²
    public const float AirRho = 1.18f;           // ความหนาแน่นอากาศ ที่ ~30°C
    public const float Mu = 0.95f;               // แรงเกาะยาง
    public const float CgRear = 0.50f;           // จุดศูนย์ถ่วงอยู่ห่างล้อหน้ากี่ส่วนของฐานล้อ (น้ำหนักลงล้อหลังตอนนิ่ง)
    public const float CgHeight = 0.40f;         // ความสูงจุดศูนย์ถ่วง ÷ ฐานล้อ

    /// <summary>
    /// แรงลากสูงสุดที่ล้อหลังที่ยางยังเกาะ (N): ตอนเร่งน้ำหนักย้ายไปล้อหลัง แรงเกาะจึงเพิ่มตามแรงเร่ง
    /// F = μ·N, N = m·g·b + F·h  =>  F = μ·m·g·b / (1 - μ·h)  (ถ้าเกินนี้ ล้อฟรี)
    /// </summary>
    public static float TractionMax(EngineDesign d) =>
        MuFor(d) * Mass(d) * G * CgRear / (1f - MuFor(d) * CgHeight);
    public const float G = 9.81f;
    public const float BrakeDecel = 7.0f;        // เบรกเต็มที่ (m/s²)

    public static float BikeKg(EngineDesign d) => d.BikeKg;

    /// <summary>รัศมีล้อกลิ้ง (m): เวฟ ยาง 80/90-17, ออโต้ ยาง 90/90-14</summary>
    public static float WheelRadius(EngineDesign d) => (d.RimInch * 12.7f + d.TireW * d.TireAsp / 100f) * 0.992f / 1000f;

    /// <summary>ความกว้างขอบล้อที่เหมาะกับหน้ายางนี้ (นิ้ว): 80 -> 1.40, 90 -> 1.69, 140 -> 3.50</summary>
    public static float IdealRim(float tireW) => 0.000125f * tireW * tireW + 0.0075f * tireW;

    /// <summary>ความเหมาะของขอบล้อกับหน้ายาง 0.7..1 (ห่างจากที่เหมาะเกิน 0.2 นิ้วเริ่มเสียแรงเกาะ)</summary>
    public static float RimFit(EngineDesign d)
    {
        float ideal = IdealRim(d.TireW);
        float m = MathF.Max(0f, MathF.Abs(d.RimW - ideal) - 0.2f) / ideal;
        return Math.Clamp(1f - 0.5f * m, 0.7f, 1f);
    }

    /// <summary>แรงเกาะยางหลัง: หน้ายางกว้างเกาะดีขึ้น (อ้างอิง 90 มม.) x ความเหมาะของขอบล้อ</summary>
    /// <summary>พื้นถนน: ตัวคูณแรงเกาะ (μ) และแรงต้านการกลิ้ง</summary>
    public readonly record struct SurfaceDef(string Name, float MuK, float CrrK, string Note);
    public static readonly SurfaceDef[] Surfaces =
    {
        new("ถนนทั่วไป", 1.00f, 1.00f, "ลาดยางทั่วไป แห้ง ค่าอ้างอิง"),
        new("ราดยาง", 0.90f, 1.25f, "ผิวหยาบ/หินเกล็ดราดยาง เกาะน้อยลง ต้านการกลิ้งมาก"),
        new("คอนกรีต", 1.05f, 0.92f, "ผิวแข็งเรียบ เกาะดี ต้านการกลิ้งน้อย"),
        new("แทร็ก", 1.25f, 0.85f, "ผิวแข่ง/สนามดรักเรียบเหนียว เกาะสูงสุด ต้านต่ำสุด"),
    };
    public static int SurfaceIndex = 0;
    private static SurfaceDef Surf => Surfaces[Math.Clamp(SurfaceIndex, 0, Surfaces.Length - 1)];

    public static float MuFor(EngineDesign d) => Mu * Surf.MuK * MathF.Pow(d.TireW / 90f, 0.3f) * RimFit(d);

    /// <summary>แรงต้านการกลิ้ง: ยางกว้างต้านมากขึ้น ขอบไม่พอดียิ่งต้านมาก</summary>
    public static float CrrFor(EngineDesign d) => Crr * Surf.CrrK * MathF.Pow(d.TireW / 90f, 0.25f) / (0.5f + 0.5f * RimFit(d));

    /// <summary>ความเฉื่อยล้อ: โตขึ้นตามรัศมียกกำลังสอง และหนักขึ้นตามหน้ายาง/ความกว้างขอบ</summary>
    public static float WheelInertiaFor(EngineDesign d)
    {
        float r = WheelRadius(d) / 0.285f;
        return WheelInertia * r * r * (0.7f + 0.3f * d.TireW / 80f) * (0.85f + 0.15f * d.RimW / 1.4f);
    }

    public static float Final(EngineDesign d) => d.SprocketR / d.SprocketF;

    /// <summary>อัตราทดรวมจากข้อเหวี่ยงถึงล้อ ที่เกียร์ gear (1..4) ของรถเกียร์ธรรมดา</summary>
    public static float Total(EngineDesign d, int gear) =>
        gear <= 0 ? 0f : d.Primary * GearOf(d, gear) * Final(d);

    public static float GearOf(EngineDesign d, int gear) => Math.Clamp(gear, 1, 4) switch { 1 => d.Gear1, 2 => d.Gear2, 3 => d.Gear3, _ => d.Gear4 };

    public static float Mass(EngineDesign d) => BikeKg(d) + d.RiderKg;

    /// <summary>มวลเทียบเท่า รวมความเฉื่อยของล้อ</summary>
    public static float EffMass(EngineDesign d)
    {
        float r = WheelRadius(d);
        return Mass(d) + WheelInertiaFor(d) / (r * r);
    }

    /// <summary>แรงต้านรวมที่ความเร็ว v (m/s) บนเนินลาดชัน slopePct (%) หน่วย N (บวก = ต้านการเดินหน้า)</summary>
    /// <summary>พื้นที่ลมตามขนาดรถ: รถใหญ่ (มีกระจกบังลม) ต้านลมมากกว่า</summary>
    public static float CdAFor(EngineDesign d) => d.CdA;

    public static float Resist(EngineDesign d, float v, float slopePct)
    {
        float m = Mass(d);
        float s = slopePct / 100f;
        float sin = s / MathF.Sqrt(1f + s * s), cos = 1f / MathF.Sqrt(1f + s * s);
        return CrrFor(d) * m * G * cos + 0.5f * AirRho * CdAFor(d) * v * MathF.Abs(v) + m * G * sin;
    }

    /// <summary>กิโลเมตร/ชม. ที่ rpm ในเกียร์ gear (รถเกียร์ธรรมดา)</summary>
    public static float KmhAt(EngineDesign d, int gear, float rpm) =>
        rpm / 9.5493f / Total(d, gear) * WheelRadius(d) * 3.6f;

    /// <summary>ความเร็วสูงสุดที่เครื่องลากไหว (เปิดคันเร่งสุด ทางราบ) ที่เกียร์ gear; คืน (km/h, rpm)</summary>
    public static (float Kmh, float Rpm) TopSpeed(EngineModel m, int gear, float slopePct = 0f)
    {
        var d = m.D;
        float ratio = Total(d, gear);
        float r = WheelRadius(d);
        float best = 0f, bestRpm = 0f;
        for (float v = 1f; v < 80f; v += 0.1f)
        {
            float rpm = v / r * ratio * 9.5493f;
            if (rpm > d.Limiter) break;
            if (rpm < 2500f) continue;
            float fav = MathF.Max(0f, m.BrakeTorqueWot(rpm)) * ratio * DriveEff / r;
            if (fav >= Resist(d, v, slopePct)) { best = v; bestRpm = rpm; }
        }
        return (best * 3.6f, bestRpm);
    }

    /// <summary>ความเร็วสูงสุดของรถออโต้: ชุดขับหน้าถือรอบไว้ที่ HoldRpm จนอัตราทดสูงสุด หลังจากนั้นรอบเพิ่มตามความเร็ว</summary>
    public static (float Kmh, float Rpm) TopSpeedAuto(EngineModel m, float slopePct = 0f)
    {
        var d = m.D;
        float r = WheelRadius(d);
        float hold = Cvt.HoldRpm(m);
        float best = 0f, bestRpm = 0f;
        for (float v = 1f; v < 80f; v += 0.1f)
        {
            float rpmHi = v / r * Cvt.Ratio(1f) * Cvt.Final(d) * 9.5493f;       // รอบที่อัตราทดสูงสุด
            float rpm = MathF.Max(hold, rpmHi);
            if (rpm > d.Limiter) break;
            float ratio = rpm / 9.5493f * r / v;                                    // อัตราทดรวมที่ใช้
            float fav = MathF.Max(0f, m.BrakeTorqueWot(rpm)) * ratio * DriveEff / r;
            if (fav >= Resist(d, v, slopePct)) { best = v; bestRpm = rpm; }
        }
        return (best * 3.6f, bestRpm);
    }
}

/// <summary>
/// ชุดขับหน้า CVT (ตัวแปรความเร็ว) ของรถออโต้
/// แรงเหวี่ยงของเม็ดดันฝาเลื่อนผ่านชามเม็ด: แรงตามแกน = มวลเม็ดรวม × ω² × รัศมีเม็ด / tan(องศาชาม)
/// ถูกถ่วงด้วยแรงหนีบของชุดขับหลัง (สปริง + แรงจากแรงบิด) ตำแหน่ง s: 0 = อัตราทดต่ำสุด (ออกตัว), 1 = สูงสุด (ปลาย)
/// </summary>
public static class Cvt
{
    public const float RLow = 2.50f, RHigh = 0.77f;       // อัตราทดสายพาน
    public const float R0 = 0.032f, R1 = 0.050f;          // รัศมีเม็ดที่ตำแหน่ง s = 0, 1 (m)
    public const float FBase = 1000f;                     // แรงหนีบพื้นฐานของชุดขับหลังที่ไม่ขึ้นกับสปริงทอค (N)
    public const float SpringPreloadMm = 25f;             // สปริงทอคถูกบีบไว้ก่อน (มม.)
    public const float SpringTravelMm = 35f;              // ระยะบีบเพิ่มตอนอัตราทดไล่ถึงสูงสุด (มม.)
    public const float KCam = 250f;                       // แรงหนีบเพิ่มต่อแรงบิด (N ต่อ N·m) จากเดือยกดแรงบิด
    public const float FScale = 1500f;                    // แรงไม่สมดุลที่ทำให้ขยับเต็มอัตรา (N)
    public const float Rate = 1.5f;                       // ความเร็วขยับสูงสุดของ s (ต่อวินาที)

    /// <summary>อัตราทดสายพานที่ตำแหน่ง s (เปลี่ยนแบบเรขาคณิต)</summary>
    public static float Ratio(float s) => RLow * MathF.Pow(RHigh / RLow, Math.Clamp(s, 0f, 1f));

    /// <summary>มวลเม็ดรวม (kg): เม็ดตำแหน่งคี่ใช้น้ำหนักหลัก, เม็ดตำแหน่งคู่ใช้น้ำหนักสลับ</summary>
    public static float Mass(EngineDesign d)
    {
        int n = d.Rollers;
        int a = (n + 1) / 2, b = n / 2;
        return (a * d.RollerG + b * d.RollerG2) / 1000f;
    }

    /// <summary>แรงตามแกนที่เม็ดดันฝาเลื่อน (N) ที่ความเร็วเชิงมุมเครื่อง omega (rad/s)</summary>
    public static float Ff(EngineDesign d, float omega, float s)
    {
        float tanA = MathF.Tan(d.RampDeg * MathF.PI / 180f);
        return Mass(d) * omega * omega * (R0 + (R1 - R0) * s) / tanA;
    }

    /// <summary>ทดรวมหลังสายพาน: ลดขั้นแรกตายตัว x เฟืองท้าย (ฟันหลัง/ฟันหน้า)</summary>
    public static float Final(EngineDesign d) => d.FirstRed * d.GearR / d.GearF;

    /// <summary>แรงหนีบของชุดขับหลัง (N): พื้นฐาน + สปริงทอค (ความแข็ง × ระยะบีบ) + แรงจากแรงบิดผ่านคลัตช์</summary>
    public static float Fr(EngineDesign d, float torque, float s) =>
        FBase + d.SpringK * (SpringPreloadMm + SpringTravelMm * s) + KCam * MathF.Max(0f, torque);

    /// <summary>
    /// รอบที่ชุดขับถือไว้ตอนเปิดคันเร่งสุด (ตำแหน่งกลาง s = 0.5): หารอบที่แรงเหวี่ยงสมดุลกับแรงหนีบ
    /// เทียบกับรอบพีคกำลังของเครื่อง: ถือต่ำกว่า = เครื่องไม่ได้อยู่ที่รอบแรงสุด
    /// </summary>
    public static float HoldRpm(EngineModel m) => HoldRpm(m, m.D);

    /// <summary>รอบที่ CVT ถือ เมื่อใช้ชุดขับตามแบบ d (ใช้กับเครื่อง m) เพื่อลองน้ำหนักเม็ดต่าง ๆ</summary>
    public static float HoldRpm(EngineModel m, EngineDesign d)
    {
        const float s = 0.5f;
        for (float rpm = 2500f; rpm < EngineModel.MaxRpm; rpm += 50f)
        {
            float omega = rpm / 9.5493f;
            float t = MathF.Max(0f, m.BrakeTorqueWot(rpm));
            if (Ff(d, omega, s) >= Fr(d, t, s)) return rpm;
        }
        return EngineModel.MaxRpm;
    }

    /// <summary>
    /// น้ำหนักเม็ด (กรัม ใช้เท่ากันทุกเม็ด) ที่ทำให้ CVT ถือรอบใกล้รอบกำลังสูงสุดของเครื่องที่สุด
    /// คืน (น้ำหนัก, รอบที่ถือ) ถ้าถึงขอบเขต 4-20 กรัมแล้วยังไม่ถึง จะคืนค่าที่ใกล้สุด
    /// </summary>
    public static (float G, float Hold) SuggestRoller(EngineModel m)
    {
        float target = m.PeakPsRpm * 0.97f;
        float bestG = m.D.RollerG, bestHold = HoldRpm(m), bestErr = float.MaxValue;
        var d = m.D.Clone();
        for (float g = 4f; g <= 20f; g += 0.1f)
        {
            d.RollerG = g;
            d.RollerG2 = g;
            float hold = HoldRpm(m, d);
            float err = MathF.Abs(hold - target);
            if (err < bestErr) { bestErr = err; bestG = g; bestHold = hold; }
        }
        return (MathF.Round(bestG * 2f) / 2f, bestHold);
    }
}
