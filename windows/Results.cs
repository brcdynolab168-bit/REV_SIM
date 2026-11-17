namespace RevGame;

/// <summary>ผลวิ่งไดโนหนึ่งครั้ง</summary>
public sealed class DynoResult
{
    public EngineModel Model = null!;
    public float[] Tq = Array.Empty<float>();   // N·m ทุก 100 rpm (NaN = ไม่มี)
    public float PeakPs, PeakPsRpm, PeakTq, PeakTqRpm;
    public bool Completed;                      // วิ่งจนจบโดยไม่ดับ

    public static DynoResult From(EngineModel m, float[] tq, bool failed)
    {
        var r = new DynoResult { Model = m, Tq = (float[])tq.Clone(), Completed = !failed };
        // เกลี่ยให้เรียบ (ค่าที่วัดได้กระเพื่อมตามรอบจุดระเบิด)
        var s = new float[r.Tq.Length];
        for (int i = 0; i < s.Length; i++)
        {
            float sum = 0; int n = 0;
            for (int j = Math.Max(0, i - 2); j <= Math.Min(s.Length - 1, i + 2); j++)
                if (!float.IsNaN(r.Tq[j])) { sum += r.Tq[j]; n++; }
            s[i] = float.IsNaN(r.Tq[i]) || n == 0 ? float.NaN : sum / n;
        }
        r.Tq = s;
        bool any = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (float.IsNaN(s[i])) continue;
            any = true;
            float rpm = i * 100f, ps = EngineModel.Ps(s[i], rpm);
            if (s[i] > r.PeakTq) { r.PeakTq = s[i]; r.PeakTqRpm = rpm; }
            if (ps > r.PeakPs) { r.PeakPs = ps; r.PeakPsRpm = rpm; }
        }
        if (!any) r.Completed = false;
        return r;
    }
}

/// <summary>สถิติของเครื่องชุดหนึ่ง (ตั้งแต่ประกอบจนถึงแก้แบบ) ใช้เทียบ ก่อน/หลัง แก้</summary>
public sealed class RunStats
{
    public float B060 = -1f, B100 = -1f, B402 = -1f;   // ดีสุดของชุดนี้ (วินาที, -1 = ยังไม่มี)
    public float Top, PeakRpm, Hp;                      // ท็อปสปีด (กม./ชม.), รอบสูงสุด, แรงม้าจากไดโน (0 = ยังไม่วัด)
    public bool Any => B060 > 0f || B100 > 0f || B402 > 0f || Top > 0f || Hp > 0f;

    public static void Best(ref float best, float v) { if (v > 0f && (best < 0f || v < best)) best = v; }
}

/// <summary>บันทึกแบบที่ประกอบล่าสุด + สถิติถนน ไว้ที่ %LocalAppData%\RevGame110\save.txt</summary>
public sealed class SaveData
{
    public EngineDesign Design = EngineDesign.Presets[0].D.Clone();
    public float Best060 = -1f, Best100 = -1f, Best402 = -1f, BestTop;   // สถิติถนน: 0-60 กม./ชม., 100 ม., 402 ม. (วินาที), ท็อปสปีด (กม./ชม.)
    public float Volume = 0.8f;                           // ระดับเสียงที่ตั้งไว้
    public bool Fullscreen = true;
    public bool ClutchSys = true;                         // ระบบครัตช์มือ (เปิด/ปิด)

    private static string PathName => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevGame110", "save.txt");

    private static string S(float v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static float F(string v, float fallback) =>
        float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r)
            ? r : fallback;

    public static SaveData Load()
    {
        var s = new SaveData();
        try
        {
            if (!File.Exists(PathName)) return s;
            foreach (var line in File.ReadAllLines(PathName))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line[..eq], v = line[(eq + 1)..];
                if (k == "design") s.Design = EngineDesign.Deserialize(v);
                else if (k == "best060") s.Best060 = F(v, -1f);
                else if (k == "best100") s.Best100 = F(v, -1f);
                else if (k == "best402") s.Best402 = F(v, -1f);
                else if (k == "volume") s.Volume = Math.Clamp(F(v, 0.8f), 0f, 1f);
                else if (k == "fullscreen") s.Fullscreen = v != "0";
                else if (k == "clutchsys") s.ClutchSys = v != "0";
                else if (k == "besttop") s.BestTop = F(v, 0f);
            }
        }
        catch
        {
            // ไฟล์เสีย: เริ่มใหม่
        }
        if (s.Best060 < 0.8f) s.Best060 = -1f;
        if (s.Best100 < 2f) s.Best100 = -1f;
        if (s.Best402 < 5f) s.Best402 = -1f;
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
            File.WriteAllLines(PathName, new[]
            {
                "design=" + Design.Serialize(),
                "best060=" + S(Best060),
                "best100=" + S(Best100),
                "best402=" + S(Best402),
                "volume=" + S(Volume),
                "fullscreen=" + (Fullscreen ? "1" : "0"),
                "clutchsys=" + (ClutchSys ? "1" : "0"),
                "besttop=" + S(BestTop),
            });
        }
        catch
        {
            // บันทึกไม่ได้ก็เล่นต่อได้
        }
    }
}
