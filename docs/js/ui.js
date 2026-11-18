// กรอบวาดแบบเดียวกับเวอร์ชัน C# (GDI+ immediate mode): พิกัดเสมือน 1920x1080 ย่อ/ขยายพอดีจอ + พื้นที่คลิกที่ลงทะเบียนทุกเฟรม
export const VW = 1920, VH = 1080;
const rgb = (r, g, b, a = 1) => (a === 1 ? `rgb(${r},${g},${b})` : `rgba(${r},${g},${b},${a})`);
export const Palette = {
  Letterbox: rgb(4, 5, 7), Bg: rgb(11, 13, 16), BgTop: rgb(17, 20, 25), Panel: rgb(20, 23, 28), PanelHi: rgb(31, 36, 44),
  Border: rgb(50, 57, 67), Accent: rgb(255, 163, 26), Red: rgb(228, 62, 56), Green: rgb(72, 204, 124), Blue: rgb(70, 186, 236),
  Yellow: rgb(238, 196, 64), Text: rgb(228, 232, 238), Dim: rgb(122, 131, 144), Black: rgb(14, 14, 16), White: '#fff',
};
export const rgbf = rgb;

const SANS = '"Leelawadee UI","Noto Sans Thai","Sarabun",Tahoma,"Segoe UI",sans-serif';
const MONO = 'Consolas,"Inconsolata","Courier New",monospace';
const font = (px, bold = false, mono = false) => `${bold ? 'bold ' : ''}${px}px ${mono ? MONO : SANS}`;
export const Fonts = {
  xs: font(19), sm: font(22), md: font(26), lg: font(34), btn: font(28, true), title: font(46, true), huge: font(150, true),
  gaugeNum: font(40, true, true), rpm: font(110, true, true), speed: font(84, true, true), num: font(30, true, true),
  numL: font(72, true, true), numS: font(22, true, true), cap: font(17, true),
};
const px = f => +/(\d+)px/.exec(f)[1];

export class Ui {
  constructor(canvas) {
    this.cv = canvas; this.g = canvas.getContext('2d');
    this.hits = []; this.active = new Map(); this.mouse = { x: -1, y: -1 }; this.scale = 1; this.offX = 0; this.offY = 0;
    this.bg = null; this.bgKey = '';
    const ev = (t, f) => canvas.addEventListener(t, f, { passive: false });
    ev('pointerdown', e => this._down(e)); ev('pointermove', e => this._move(e)); ev('pointerup', e => this._up(e)); ev('pointercancel', e => this._up(e));
    ev('wheel', e => this._wheel(e)); ev('contextmenu', e => e.preventDefault());
  }
  toV(e) { const r = this.cv.getBoundingClientRect(); return { x: (e.clientX - r.left - this.offX) / this.scale, y: (e.clientY - r.top - this.offY) / this.scale }; }
  _down(e) {
    e.preventDefault();
    const p = this.toV(e); this.mouse = p;
    for (let i = this.hits.length - 1; i >= 0; i--) {
      const h = this.hits[i];
      if (!inRect(h.r, p)) continue;
      if (h.drag || h.up) { this.active.set(e.pointerId, h); try { this.cv.setPointerCapture(e.pointerId); } catch { /* ok */ } }
      h.down?.(p);
      break;
    }
  }
  _move(e) { const p = this.toV(e); this.mouse = p; const h = this.active.get(e.pointerId); h?.drag?.(p); }
  _up(e) { const h = this.active.get(e.pointerId); if (h) { this.active.delete(e.pointerId); h.up?.(); } }
  _wheel(e) {
    const p = this.toV(e);
    for (let i = this.hits.length - 1; i >= 0; i--) { const h = this.hits[i]; if (!h.wheel || !inRect(h.r, p)) continue; e.preventDefault(); h.wheel(e.deltaY < 0 ? 1 : -1); break; }
  }
  releaseAll() { for (const h of this.active.values()) h.up?.(); this.active.clear(); }
  hit(r, down, drag, up, wheel) { this.hits.push({ r, down, drag, up, wheel }); }

