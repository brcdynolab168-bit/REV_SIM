// ตัดพัลส์ไอเสียทุกช่วงรอบจากคลิป: หาตำแหน่งพัลส์ด้วย DP ตามคาบที่เปลี่ยนช้า ๆ แล้วจัดแนวด้วย template ท้องถิ่น
// args: ref.wav outDir      (segments กำหนดในโค้ด: เวลา, ช่วงรอบ, ภาระ)
string inPath = args[0], outDir = args[1];
float[] all = Wav.Read(inPath, out int fs);

// (ชื่อ, เริ่ม, จบ, rpmต่ำสุด, rpmสูงสุด, load: 1 = เปิดคันเร่ง, 0 = ปิดคันเร่ง/เบรกเครื่อง)
var segs = new (string n, double a, double b, int load, (double t, double rpm)[] kf)[]
{
    ("B", 0.05, 1.72, 0, new[] { (0.0, 12300.0), (0.5, 11500.0), (0.75, 9900.0), (1.25, 8100.0), (1.75, 6240.0) }),
    ("C1", 1.78, 2.40, 1, new[] { (1.75, 6240.0), (2.0, 8550.0), (2.25, 10300.0), (2.5, 11430.0) }),
    ("C2", 2.40, 2.98, 1, new[] { (2.25, 10300.0), (2.5, 11430.0), (2.75, 11430.0), (3.0, 12630.0) }),
    ("D", 3.00, 5.85, 1, new[] { (3.0, 12630.0), (3.5, 13000.0), (4.5, 13300.0), (5.85, 13820.0) }),
};

// ตัด DC / ลมต่ำ
float[] x = (float[])all.Clone();
{
    float a = 1f - MathF.Exp(-2f * MathF.PI * 30f / fs), z = 0;
    for (int i = 0; i < x.Length; i++) { z += a * (x[i] - z); x[i] -= z; }
}
float[] lp = Zero(x, 350f, fs);

int segId = 0;
var info = new List<string>();
foreach (var sg in segs)
{
    int s0 = (int)(sg.a * fs), s1 = (int)(sg.b * fs);
    double hiR = sg.kf.Max(k => k.rpm), loR = sg.kf.Min(k => k.rpm);
    int pMin = (int)(fs * 120.0 / hiR * 0.7), pMax = (int)(fs * 120.0 / loR * 1.4);
    double ExpP(int sample) { double t = sample / (double)fs; double r = sg.kf[0].rpm; for (int q = 0; q + 1 < sg.kf.Length; q++) if (t >= sg.kf[q].t) { double u = Math.Clamp((t - sg.kf[q].t) / (sg.kf[q + 1].t - sg.kf[q].t), 0, 1); r = sg.kf[q].rpm + (sg.kf[q + 1].rpm - sg.kf[q].rpm) * u; } if (t >= sg.kf[^1].t) r = sg.kf[^1].rpm; return fs * 120.0 / r; }
    // ยอด (peak) ผู้สมัคร
    var peaks = new List<int>();
    int hw = Math.Max(4, pMin / 3);
    for (int i = s0 + hw; i < s1 - hw; i++)
    {
        float v = lp[i];
        if (v <= 0) continue;
        bool ok = true;
        for (int k = -hw; k <= hw && ok; k++) if (lp[i + k] > v) ok = false;
        if (ok) peaks.Add(i);
    }
    // ความแรงสัมพัทธ์
    var str = new float[peaks.Count];
    for (int i = 0; i < peaks.Count; i++)
    {
        float mx = 1e-6f;
        for (int j = Math.Max(0, i - 40); j < Math.Min(peaks.Count, i + 40); j++) mx = Math.Max(mx, lp[peaks[j]]);
        str[i] = lp[peaks[i]] / mx;
    }
    // DP บนคู่ (j -> i)
    int n = peaks.Count;
    var best = new Dictionary<(int, int), (double score, (int, int) prev)>();
    const double Mu = 7.0;
    for (int i = 0; i < n; i++)
    {
        for (int j = i - 1; j >= 0; j--)
        {
            int p = peaks[i] - peaks[j];
            if (p > pMax) break;
            if (p < pMin) continue;
            double ep = ExpP((peaks[i] + peaks[j]) / 2);
            if (p < ep * 0.78 || p > ep * 1.28) continue;
            double sc = str[j] + str[i];
            double bestPrev = double.NegativeInfinity;
            (int, int) bp = (-1, -1);
            for (int h = j - 1; h >= 0; h--)
            {
                int p0 = peaks[j] - peaks[h];
                if (p0 > pMax) break;
                if (p0 < pMin) continue;
                if (!best.TryGetValue((h, j), out var pv)) continue;
                double c = pv.score - Mu * Math.Abs(Math.Log((double)p / p0)) + str[i];
                if (c > bestPrev) { bestPrev = c; bp = (h, j); }
            }
            if (bp.Item1 >= 0 && bestPrev > sc) best[(j, i)] = (bestPrev, bp);
            else best[(j, i)] = (sc, (-1, -1));
        }
    }
    if (best.Count == 0) { Console.WriteLine($"seg {sg.n}: no path"); continue; }
    var end = best.OrderByDescending(k => k.Value.score).First().Key;
    var path = new List<int>();
    var cur = end;
    while (true)
    {
        path.Add(peaks[cur.Item2]);
        var pv = best[cur].prev;
        if (pv.Item1 < 0) { path.Add(peaks[cur.Item1]); break; }
        cur = pv;
    }
    path.Reverse();
    var marks = path.Distinct().OrderBy(v => v).ToList();

    // จัดแนวด้วย template ท้องถิ่น (สัญญาณเต็มย่าน ตัดช่วงสั้นรอบยอดพัลส์)
    for (int iter = 0; iter < 3; iter++)
    {
        var nm = new List<int>(marks.Count);
        for (int k = 0; k < marks.Count; k++)
        {
            int P = k + 1 < marks.Count ? marks[k + 1] - marks[k] : marks[k] - marks[k - 1];
            int pre = (int)(P * 0.25), post = (int)(P * 0.35);
            var T = new float[pre + post];
            int cnt = 0;
            for (int q = Math.Max(0, k - 12); q <= Math.Min(marks.Count - 1, k + 12); q++)
            {
                if (q == k) continue;
                int c = marks[q];
                if (c - pre < 0 || c + post >= x.Length) continue;
                for (int j = 0; j < T.Length; j++) T[j] += x[c - pre + j];
                cnt++;
            }
            if (cnt == 0) { nm.Add(marks[k]); continue; }
            int bestC = marks[k];
            double bv = double.MinValue;
            int rng = Math.Max(3, P / 6);
            for (int c = marks[k] - rng; c <= marks[k] + rng; c++)
            {
                if (c - pre < 0 || c + post >= x.Length) continue;
                double a = 0, e = 0;
                for (int j = 0; j < T.Length; j++) { float v = x[c - pre + j]; a += v * T[j]; e += v * v; }
                double sc = a / Math.Sqrt(e + 1e-12);
                if (sc > bv) { bv = sc; bestC = c; }
            }
            nm.Add(bestC);
        }
        marks = nm.Distinct().OrderBy(v => v).ToList();
    }

    var per = marks.Zip(marks.Skip(1), (a, b) => b - a).ToArray();
    double ss = 0;
    int m0 = marks[0], m1 = marks[^1];
    for (int i = m0; i < m1; i++) ss += x[i] * x[i];
    double rms = Math.Sqrt(ss / Math.Max(1, m1 - m0));
    double mean = per.Average();
    double jit = Math.Sqrt(per.Select(p => (p - mean) * (p - mean)).Average());
    var rp = new List<string>();
    for (int q = 0; q < 6; q++)
    {
        int k = Math.Min(per.Length - 1, (int)(q / 5.0 * (per.Length - 1)));
        rp.Add($"{fs / (double)per[k] * 120:0}");
    }
    Console.WriteLine($"seg {sg.n} {sg.a}-{sg.b}s: {marks.Count} pulses, period {per.Min()}-{per.Max()} (rpm {string.Join(" ", rp)}) jitter {jit:0.0} rms {rms:0.0000}");
    info.Add($"seg {segId} {sg.load} {rms:0.00000}");
    foreach (var mk in marks) info.Add(mk.ToString());
    segId++;
}

