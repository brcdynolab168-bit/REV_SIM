using System.Drawing.Drawing2D;

namespace RevGame;

internal static class Palette
{
    public static readonly Color Letterbox = Color.FromArgb(4, 5, 7);
    public static readonly Color Bg = Color.FromArgb(11, 13, 16);
    public static readonly Color BgTop = Color.FromArgb(17, 20, 25);
    public static readonly Color Panel = Color.FromArgb(20, 23, 28);
    public static readonly Color PanelHi = Color.FromArgb(31, 36, 44);
    public static readonly Color Border = Color.FromArgb(50, 57, 67);
    public static readonly Color Accent = Color.FromArgb(255, 163, 26);
    public static readonly Color Red = Color.FromArgb(228, 62, 56);
    public static readonly Color Green = Color.FromArgb(72, 204, 124);
    public static readonly Color Blue = Color.FromArgb(70, 186, 236);
    public static readonly Color Yellow = Color.FromArgb(238, 196, 64);
    public static readonly Color Text = Color.FromArgb(228, 232, 238);
    public static readonly Color Dim = Color.FromArgb(122, 131, 144);
}

/// <summary>พื้นที่คลิกที่ลงทะเบียนตอนวาดแต่ละเฟรม (พิกัดเสมือน 1920x1080)</summary>
internal sealed class Hit
{
    public RectangleF R;
    public Action<PointF>? Down;
    public Action<PointF>? Drag;
    public Action? Up;
    public Action<int>? Wheel;
}