  /** เริ่มเฟรม: ปรับขนาดภาพ วาดพื้นหลัง แล้วตั้งสเกลเป็นพิกัดเสมือน */
  begin() {
    const cv = this.cv, dpr = Math.min(window.devicePixelRatio || 1, 2);
    const cssW = cv.clientWidth, cssH = cv.clientHeight;
    const W = Math.max(1, Math.round(cssW * dpr)), H = Math.max(1, Math.round(cssH * dpr));
    if (cv.width !== W || cv.height !== H) { cv.width = W; cv.height = H; }
    const g = this.g;
    g.setTransform(1, 0, 0, 1, 0, 0);
    const s = Math.min(W / VW, H / VH);
    this.scale = s / dpr; this.offX = (W - VW * s) / 2 / dpr; this.offY = (H - VH * s) / 2 / dpr;
    const key = `${W}x${H}`;
    if (key !== this.bgKey) {
      this.bgKey = key;
      const b = document.createElement('canvas'); b.width = W; b.height = H;
      const bg = b.getContext('2d');
      bg.fillStyle = Palette.Letterbox; bg.fillRect(0, 0, W, H);
      bg.translate((W - VW * s) / 2, (H - VH * s) / 2); bg.scale(s, s);
      const gr = bg.createLinearGradient(0, 0, 0, VH); gr.addColorStop(0, Palette.BgTop); gr.addColorStop(1, Palette.Bg);
      bg.fillStyle = gr; bg.fillRect(0, 0, VW, VH);
      bg.strokeStyle = 'rgba(255,255,255,0.035)'; bg.lineWidth = 1; bg.beginPath();
      for (let x = 0; x <= VW; x += 48) { bg.moveTo(x, 0); bg.lineTo(x, VH); }
      for (let y = 0; y <= VH; y += 48) { bg.moveTo(0, y); bg.lineTo(VW, y); }
      bg.stroke();
      this.bg = b;
    }
    g.drawImage(this.bg, 0, 0);
    g.setTransform(s, 0, 0, s, (W - VW * s) / 2, (H - VH * s) / 2);
    g.save(); g.beginPath(); g.rect(0, 0, VW, VH); g.clip(); g.restore();
    this.hits.length = 0;
    g.textBaseline = 'top'; g.lineJoin = 'round';
    return g;
  }

  // ---------- วาดพื้นฐาน ----------
  rr(r, rad) {
    const g = this.g, x = r.x, y = r.y, w = r.w, h = r.h;
    g.beginPath();
    if (rad <= 0.5) { g.rect(x, y, w, h); return; }
    g.moveTo(x + rad, y); g.arcTo(x + w, y, x + w, y + h, rad); g.arcTo(x + w, y + h, x, y + h, rad); g.arcTo(x, y + h, x, y, rad); g.arcTo(x, y, x + w, y, rad); g.closePath();
  }
  fillRR(r, rad, c) { this.rr(r, rad); this.g.fillStyle = c; this.g.fill(); }
  drawRR(r, rad, c, w = 2) { this.rr(r, rad); this.g.strokeStyle = c; this.g.lineWidth = w; this.g.stroke(); }
  line(x1, y1, x2, y2, c, w = 1) { const g = this.g; g.strokeStyle = c; g.lineWidth = w; g.beginPath(); g.moveTo(x1, y1); g.lineTo(x2, y2); g.stroke(); }
  rect(x, y, w, h, c) { this.g.fillStyle = c; this.g.fillRect(x, y, w, h); }
  txt(s, f, c, x, y) { const g = this.g; g.font = f; g.fillStyle = c; g.textAlign = 'left'; g.textBaseline = 'top'; g.fillText(s, x, y + px(f) * 0.06); }
  txtC(s, f, c, cx, cy) { const g = this.g; g.font = f; g.fillStyle = c; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText(s, cx, cy + px(f) * 0.04); g.textBaseline = 'top'; }
  txtR(s, f, c, rx, y) { const g = this.g; g.font = f; g.fillStyle = c; g.textAlign = 'right'; g.textBaseline = 'top'; g.fillText(s, rx, y + px(f) * 0.06); g.textAlign = 'left'; }
  /** ข้อความตัดบรรทัดในกรอบ (ภาษาไทยไม่มีเว้นวรรค ตัดตามความกว้างด้วย Intl.Segmenter ถ้ามี) */
  wrap(s, f, c, x, y, w, h = 1e9) {
    const g = this.g; g.font = f; g.fillStyle = c; g.textAlign = 'left'; g.textBaseline = 'top';
    const lh = px(f) * 1.3;
    let seg;
    try { seg = [...new Intl.Segmenter('th', { granularity: 'word' }).segment(s)].map(x => x.segment); } catch { seg = [...s]; }
    let line = '', yy = y;
    for (const word of seg) {
      const t = line + word;
      if (g.measureText(t).width > w && line) { g.fillText(line, x, yy + px(f) * 0.06); yy += lh; line = word.trimStart(); if (yy + lh > y + h) return yy; } else line = t;
    }
    if (line) { g.fillText(line, x, yy + px(f) * 0.06); yy += lh; }
    return yy;
  }
  measure(s, f) { this.g.font = f; return this.g.measureText(s).width; }

