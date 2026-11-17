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
    private void DrawChart(Graphics g, RectangleF r, float[] rpm, float[] tq, float[] hp, float maxRpm)
    {
        FillRR(g, r, 4, Color.FromArgb(14, 17, 21));
        DrawRR(g, r, 4, Palette.Border, 1f);
        float top = 1f;
        for (int i = 0; i < rpm.Length; i++)
            if (!float.IsNaN(tq[i])) top = MathF.Max(top, MathF.Max(tq[i], hp[i]));
        float stepV = top > 120f ? 20f : top > 60f ? 10f : top > 25f ? 5f : 2f;
        top = MathF.Ceiling(top * 1.1f / stepV) * stepV;
        float xMax = MathF.Max(maxRpm, 4000f);
        var grid = Pn(Color.FromArgb(36, 42, 51), 1f);
        float px = r.X + 56, pw = r.Width - 76, py = r.Y + 40, ph = r.Height - 76;

        for (float k = 0; k <= xMax; k += 1000f)
        {
            float x = px + k / xMax * pw;
            g.DrawLine(k % 2000f == 0 ? Pn(Color.FromArgb(48, 55, 66), 1f) : grid, x, py, x, py + ph);
            if (k % 2000f == 0) TxtC(g, (k / 1000f).ToString("0"), _fXs, Palette.Dim, x, py + ph + 18);
        }
        TxtR(g, "x1000 rpm", _fXs, Palette.Dim, r.Right - 14, r.Y + 8);
        for (float v = 0; v <= top; v += stepV)
        {
            float y = py + ph - v / top * ph;
            g.DrawLine(grid, px, y, px + pw, y);
            TxtR(g, v.ToString("0"), _fXs, Palette.Dim, px - 6, y - 12);
        }

        void Curve(float[] vals, Color c)
        {
            var pts = new List<PointF>();
            for (int i = 0; i < rpm.Length; i++)
            {
                if (float.IsNaN(vals[i])) continue;
                pts.Add(new PointF(px + rpm[i] / xMax * pw, py + ph - Math.Clamp(vals[i], 0f, top) / top * ph));
            }
            if (pts.Count >= 2) g.DrawLines(Pn(c, 2.6f), pts.ToArray());
        }
        Curve(tq, Palette.Blue);
        Curve(hp, Palette.Accent);
        g.FillRectangle(Br(Palette.Blue), px, r.Y + 14, 22, 4);
        Txt(g, "แรงบิด N·m", _fSm, Palette.Blue, px + 30, r.Y + 6);
        g.FillRectangle(Br(Palette.Accent), px + 190, r.Y + 14, 22, 4);
        Txt(g, "กำลัง HP", _fSm, Palette.Accent, px + 220, r.Y + 6);
    }
}