public sealed partial class MainForm
{
    // ---------- ฟอนต์ (หน่วยพิกเซลเสมือน) ----------
    private static Font Fnt(float px, bool bold = false, string family = "Leelawadee UI") =>
        new(family, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    private readonly Font _fXs = Fnt(19);
    private readonly Font _fSm = Fnt(22);
    private readonly Font _fMd = Fnt(26);
    private readonly Font _fLg = Fnt(34);
    private readonly Font _fBtn = Fnt(28, true);
    private readonly Font _fTitle = Fnt(46, true);
    private readonly Font _fHuge = Fnt(150, true);
    private readonly Font _fGaugeNum = Fnt(40, true, "Consolas");
    private readonly Font _fRpm = Fnt(110, true, "Consolas");
    private readonly Font _fSpeed = Fnt(84, true, "Consolas");
    private readonly Font _fNum = Fnt(30, true, "Consolas");
    private readonly Font _fNumL = Fnt(72, true, "Consolas");
    private readonly Font _fNumS = Fnt(22, true, "Consolas");
    private readonly Font _fCap = Fnt(17, true);

    private readonly Dictionary<int, SolidBrush> _brushes = new();
    private readonly Dictionary<(int, int), Pen> _pens = new();
    private static readonly StringFormat SfC = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    private static readonly StringFormat SfR = new() { Alignment = StringAlignment.Far };

    private SolidBrush Br(Color c)
    {
        int k = c.ToArgb();
        if (!_brushes.TryGetValue(k, out var b)) _brushes[k] = b = new SolidBrush(c);
        return b;
    }

    private Pen Pn(Color c, float w)
    {
        var k = (c.ToArgb(), (int)(w * 10));
        if (!_pens.TryGetValue(k, out var p)) _pens[k] = p = new Pen(c, w);
        return p;
    }

    private void DisposeUi()
    {
        foreach (var b in _brushes.Values) b.Dispose();
        foreach (var p in _pens.Values) p.Dispose();
        _bgBrush?.Dispose();
        _bgBmp?.Dispose();
        _gaugeBmp?.Dispose();
        foreach (var f in new[] { _fXs, _fSm, _fMd, _fLg, _fBtn, _fTitle, _fHuge, _fGaugeNum, _fRpm, _fSpeed, _fNum, _fNumL, _fNumS, _fCap }) f.Dispose();
    }

    // ---------- พื้นที่คลิก ----------
    private readonly List<Hit> _hits = new();
    private Hit? _drag;
    private PointF _mouseV;
    private float _scale = 1f, _offX, _offY;

    private PointF ToV(Point p) => new((p.X - _offX) / _scale, (p.Y - _offY) / _scale);

    private void AddHit(RectangleF r, Action<PointF>? down = null, Action<PointF>? drag = null, Action? up = null, Action<int>? wheel = null) =>
        _hits.Add(new Hit { R = r, Down = down, Drag = drag, Up = up, Wheel = wheel });

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var p = ToV(e.Location);
        _mouseV = p;
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            var h = _hits[i];
            if (!h.R.Contains(p)) continue;
            _drag = h.Drag != null || h.Up != null ? h : null;
            h.Down?.Invoke(p);
            break;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _mouseV = ToV(e.Location);
        _drag?.Drag?.Invoke(_mouseV);
        Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _drag?.Up?.Invoke();
        _drag = null;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var p = ToV(e.Location);
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            var h = _hits[i];
            if (h.Wheel == null || !h.R.Contains(p)) continue;
            h.Wheel(e.Delta > 0 ? 1 : -1);
            break;
        }
        base.OnMouseWheel(e);
    }

    // ---------- วาดพื้นฐาน ----------
    private LinearGradientBrush? _bgBrush;
    private Bitmap? _bgBmp;

    /// <summary>วางพื้นหลัง (ไล่สี + ขอบดำ) ที่แคชไว้เป็นภาพ ก่อนตั้งค่าสเกล เรียกซ้ำแทบไม่เสียเวลา</summary>
    private void DrawBackgroundCached(Graphics g, int cw, int ch)
    {
        if (_bgBmp == null || _bgBmp.Width != cw || _bgBmp.Height != ch)
        {
            _bgBmp?.Dispose();
            _bgBmp = new Bitmap(Math.Max(cw, 1), Math.Max(ch, 1), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using var gg = Graphics.FromImage(_bgBmp);
            gg.Clear(Palette.Letterbox);
            float s = Math.Min(cw / VW, ch / VH);
            gg.TranslateTransform((cw - VW * s) / 2f, (ch - VH * s) / 2f);
            gg.ScaleTransform(s, s);
            _bgBrush ??= new LinearGradientBrush(new RectangleF(0, 0, VW, VH), Palette.BgTop, Palette.Bg, LinearGradientMode.Vertical);
            gg.FillRectangle(_bgBrush, 0, 0, VW, VH);
            using var gp = new Pen(Color.FromArgb(9, 255, 255, 255), 1f);
            for (float x = 0; x <= VW; x += 48f) gg.DrawLine(gp, x, 0, x, VH);
            for (float y = 0; y <= VH; y += 48f) gg.DrawLine(gp, 0, y, VW, y);
        }
        g.DrawImageUnscaled(_bgBmp, 0, 0);
    }

    private static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private void FillRR(Graphics g, RectangleF r, float rad, Color c)
    {
        if (rad <= 0.5f) { g.FillRectangle(Br(c), r); return; }
        // เติมสีแบบไม่ลบรอยหยัก (เร็วกว่ามาก) เส้นขอบ DrawRR ที่ลบรอยหยักบังขอบเอง
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.HighSpeed;
        using var p = RoundRect(r, rad);
        g.FillPath(Br(c), p);
        g.SmoothingMode = old;
    }

    private void DrawRR(Graphics g, RectangleF r, float rad, Color c, float w = 2f)
    {
        using var p = RoundRect(r, rad);
        g.DrawPath(Pn(c, w), p);
    }

    private void Txt(Graphics g, string s, Font f, Color c, float x, float y) => g.DrawString(s, f, Br(c), x, y);

    private void TxtC(Graphics g, string s, Font f, Color c, float cx, float cy)
    {
        g.DrawString(s, f, Br(c), new RectangleF(cx - 1000, cy - 100, 2000, 200), SfC);
    }

    private void TxtR(Graphics g, string s, Font f, Color c, float rx, float y)
    {
        g.DrawString(s, f, Br(c), new RectangleF(rx - 1200, y, 1200, 120), SfR);
    }

    // ---------- วิดเจ็ต ----------
    private void PanelBox(Graphics g, RectangleF r, string? title = null)
    {
        FillRR(g, r, 4, Palette.Panel);
        DrawRR(g, r, 4, Palette.Border, 1f);
        if (title != null)
        {
            g.FillRectangle(Br(Palette.Accent), r.X + 16, r.Y + 17, 4, 18);
            Txt(g, title, _fSm, Palette.Dim, r.X + 28, r.Y + 12);
        }
    }

    /// <summary>ปุ่มเรียบแบบแผงเครื่องมือ: active = เติมสี, ปกติ = กรอบบาง</summary>
    private void Btn(Graphics g, RectangleF r, string text, bool active, Color col, Action? click,
        Font? font = null, Action? down = null, Action? up = null)
    {
        bool hov = r.Contains(_mouseV);
        Color back = active ? col : (hov ? Palette.PanelHi : Palette.Panel);
        FillRR(g, r, 4, back);
        DrawRR(g, r, 4, active ? col : (hov ? Palette.Accent : Palette.Border), hov ? 2f : 1f);
        TxtC(g, text, font ?? _fBtn, active ? Color.FromArgb(14, 14, 16) : Palette.Text, r.X + r.Width / 2, r.Y + r.Height / 2);
        if (click != null || down != null || up != null)
            AddHit(r, down: _ => { down?.Invoke(); click?.Invoke(); }, up: up);
    }

    private void SmallBtn(Graphics g, RectangleF r, string text, Action click) => Btn(g, r, text, false, Palette.Accent, click, _fBtn);

    private void Bar(Graphics g, RectangleF r, float frac, Color col)
    {
        FillRR(g, r, 2, Color.FromArgb(40, 46, 56));
        float w = Math.Clamp(frac, 0f, 1f) * r.Width;
        if (w > 1f) FillRR(g, new RectangleF(r.X, r.Y, w, r.Height), 2, col);
    }

    /// <summary>แถบเลื่อนบาง มีขีดสเกล หัวเลื่อนเป็นแท่ง</summary>
    private void Slider(Graphics g, RectangleF r, float frac, Color col, Action<float> set)
    {
        float ty = r.Y + r.Height / 2;
        var track = new RectangleF(r.X, ty - 3, r.Width, 6);
        Bar(g, track, frac, col);
        var tick = Pn(Color.FromArgb(70, 78, 90), 1f);
        for (int i = 0; i <= 10; i++) g.DrawLine(tick, r.X + r.Width * i / 10f, ty + 7, r.X + r.Width * i / 10f, ty + (i % 5 == 0 ? 13 : 10));
        float kx = r.X + Math.Clamp(frac, 0f, 1f) * r.Width;
        g.FillRectangle(Br(Palette.Text), kx - 4, ty - 11, 8, 22);
        void Apply(PointF p) => set(Math.Clamp((p.X - r.X) / r.Width, 0f, 1f));
        AddHit(r, Apply, Apply, () => { });
    }

    // ---------- สโคปคลื่นเสียง ----------
    private void DrawScope(Graphics g, RectangleF r)
    {
        FillRR(g, r, 4, Color.FromArgb(14, 17, 21));
        DrawRR(g, r, 4, Palette.Border, 1f);
        Txt(g, "คลื่นเสียง", _fXs, Palette.Dim, r.X + 14, r.Y + 6);
        float[] scope = _eng.Scope;
        int len = scope.Length;
        int start = _eng.ScopeWritePos;                 // ตัวเก่าสุดอยู่ถัดจากตำแหน่งเขียน
        const int points = 600;
        var pts = new PointF[points];
        float mid = r.Y + r.Height / 2f;
        float amp = r.Height / 2f - 8f;
        for (int i = 0; i < points; i++)
        {
            int idx = (start + (int)((long)i * (len - 1) / (points - 1))) & (len - 1);
            pts[i] = new PointF(r.X + 8 + i * (r.Width - 16) / (points - 1f), mid - Math.Clamp(scope[idx], -1f, 1f) * amp);
        }
        g.DrawLines(Pn(Palette.Accent, 2f), pts);
    }

    // ---------- กราฟกำลัง/แรงบิด (HP / N·m) ----------
    /// <summary>
    /// กราฟแรงบิด/แรงม้า  เส้นทึบ = ค่าปัจจุบัน (หลัง)  เส้นประ = ค่าก่อนหน้า (tq0/hp0 ถ้ามี)
    /// แรเงาส่วนต่างกำลัง (เขียว = ดีขึ้น แดง = แย่ลง) + แถบผลต่างกำลังรายรอบด้านล่าง + สรุปพีคก่อน/หลัง
    /// </summary>
    private void DrawChart(Graphics g, RectangleF r, float[] rpm, float[] tq, float[] hp, float maxRpm, float[]? tq0 = null, float[]? hp0 = null)
    {
        FillRR(g, r, 4, Color.FromArgb(14, 17, 21));
        DrawRR(g, r, 4, Palette.Border, 1f);
        bool cmp = tq0 != null && hp0 != null;
        float top = 1f;
        for (int i = 0; i < rpm.Length; i++)
        {
            if (!float.IsNaN(tq[i])) top = MathF.Max(top, MathF.Max(tq[i], hp[i]));
            if (cmp && !float.IsNaN(tq0![i])) top = MathF.Max(top, MathF.Max(tq0[i], hp0![i]));
        }
        float xMax = MathF.Max(maxRpm, 4000f);
        float xMin = rpm.Length > 0 ? MathF.Floor(rpm[0] / 1000f) * 1000f : 0f;
        var grid = Pn(Color.FromArgb(36, 42, 51), 1f);
        float headH = cmp ? 62f : 40f, stripH = cmp ? 30f : 0f, gap = cmp ? 22f : 0f;
        float px = r.X + 56, pw = r.Width - 76, py = r.Y + headH, ph = r.Height - headH - stripH - gap - 34;
        float stepV = top > 120f ? 20f : top > 60f ? 10f : top > 25f ? 5f : 2f;
        while (ph / MathF.Max(1f, top / stepV) < 26f) stepV *= 2f;
        top = MathF.Ceiling(top * 1.12f / stepV) * stepV;
        float XS(float v) => px + (v - xMin) / (xMax - xMin) * pw;

        for (float k = MathF.Ceiling(xMin / 1000f) * 1000f; k <= xMax; k += 1000f)
        {
            float x = XS(k);
            g.DrawLine(k % 2000f == 0 ? Pn(Color.FromArgb(48, 55, 66), 1f) : grid, x, py, x, py + ph + gap + stripH);
            if (k % 2000f == 0 && x < r.Right - 120) TxtC(g, (k / 1000f).ToString("0"), _fXs, Palette.Dim, x, py + ph + gap + stripH + 16);
        }
        TxtR(g, "x1000 rpm", _fCap, Palette.Dim, r.Right - 14, py + ph + gap + stripH + 8);
        for (float v = 0; v <= top; v += stepV)
        {
            float y = py + ph - v / top * ph;
            g.DrawLine(grid, px, y, px + pw, y);
            TxtR(g, v.ToString("0"), _fXs, Palette.Dim, px - 6, y - 12);
        }
        float X(int i) => XS(rpm[i]);
        float Y(float v) => py + ph - Math.Clamp(v, 0f, top) / top * ph;

        if (cmp)       // แรเงาส่วนต่างของกำลัง
        {
            for (int i = 0; i + 1 < rpm.Length; i++)
            {
                if (float.IsNaN(hp[i]) || float.IsNaN(hp[i + 1]) || float.IsNaN(hp0![i]) || float.IsNaN(hp0[i + 1])) continue;
                float dAvg = (hp[i] - hp0[i] + hp[i + 1] - hp0[i + 1]) * 0.5f;
                g.FillPolygon(Br(Color.FromArgb(95, dAvg >= 0f ? Palette.Green : Palette.Red)),
                    new[] { new PointF(X(i), Y(hp0[i])), new PointF(X(i + 1), Y(hp0[i + 1])), new PointF(X(i + 1), Y(hp[i + 1])), new PointF(X(i), Y(hp[i])) });
            }
        }

        void Curve(float[] vals, Color c, bool dashed)
        {
            var pts = new List<PointF>();
            for (int i = 0; i < rpm.Length; i++) if (!float.IsNaN(vals[i])) pts.Add(new PointF(X(i), Y(vals[i])));
            if (pts.Count < 2) return;
            using var pen = new Pen(dashed ? Color.FromArgb(230, Mix(c, Color.White, 0.35f)) : c, dashed ? 2.6f : 3.6f) { LineJoin = LineJoin.Round };
            if (dashed) pen.DashPattern = new[] { 4f, 2.5f };
            g.DrawLines(pen, pts.ToArray());
        }
        if (cmp) { Curve(tq0!, Palette.Blue, true); Curve(hp0!, Palette.Accent, true); }
        Curve(tq, Palette.Blue, false);
        Curve(hp, Palette.Accent, false);

        int Peak(float[] v) { int bi = -1; for (int i = 0; i < v.Length; i++) if (!float.IsNaN(v[i]) && (bi < 0 || v[i] > v[bi])) bi = i; return bi; }
        void Mark(float[] v, Color c, float[]? v0)
        {
            int i = Peak(v); if (i < 0) return;
            if (cmp && v0 != null) { int j = Peak(v0); if (j >= 0) g.DrawEllipse(Pn(c, 2.4f), X(j) - 6, Y(v0[j]) - 6, 12, 12); }
            g.FillEllipse(Br(c), X(i) - 6, Y(v[i]) - 6, 12, 12);
            g.DrawEllipse(Pn(Color.White, 1.6f), X(i) - 6, Y(v[i]) - 6, 12, 12);
        }
        Mark(hp, Palette.Accent, hp0); Mark(tq, Palette.Blue, tq0);
        if (cmp)       // ป้ายบอกช่วงรอบที่กำลังดีขึ้น (เขียว ▲) / แย่ลง (แดง ▼)
        {
            var zs = ChartZones(rpm, hp, hp0);
            for (int k = 0; k < Math.Min(3, zs.Count); k++)
            {
                var z = zs[k];
                Color zc = z.Good ? Palette.Green : Palette.Red;
                float x0 = XS(z.Lo), x1 = XS(z.Hi), yl = py + 6 + k * 21;
                g.DrawLine(Pn(Color.FromArgb(200, zc), 2f), x0, yl + 19, x1, yl + 19);
                string zt = $"{(z.Good ? "▲" : "▼")} {(z.Peak >= 0 ? "+" : "")}{z.Peak:0.0} HP";
                TxtC(g, zt, _fCap, zc, Math.Clamp((x0 + x1) / 2f, px + 60, px + pw - 60), yl + 8);
            }
        }

        // แถบผลต่างกำลังรายรอบ
        if (cmp)
        {
            float sy = py + ph + gap, sh = stripH, mid = sy + sh / 2f, maxD = 0.5f;
            for (int i = 0; i < rpm.Length; i++) if (!float.IsNaN(hp[i]) && !float.IsNaN(hp0![i])) maxD = MathF.Max(maxD, MathF.Abs(hp[i] - hp0[i]));
            FillRR(g, new RectangleF(px, sy, pw, sh), 2, Color.FromArgb(22, 26, 32));
            g.DrawLine(Pn(Color.FromArgb(70, 78, 90), 1f), px, mid, px + pw, mid);
            float bw = MathF.Max(2f, pw / rpm.Length);
            for (int i = 0; i < rpm.Length; i++)
            {
                if (float.IsNaN(hp[i]) || float.IsNaN(hp0![i])) continue;
                float d = hp[i] - hp0[i], hgt = d / maxD * (sh / 2f - 1f);
                g.FillRectangle(Br(d >= 0f ? Palette.Green : Palette.Red), X(i) - bw / 2f, hgt >= 0f ? mid - hgt : mid, bw, MathF.Abs(hgt));
            }
            Txt(g, $"ผลต่างกำลังรายรอบ (±{maxD:0.0} HP)   เขียว = เพิ่ม  แดง = ลด", _fCap, Palette.Dim, px, sy - 20);
        }

        // คำอธิบาย + สรุปพีค
        void Sample(float x, float y, Color c, bool dashed)
        {
            using var pen = new Pen(dashed ? Mix(c, Color.White, 0.35f) : c, dashed ? 2.6f : 3.6f);
            if (dashed) pen.DashPattern = new[] { 4f, 2.5f };
            g.DrawLine(pen, x, y, x + 30, y);
        }
        if (!cmp)
        {
            Sample(px, r.Y + 17, Palette.Blue, false); Txt(g, "แรงบิด N·m", _fSm, Palette.Blue, px + 38, r.Y + 5);
            Sample(px + 200, r.Y + 17, Palette.Accent, false); Txt(g, "กำลัง HP", _fSm, Palette.Accent, px + 238, r.Y + 5);
            return;
        }
        Sample(px, r.Y + 17, Palette.Text, false); Txt(g, "เส้นทึบ = หลัง (กำลังแก้)", _fXs, Palette.Text, px + 38, r.Y + 6);
        Sample(px + 300, r.Y + 17, Palette.Text, true); Txt(g, "เส้นประ = ก่อน (ที่ติดตั้ง)", _fXs, Palette.Dim, px + 338, r.Y + 6);
        int ih = Peak(hp), ih0 = Peak(hp0!), it = Peak(tq), it0 = Peak(tq0!);
        if (ih >= 0 && ih0 >= 0 && it >= 0 && it0 >= 0)
        {
            float dh = hp[ih] - hp0![ih0], dt = tq[it] - tq0![it0];
            string a = $"HP {hp[ih]:0.0} ({(dh >= 0f ? "+" : "")}{dh:0.0}) @{rpm[ih]:N0}", b = $"N·m {tq[it]:0.0} ({(dt >= 0f ? "+" : "")}{dt:0.0}) @{rpm[it]:N0}";
            Txt(g, a, _fCap, Palette.Accent, px, r.Y + 38);
            Txt(g, b, _fCap, Palette.Blue, px + (pw > 560f ? 280 : 250), r.Y + 38);
        }
    }
}