Directory.CreateDirectory(outDir);
int endS = (int)(9.3 * fs);
float[] y = x[..Math.Min(endS, x.Length)];
float pk = y.Max(MathF.Abs);
for (int i = 0; i < y.Length; i++) y[i] *= 0.95f / pk;
Wav.Write(Path.Combine(outDir, "engine_ref.wav"), y, fs);
File.WriteAllText(Path.Combine(outDir, "engine_ref.marks"),
    "# multi-segment pulse marks: 'seg <id> <load 1|0> <rms>' then sample indices\n" + string.Join("\n", info));
Console.WriteLine("written");

static float[] Zero(float[] x, float hz, int fs)
{
    var y = (float[])x.Clone();
    for (int pass = 0; pass < 2; pass++)
    {
        float a = 1f - MathF.Exp(-2f * MathF.PI * hz / fs), z = 0;
        for (int i = 0; i < y.Length; i++) { z += a * (y[i] - z); y[i] = z; }
        z = 0;
        for (int i = y.Length - 1; i >= 0; i--) { z += a * (y[i] - z); y[i] = z; }
    }
    return y;
}

static class Wav
{
    public static float[] Read(string p, out int fs)
    {
        var b = File.ReadAllBytes(p);
        int pos = 12; fs = 44100; int ch = 1;
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int len = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ") { ch = BitConverter.ToInt16(b, pos + 10); fs = BitConverter.ToInt32(b, pos + 12); }
            if (id == "data")
            {
                int n = Math.Min(len, b.Length - pos - 8) / 2 / ch;
                var r = new float[n];
                for (int i = 0; i < n; i++) r[i] = BitConverter.ToInt16(b, pos + 8 + i * 2 * ch) / 32768f;
                return r;
            }
            pos += 8 + len + (len & 1);
        }
        throw new Exception("no data");
    }

    public static void Write(string path, float[] d, int fs)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + d.Length * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(fs); w.Write(fs * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(d.Length * 2);
        foreach (var v in d) w.Write((short)Math.Clamp(v * 32767f, -32768f, 32767f));
    }
}