  // ---------- วิดเจ็ต ----------
  panelBox(r, title) {
    this.fillRR(r, 4, Palette.Panel); this.drawRR(r, 4, Palette.Border, 1);
    if (title != null) { this.rect(r.x + 16, r.y + 17, 4, 18, Palette.Accent); this.txt(title, Fonts.sm, Palette.Dim, r.x + 28, r.y + 12); }
  }
  btn(r, text, active, col, click, font = Fonts.btn, down, up) {
    const hov = inRect(r, this.mouse) && !('ontouchstart' in window && this.active.size === 0 && false);
    const back = active ? col : hov ? Palette.PanelHi : Palette.Panel;
    this.fillRR(r, 4, back);
    this.drawRR(r, 4, active ? col : hov ? Palette.Accent : Palette.Border, hov ? 2 : 1);
    this.txtC(text, font, active ? Palette.Black : Palette.Text, r.x + r.w / 2, r.y + r.h / 2);
    if (click || down || up) this.hit(r, () => { down?.(); click?.(); }, null, up);
  }
  smallBtn(r, text, click) { this.btn(r, text, false, Palette.Accent, click, Fonts.btn); }
  bar(r, frac, col) {
    this.fillRR(r, 2, 'rgb(40,46,56)');
    const w = Math.min(1, Math.max(0, frac)) * r.w;
    if (w > 1) this.fillRR({ x: r.x, y: r.y, w, h: r.h }, 2, col);
  }
  slider(r, frac, col, set) {
    const ty = r.y + r.h / 2;
    this.bar({ x: r.x, y: ty - 3, w: r.w, h: 6 }, frac, col);
    for (let i = 0; i <= 10; i++) this.line(r.x + r.w * i / 10, ty + 7, r.x + r.w * i / 10, ty + (i % 5 === 0 ? 13 : 10), 'rgb(70,78,90)', 1);
    const kx = r.x + Math.min(1, Math.max(0, frac)) * r.w;
    this.rect(kx - 4, ty - 11, 8, 22, Palette.Text);
    const apply = p => set(Math.min(1, Math.max(0, (p.x - r.x) / r.w)));
    this.hit(r, apply, apply, () => { });
  }
  scope(r, data, writePos) {
    this.fillRR(r, 4, 'rgb(14,17,21)'); this.drawRR(r, 4, Palette.Border, 1);
    this.txt('คลื่นเสียง', Fonts.xs, Palette.Dim, r.x + 14, r.y + 6);
    if (!data) return;
    const g = this.g, len = data.length, pts = 600, mid = r.y + r.h / 2, amp = r.h / 2 - 8;
    g.strokeStyle = Palette.Accent; g.lineWidth = 2; g.beginPath();
    for (let i = 0; i < pts; i++) {
      const idx = (writePos + Math.floor(i * (len - 1) / (pts - 1))) & (len - 1);
      const x = r.x + 8 + i * (r.w - 16) / (pts - 1), y = mid - Math.max(-1, Math.min(1, data[idx])) * amp;
      if (i === 0) g.moveTo(x, y); else g.lineTo(x, y);
    }
    g.stroke();
  }
  /**
   * กราฟแรงบิด/แรงม้า  เส้นทึบ = ค่าปัจจุบัน (หลัง)  เส้นประ = ค่าก่อนหน้า (base = {tq, hp} ถ้ามี)
   * แรเงาส่วนต่างกำลัง (เขียว = ดีขึ้น แดง = แย่ลง) + แถบผลต่างกำลังรายรอบด้านล่าง + สรุปพีคก่อน/หลัง
   */
  chart(r, rpm, tq, hp, maxRpm, base = null) {
    const g = this.g, cmp = !!base, tq0 = base?.tq, hp0 = base?.hp;
    this.fillRR(r, 4, 'rgb(14,17,21)'); this.drawRR(r, 4, Palette.Border, 1);
    let top = 1;
    for (let i = 0; i < rpm.length; i++) {
      if (!Number.isNaN(tq[i])) top = Math.max(top, tq[i], hp[i]);
      if (cmp && !Number.isNaN(tq0[i])) top = Math.max(top, tq0[i], hp0[i]);
    }
    const xMax = Math.max(maxRpm, 4000), xMin = rpm.length ? Math.floor(rpm[0] / 1000) * 1000 : 0, grid = 'rgb(36,42,51)', headH = cmp ? 62 : 40, stripH = cmp ? 30 : 0, gap = cmp ? 22 : 0;
    const px_ = r.x + 56, pw = r.w - 76, py = r.y + headH, ph = r.h - headH - stripH - gap - 28;
    let stepV = top > 120 ? 20 : top > 60 ? 10 : top > 25 ? 5 : 2;
    while (ph / Math.max(1, top / stepV) < 26) stepV *= 2;
    top = Math.ceil(top * 1.12 / stepV) * stepV;
    const XS = v => px_ + (v - xMin) / (xMax - xMin) * pw;
    for (let k = Math.ceil(xMin / 1000) * 1000; k <= xMax; k += 1000) {
      const x = XS(k);
      this.line(x, py, x, py + ph + gap + stripH, k % 2000 === 0 ? 'rgb(48,55,66)' : grid, 1);
      if (k % 2000 === 0) this.txtC(String(k / 1000), Fonts.xs, Palette.Dim, x, py + ph + gap + stripH + 14);
    }
    this.txtR('x1000 rpm', Fonts.cap, Palette.Dim, r.x + r.w - 14, py + ph + gap + stripH + 6);
    for (let v = 0; v <= top; v += stepV) {
      const y = py + ph - v / top * ph;
      this.line(px_, y, px_ + pw, y, grid, 1);
      this.txtR(String(v), Fonts.xs, Palette.Dim, px_ - 6, y - 12);
    }
    const X = i => XS(rpm[i]), Y = v => py + ph - Math.min(Math.max(v, 0), top) / top * ph;
    if (cmp) {
      for (let i = 0; i + 1 < rpm.length; i++) {
        if ([hp[i], hp[i + 1], hp0[i], hp0[i + 1]].some(Number.isNaN)) continue;
        g.fillStyle = (hp[i] - hp0[i] + hp[i + 1] - hp0[i + 1]) >= 0 ? 'rgba(72,204,124,0.37)' : 'rgba(228,62,56,0.37)';
        g.beginPath(); g.moveTo(X(i), Y(hp0[i])); g.lineTo(X(i + 1), Y(hp0[i + 1])); g.lineTo(X(i + 1), Y(hp[i + 1])); g.lineTo(X(i), Y(hp[i])); g.closePath(); g.fill();
      }
    }
    const light = c => (c === Palette.Blue ? 'rgb(150,215,245)' : c === Palette.Accent ? 'rgb(255,205,130)' : c);
    const curve = (vals, c, dashed) => {
      g.strokeStyle = dashed ? light(c) : c; g.lineWidth = dashed ? 2.6 : 3.6; g.lineJoin = 'round'; g.setLineDash(dashed ? [4, 2.5] : []);
      g.beginPath(); let n = 0;
      for (let i = 0; i < rpm.length; i++) {
        if (Number.isNaN(vals[i])) continue;
        if (n++ === 0) g.moveTo(X(i), Y(vals[i])); else g.lineTo(X(i), Y(vals[i]));
      }
      if (n >= 2) g.stroke();
      g.setLineDash([]);
    };
    if (cmp) { curve(tq0, Palette.Blue, true); curve(hp0, Palette.Accent, true); }
    curve(tq, Palette.Blue, false); curve(hp, Palette.Accent, false);
    const peak = v => { let bi = -1; for (let i = 0; i < v.length; i++) if (!Number.isNaN(v[i]) && (bi < 0 || v[i] > v[bi])) bi = i; return bi; };
    const mark = (v, c, v0) => {
      const i = peak(v); if (i < 0) return;
      const j = cmp ? peak(v0) : -1;
      if (j >= 0) { g.beginPath(); g.arc(X(j), Y(v0[j]), 6, 0, 7); g.strokeStyle = c; g.lineWidth = 2.4; g.stroke(); }
      g.beginPath(); g.arc(X(i), Y(v[i]), 6, 0, 7); g.fillStyle = c; g.fill(); g.strokeStyle = '#fff'; g.lineWidth = 1.6; g.stroke();
    };
    mark(hp, Palette.Accent, hp0); mark(tq, Palette.Blue, tq0);
    if (cmp) {      // แถบผลต่างกำลังรายรอบ
      const sy = py + ph + gap, sh = stripH, mid = sy + sh / 2;
      let maxD = 0.5; for (let i = 0; i < rpm.length; i++) if (!Number.isNaN(hp[i]) && !Number.isNaN(hp0[i])) maxD = Math.max(maxD, Math.abs(hp[i] - hp0[i]));
      this.rect(px_, sy, pw, sh, 'rgb(22,26,32)'); this.line(px_, mid, px_ + pw, mid, 'rgb(70,78,90)', 1);
      const bw = Math.max(2, pw / rpm.length);
      for (let i = 0; i < rpm.length; i++) {
        if (Number.isNaN(hp[i]) || Number.isNaN(hp0[i])) continue;
        const d = hp[i] - hp0[i], hgt = d / maxD * (sh / 2 - 1);
        this.rect(X(i) - bw / 2, hgt >= 0 ? mid - hgt : mid, bw, Math.abs(hgt), d >= 0 ? Palette.Green : Palette.Red);
      }
      this.txt(`ผลต่างกำลังรายรอบ (±${maxD.toFixed(1)} HP)   เขียว = เพิ่ม  แดง = ลด`, Fonts.cap, Palette.Dim, px_, sy - 20);
    }
    const sample = (x, y, c, dashed) => { g.strokeStyle = dashed ? light(c) : c; g.lineWidth = dashed ? 2.6 : 3.6; g.setLineDash(dashed ? [4, 2.5] : []); g.beginPath(); g.moveTo(x, y); g.lineTo(x + 30, y); g.stroke(); g.setLineDash([]); };
    if (!cmp) {
      sample(px_, r.y + 17, Palette.Blue, false); this.txt('แรงบิด N·m', Fonts.sm, Palette.Blue, px_ + 38, r.y + 5);
      sample(px_ + 200, r.y + 17, Palette.Accent, false); this.txt('กำลัง HP', Fonts.sm, Palette.Accent, px_ + 238, r.y + 5);
      return;
    }
    sample(px_, r.y + 17, Palette.Text, false); this.txt('เส้นทึบ = หลัง (กำลังแก้)', Fonts.xs, Palette.Text, px_ + 38, r.y + 6);
    sample(px_ + 300, r.y + 17, Palette.Text, true); this.txt('เส้นประ = ก่อน (ที่ติดตั้ง)', Fonts.xs, Palette.Dim, px_ + 338, r.y + 6);
    const ih = peak(hp), ih0 = peak(hp0), it = peak(tq), it0 = peak(tq0);
    if (ih >= 0 && ih0 >= 0 && it >= 0 && it0 >= 0) {
      const dh = hp[ih] - hp0[ih0], dt = tq[it] - tq0[it0], sg = v => (v >= 0 ? '+' : '') + v.toFixed(1);
      this.txt(`HP ${hp[ih].toFixed(1)} (${sg(dh)}) @${Math.round(rpm[ih]).toLocaleString()}`, Fonts.cap, Palette.Accent, px_, r.y + 38);
      this.txt(`N·m ${tq[it].toFixed(1)} (${sg(dt)}) @${Math.round(rpm[it]).toLocaleString()}`, Fonts.cap, Palette.Blue, px_ + (pw > 560 ? 280 : 250), r.y + 38);
    }
  }
}
export const inRect = (r, p) => p.x >= r.x && p.x < r.x + r.w && p.y >= r.y && p.y < r.y + r.h;
export const R = (x, y, w, h) => ({ x, y, w, h });
