// Rev Lab (เว็บ/มือถือ): หน้าจอเหมือนเวอร์ชัน C# (วาดบนผืน 1920x1080) + ลูปเกม
// เสียง/ฟิสิกส์อยู่ใน AudioWorklet: worklet.js + sim.js
import {
  Surfaces, EcuN, ecuLoadAt, InjDeadMs, EngineModel, Presets, Params, paramDisplay, nudge, GarageTabs, PartCats, partCurrent, Fuels, newDesign,
  Veh, Cvt, clamp, Hp, Ps, PsToHp, calibrate, tireCode, MaxRpm,
} from './engine.js';
import * as Adv from './advisor.js';
import { Ui, Palette as C, Fonts as F, VW, VH, R, inRect, rgbf, chartZones } from './ui.js';

const fmtN = v => Math.round(v).toLocaleString('en-US');
const f1 = v => v.toFixed(1), f0 = v => v.toFixed(0), f2 = v => v.toFixed(2);

// ================================================================= บันทึก
const KEY = 'revlab.web.v2';
const save = (() => {
  const s = { design: null, best: { b060: -1, b100: -1, b402: -1, top: 0 }, volume: 0.8, idleGain: 0.4, assist: matchMedia('(pointer:coarse)').matches, clutchSys: true, surface: 0, gaugeMode: 0 };
  try { Object.assign(s, JSON.parse(localStorage.getItem(KEY) || '{}')); } catch { /* เริ่มใหม่ */ }
  return s;
})();
let saveTimer = 0;
const persist = () => { clearTimeout(saveTimer); saveTimer = setTimeout(() => { try { localStorage.setItem(KEY, JSON.stringify(save)); } catch { /* ไม่เป็นไร */ } }, 250); };

Veh.setSurface(save.surface || 0);
let design = newDesign(save.design || {});
if (save.design && save.design.RampOutDeg === undefined) design.RampOutDeg = design.RampDeg;     // เซฟเก่า: ใบนอกเท่าใบใน
let draft = { ...design };
let installed = new EngineModel(design);
let preview = new EngineModel(draft);

// ================================================================= สถิติ
const newRun = () => ({ b060: -1, b100: -1, b402: -1, top: 0, peakRpm: 0, hp: 0 });
const runAny = r => r.b060 > 0 || r.b100 > 0 || r.b402 > 0 || r.top > 0 || r.hp > 0;
const bestOf = (cur, v) => (v > 0 && (cur < 0 || v < cur) ? v : cur);
let cur = newRun(), before = null;
let last060 = -1, last100 = -1, last402 = -1, trap402 = 0, topSession = 0, peakRpm = 0;
let runDist = -1, runTime = 0, runFull = true;      // ระยะ/เวลาที่ขับได้จากความเร็ว 0 (ค้างค่าไว้ถึงออกตัวรอบใหม่)
let driveTab = 0;      // การ์ดขวาของหน้าทดสอบ: 0 = คลัตช์/ยาง, 1 = ข้อมูลสด (Live data)
let dynoRes = null, dynoPrev = null;      // dynoPrev = ผลไดโนก่อนแก้ ใช้เทียบเส้นประในผลไดโน

// ================================================================= เสียง / worklet
let ctx = null, node = null, ready = false;
const S = { rpm: 0, thr: 0, cut: false, softK: 0, speed: 0, dist: 0, gear: 0, clutchLock: 0, cfLock: 0, lever: 0, cvtS: 0, running: false, ignition: true,
  dynoPhase: 0, dynoVersion: 0, dynoFailed: false, limiter: 9500, isAuto: false, scope: null, scopeW: 0, slipRpm: 0, tracUse: 0, spin: false, driveForce: 0, gripMax: 0 };
const sent = {};
function send(key, value) {
  if (!node || sent[key] === value) return;
  sent[key] = value;
  node.port.postMessage({ type: 'set', key, value });
}
function parseWav(buf) {
  const dv = new DataView(buf);
  let pos = 12, fs = 0, ch = 1;
  while (pos + 8 <= dv.byteLength) {
    const id = String.fromCharCode(dv.getUint8(pos), dv.getUint8(pos + 1), dv.getUint8(pos + 2), dv.getUint8(pos + 3));
    const len = dv.getInt32(pos + 4, true);
    if (id === 'fmt ') { ch = dv.getInt16(pos + 10, true); fs = dv.getInt32(pos + 12, true); }
    if (id === 'data') {
      const n = Math.floor(Math.min(len, dv.byteLength - pos - 8) / 2 / ch), x = new Float32Array(n);
      for (let i = 0; i < n; i++) x[i] = dv.getInt16(pos + 8 + i * 2 * ch, true) / 32768;
      return { x, fs };
    }
    pos += 8 + len + (len & 1);
  }
  throw new Error('wav ไม่มีข้อมูลเสียง');
}
function resample(x, from, to) {
  if (from === to) return x;
  const n = Math.floor(x.length * to / from), y = new Float32Array(n), k = from / to;
  for (let i = 0; i < n; i++) { const p = i * k, i0 = Math.floor(p), f = p - i0; y[i] = x[i0] + ((x[i0 + 1] ?? x[i0]) - x[i0]) * f; }
  return y;
}
let audioError = null;
async function startAudio() {
  const AC = window.AudioContext || window.webkitAudioContext;
  ctx = new AC({ latencyHint: 'interactive' });
  if (ctx.state === 'suspended') await ctx.resume();
  await ctx.audioWorklet.addModule('js/worklet.js');
  node = new AudioWorkletNode(ctx, 'rev-engine', { numberOfInputs: 0, outputChannelCount: [1] });
  node.connect(ctx.destination);
  const [wavBuf, marks] = await Promise.all([fetch('assets/engine_ref.wav').then(r => r.arrayBuffer()), fetch('assets/engine_ref.marks').then(r => r.text())]);
  const { x, fs } = parseWav(wavBuf), ratio = ctx.sampleRate / fs, wav = resample(x, fs, ctx.sampleRate);
  const scaled = ratio === 1 ? marks : marks.split(/\r?\n/).map(l => (/^\d+$/.test(l.trim()) ? String(Math.round(+l * ratio)) : l)).join('\n');
  node.port.onmessage = onWorkletMsg;
  node.port.postMessage({ type: 'init', wav, marks: scaled, design, volume: save.volume }, [wav.buffer]);
  document.addEventListener('visibilitychange', () => { if (document.hidden) ctx.suspend(); else ctx.resume(); });
}
function onWorkletMsg(e) {
  const m = e.data;
  if (m.type === 'ready') { ready = true; return; }
  if (m.type !== 'state') return;
  const { dynoTq, type, ...st } = m;
  Object.assign(S, st);
  if (dynoTq) finishDyno(dynoTq);
}

// ================================================================= หน้า / ข้อความ
let page = 'menu', prevPage = 'drive', showDyno = false, showFps = false;
let toastText = '', toastUntil = 0;
const now = () => performance.now() / 1000;
const toast = s => { toastText = s; toastUntil = now() + 3.5; };
function goTo(p) {
  if (p === 'menu' && page !== 'menu') prevPage = page;
  page = p; mouseThrottle = mouseStarter = mouseClutch = mouseBrake = false; ui.releaseAll();
}

// ================================================================= อินพุต
const keys = new Set();
let mouseThrottle = false, mouseThrottleValue = 0, mouseStarter = false, mouseClutch = false, mouseBrake = false;
let gradThr = 0, rpmRate = 0, lastRpmG = 0, gearSel = 0, slope = 0, assistUntil = 0;
const down = k => keys.has(k);

function selectGear(gi) {
  if (S.isAuto || gi === gearSel) return;
  const qs = installed.D.Quickshift || 0, qsOk = qs >= 1 && ((gi > gearSel && gearSel > 0) || (qs === 2 && gi < gearSel && gi > 0));      // ควิกชิฟเตอร์: ชิฟต์ไม่ต้องกำครัช
  if (save.clutchSys && S.running && S.lever < 0.6 && S.cfLock > 0.05 && !qsOk) {
    if (save.assist) { assistUntil = now() + 0.5; setTimeout(() => { gearSel = gi; }, 130); return; }
    toast('กำครัตช์ก่อน: ค้าง Shift (หรือกดค้างแถบครัตช์) แล้วกดเปลี่ยนเกียร์'); return;
  }
  gearSel = gi;
}
const shiftGear = d => selectGear(clamp(gearSel + d, 0, installed.D.Gears));
const changeSlope = d => { slope = clamp(slope + d, -6, 20); };
function setSurface(i) {
  save.surface = i; Veh.setSurface(i); send('surface', i); persist(); advKey = '';
  const s = Surfaces[i];
  toast(`พื้นถนน: ${s.name}  (แรงเกาะ x${f2(s.muK)}, ต้านการกลิ้ง x${f2(s.crrK)})`);
}
function toggleClutchSys() {
  save.clutchSys = !save.clutchSys; persist();
  toast(save.clutchSys ? 'ระบบครัตช์มือ: เปิด (ค้าง Shift = กำครัตช์)' : 'ระบบครัตช์มือ: ปิด (เปลี่ยนเกียร์ได้ทันที คลัตช์แรงเหวี่ยงจับเอง)');
}
function startDynoRun() {
  if (S.isAuto) { if (S.speed * 3.6 > 1) return toast('หยุดรถก่อนวิ่งไดโน'); }
  else if (gearSel !== 0) return toast('ใส่เกียร์ว่าง (N) ก่อนวิ่งไดโน');
  if (!S.running) return toast('ต้องสตาร์ทเครื่องให้ติดก่อน (กด S ค้าง)');
  node?.port.postMessage({ type: 'dyno' });
}
function setVolume(v) { save.volume = clamp(v, 0, 1); send('volume', save.volume); persist(); }
function toggleFullscreen() {
  try { if (document.fullscreenElement) document.exitFullscreen(); else document.documentElement.requestFullscreen?.(); } catch { /* ok */ }
}

window.addEventListener('keydown', e => {
  const k = e.key.toLowerCase();
  if (['arrowup', 'arrowdown', 'arrowleft', 'arrowright', ' ', 'tab'].includes(k)) e.preventDefault();
  const first = !keys.has(k); keys.add(k);
  if (first) {
    if (k === 'f11') { e.preventDefault(); toggleFullscreen(); }
    else if (showDyno) { if (k === 'enter' || k === 'escape') showDyno = false; }
    else if (showReport) { if (['enter', 'escape', 'r'].includes(k)) showReport = false; }
    else if (showEcuHelp) { if (['enter', 'escape', 'f1', '?', '/'].includes(k)) showEcuHelp = false; }
    else if (page === 'menu') { if (k === 'enter') goTo('drive'); else if (k === 'g') goTo('garage'); else if (k === 'escape') goTo(prevPage); }
    else if (page === 'settings') { if (k === 'escape') goTo('menu'); }
    else if (page === 'garage') garageKey(k, e.shiftKey);
    else if (page === 'drive') driveKey(k);
  } else if (page === 'garage' && !showDyno && (k === 'arrowleft' || k === 'arrowright')) garageKey(k, e.shiftKey);
  if (k === '+' || k === '=') setVolume(save.volume + 0.05);
  if (k === '-' && !(page === 'garage' && isEcuTab() && !advisorOn)) setVolume(save.volume - 0.05);
});
window.addEventListener('keyup', e => keys.delete(e.key.toLowerCase()));
window.addEventListener('blur', () => { keys.clear(); mouseThrottle = mouseStarter = mouseClutch = mouseBrake = false; ui?.releaseAll(); });

// ---------- ขับเอง: สตาร์ท ออกตัว ไล่เกียร์ เบรก วิ่งซ้ำ (เหมือนเวอร์ชัน C#) ----------
let autoDrive = false, apState = 'start', apT = 0, apClutchUntil = 0, apShiftAt = 0, apGearAt = 0, apDist0 = 0, apRuns = 0, apGearPending = -1;
function toggleAutopilot() {
  autoDrive = !autoDrive; apState = 'start'; apRuns = 0; apGearPending = -1;
  toast(autoDrive ? 'ขับเอง: เปิด (สตาร์ท ออกตัว ไล่เกียร์ เบรก วิ่งซ้ำให้เอง)  กด P อีกครั้งเพื่อหยุด' : 'ขับเอง: ปิด');
}
function apShiftTo(g, t) {
  if (S.isAuto || g === gearSel) return;
  if (save.clutchSys) { apClutchUntil = t + 0.45; apGearPending = g; apGearAt = t + 0.12; } else gearSel = g;
}
/** คืนค่า {thr, brake, starter} ที่ระบบขับเองต้องการ */
function autopilotStep(t) {
  if (apGearPending >= 0 && t >= apGearAt) { gearSel = apGearPending; apGearPending = -1; }
  const o = { thr: 0, brake: 0, starter: false }, m = installed, kmh = S.speed * 3.6;
  switch (apState) {
    case 'start':
      send('ignition', true);
      if (!S.isAuto && gearSel !== 0) gearSel = 0;
      if (!S.running) o.starter = true; else { apState = 'ready'; apT = t; }
      break;
    case 'ready':
      if (!S.running) { apState = 'start'; break; }
      if (kmh > 1) { apState = 'brake'; break; }
      if (t - apT > 0.8) { apShiftTo(1, t); apDist0 = S.dist; apT = t; apShiftAt = t; apState = 'launch'; }
      break;
    case 'launch': o.thr = 1; if (t - apT > 0.5) apState = 'run'; break;
    case 'run': {
      o.thr = 1;
      const shiftRpm = Math.min(S.limiter - 250, m.PeakPsRpm * 1.05);
      if (!S.isAuto && gearSel < m.D.Gears && apGearPending < 0 && t - apShiftAt > 0.7 && S.rpm >= shiftRpm) { apShiftTo(gearSel + 1, t); apShiftAt = t; }
      if (S.dist - apDist0 >= 520 || t - apT > 40) { apState = 'brake'; apT = t; }
      break;
    }
    case 'brake':
      o.brake = 1;
      if (kmh < 1) { apRuns++; apState = 'wait'; apT = t; if (!S.isAuto) apShiftTo(0, t); }
      break;
    default: if (t - apT > 2.5) { apState = 'ready'; apT = t; }
  }
  return o;
}
const apText = () => ({ start: 'กำลังสตาร์ท…', ready: 'เตรียมออกตัว…', launch: 'กำลังวิ่ง…', run: 'กำลังวิ่ง…', brake: 'เบรกจนหยุด…', wait: 'พักก่อนวิ่งซ้ำ…' }[apState]) + `  (วิ่งแล้ว ${apRuns} ครั้ง)`;

function driveKey(k) {
  if (k === 'k') send('ignition', !S.ignition);
  else if (k === 'd') startDynoRun();
  else if (k === 'g') goTo('garage');
  else if (k === 'arrowup' || k === 'e') shiftGear(1);
  else if (k === 'arrowdown' || k === 'q') shiftGear(-1);
  else if (k === 'n') selectGear(0);
  else if (k === 'c') toggleClutchSys();
  else if (k === 'p') toggleAutopilot();
  else if (k === 'v') setSurface((save.surface + 1) % Surfaces.length);
  else if (k === 'm') toggleGaugeMode();
  else if (k === '[') changeSlope(-1);
  else if (k === ']') changeSlope(1);
  else if (k === 'escape') goTo('menu');
}

// ================================================================= ไดโน
const RunColors = ['rgb(0,122,224)', 'rgb(224,54,66)', 'rgb(70,200,120)', 'rgb(240,170,40)', 'rgb(170,110,240)', 'rgb(40,210,220)'];
const dynoRuns = []; let dynoRunCounter = 0;
function addDynoRun(r) {
  const d = r.model.D, ser = JSON.stringify(d);
  const preset = Presets.find(p => JSON.stringify(p[1]) === ser);
  dynoRuns.push({ n: ++dynoRunCounter, r, vehicle: preset ? preset[0].replace(/\*$/, '') : 'ออกแบบเอง', when: new Date(), inertia: r.model.Inertia, calK: d.CalK, visible: true,
    spec: `${f0(r.model.Cc)} cc  ${+d.Bore.toFixed(1)}x${+d.Stroke.toFixed(1)}  CR ${+d.Cr.toFixed(1)}  ${d.Auto === 1 ? 'CVT' : d.Gears + ' สปีด'}` });
  while (dynoRuns.length > 5) dynoRuns.shift();
  dynoRuns.forEach((q, i) => { q.visible = i >= dynoRuns.length - 2; });     // แสดงสองรอบล่าสุดเป็นค่าเริ่มต้น
}
const niceStep = mx => { for (const v of [1, 2, 4, 5, 10, 20, 25, 40, 50, 100, 200, 250, 500]) if (v * 5 >= mx) return v; return 1000; };
function finishDyno(tqRaw) {
  if (S.dynoFailed) { dynoRes = null; toast('วิ่งไดโนไม่จบ: เครื่องดับระหว่างวัด'); return; }
  const tq = Array.from(tqRaw);
  const sm = tq.map((_, i) => {
    let s = 0, n = 0;
    for (let j = Math.max(0, i - 2); j <= Math.min(tq.length - 1, i + 2); j++) if (!Number.isNaN(tq[j])) { s += tq[j]; n++; }
    return Number.isNaN(tq[i]) || n === 0 ? NaN : s / n;
  });
  const pk = { ps: 0, psRpm: 0, tq: 0, tqRpm: 0 }; let any = false;
  sm.forEach((t, i) => {
    if (Number.isNaN(t)) return;
    any = true; const rpm = i * 100, ps = Ps(t, rpm);
    if (t > pk.tq) { pk.tq = t; pk.tqRpm = rpm; }
    if (ps > pk.ps) { pk.ps = ps; pk.psRpm = rpm; }
  });
  if (!any) return;
  dynoRes = { curve: sm, model: installed, ps: pk.ps, psRpm: pk.psRpm, peakTq: pk.tq, tqRpm: pk.tqRpm };
  addDynoRun(dynoRes);
  cur.hp = pk.ps * PsToHp;
  showDyno = true;
}

// ================================================================= วัดถนน
let roadArmed = true, roadTiming = false, roadStart = 0, distStart = 0, got060 = false, got100 = false, got402 = false;
function roadTests(t) {
  const kmh = S.speed * 3.6;
  if (kmh > topSession) topSession = kmh;
  if (topSession > cur.top) cur.top = topSession;
  if (topSession > save.best.top + 0.5) { save.best.top = topSession; persist(); }
  if (kmh < 1) { roadArmed = true; roadTiming = false; distStart = S.dist; }
  else if (roadArmed && !roadTiming) { roadArmed = false; if (kmh < 8) { roadTiming = true; roadStart = t; got060 = got100 = got402 = false; runFull = true; } }
  if (!roadTiming) return;
  const el = t - roadStart, dist = S.dist - distStart;
  runDist = dist; runTime = el; if (S.thr < 0.95) runFull = false;
  if (!got060 && kmh >= 60) { got060 = true; last060 = el; if (el >= 0.8) { cur.b060 = bestOf(cur.b060, el); if (save.best.b060 < 0 || el < save.best.b060) { save.best.b060 = el; persist(); } } }
  if (!got100 && dist >= 100) { got100 = true; last100 = el; if (el >= 2) { cur.b100 = bestOf(cur.b100, el); if (save.best.b100 < 0 || el < save.best.b100) { save.best.b100 = el; persist(); } } }
  if (!got402 && dist >= 402.3) { got402 = true; last402 = el; trap402 = kmh; if (el >= 5) { cur.b402 = bestOf(cur.b402, el); if (save.best.b402 < 0 || el < save.best.b402) { save.best.b402 = el; persist(); } } }
}

// ================================================================= โครงหน้าจอ: แถบซ้าย + หัวหน้า
const navs = [['ภาพรวม', 'OVERVIEW', 'menu'], ['อู่ออกแบบ', 'WORKSHOP', 'garage'], ['ห้องทดสอบ', 'TEST BENCH', 'drive'], ['ตั้งค่า', 'SETTINGS', 'settings']];
function drawTopBar(title, hint) {
  const rw = 220;
  ui.rect(0, 0, rw, VH, C.Panel); ui.line(rw, 0, rw, VH, C.Border, 1);
  ui.txt('REV LAB', F.title, C.Accent, 14, 20); ui.txt('ห้องทดลองเครื่องยนต์', F.xs, C.Dim, 28, 82);
  ui.line(20, 118, rw - 20, 118, C.Border, 1);
  navs.forEach(([name, sub, p], i) => {
    const r = R(0, 136 + i * 78, rw, 72), act = page === p, hov = inRect(r, ui.mouse);
    if (act) ui.rect(r.x, r.y, r.w, r.h, C.PanelHi); else if (hov) ui.rect(r.x, r.y, r.w, r.h, 'rgb(26,30,37)');
    if (act) ui.rect(r.x, r.y, 5, r.h, C.Accent);
    ui.txt(name, F.md, act ? C.Text : C.Dim, 30, r.y + 10); ui.txt(sub, F.cap, act ? C.Accent : 'rgb(90,98,110)', 31, r.y + 44);
    ui.hit(r, () => goTo(p));
  });
  const m = installed, cy = VH - 320;
  ui.line(20, cy, rw - 20, cy, C.Border, 1);
  ui.txt('เครื่องที่ติดตั้ง', F.cap, C.Dim, 28, cy + 14);
  ui.txt(f1(m.PeakHp), F.numL, C.Accent, 24, cy + 38);
  ui.txt('HP', F.md, C.Dim, 28, cy + 118); ui.txt(`@ ${fmtN(m.PeakPsRpm)} rpm`, F.sm, C.Dim, 80, cy + 122);
  ui.txt(`${f0(m.Cc)} cc  •  ${f1(m.PeakTq)} N·m`, F.sm, C.Text, 28, cy + 156);
  ui.txt(m.D.Auto === 1 ? 'ออโต้ CVT' : 'เกียร์ธรรมดา 4 สปีด', F.sm, C.Dim, 28, cy + 188);
  ui.btn(R(20, VH - 76, rw - 40, 52), 'ออกจากเกม', false, C.Red, () => location.reload(), F.sm);
  ui.txt(title, F.lg, C.Text, 252, 18); ui.txt(hint, F.xs, C.Dim, 254, 64);
  ui.line(rw, 98, VW, 98, C.Border, 1);
}

// ================================================================= ภาพรวม
function drawMenu() {
  drawTopBar('ภาพรวม', 'Enter ไปทดสอบ  •  G อู่ออกแบบ  •  F11 เต็มจอ  •  + / − ระดับเสียง');
  const m = installed, d = design;
  const sheet = R(252, 124, 1000, 800);
  ui.panelBox(sheet, 'ใบสเปกเครื่อง (ที่ติดตั้งอยู่)');
  let y = 176;
  const sec = name => { y += 8; ui.txt(name, F.cap, C.Accent, sheet.x + 28, y); ui.line(sheet.x + 28 + 250, y + 11, sheet.x + sheet.w - 28, y + 11, C.Border, 1); y += 28; };
  const row = (k, v) => { ui.txt(k, F.sm, C.Dim, sheet.x + 36, y); ui.txtR(v, F.numS, C.Text, sheet.x + sheet.w - 36, y); y += 31; };
  const fuel = Fuels[clamp(d.Fuel, 0, 6)];
  sec('BLOCK'); row('ความจุ', `${f1(m.Cc)} cc`); row('ลูก × ชัก', `${f1(d.Bore)} × ${f1(d.Stroke)} มม.   (ก้าน ${f0(d.Rod)} มม.)`);
  row('ล้อ / ยาง', `${tireCode(d)}  ขอบ ${f2(d.RimW)}`); row('ข้อเหวี่ยง / จานไฟ', `${f1(d.CrankKg)} / ${f2(d.FlywheelKg)} กก.`);
  sec('HEAD / CAM'); row('วาล์ว / เรือน', `${d.Valves} วาล์ว / ${f0(d.Throttle)} มม.`);
  row('แคม LSA / องศา IN,EX / ยก IN,EX', `${f0(d.Lsa)}° / ${f0(d.Duration)}°,${f0(d.DurationEx)}° / ${f1(d.Lift)},${f1(d.LiftEx)} มม.`);
  sec('COMPRESSION / FUEL'); row('อัตราส่วนกำลังอัด', `${f1(d.Cr)} : 1`); row('แรงดันอัด (วัดจริง)', `${f0(m.Psi)} psi`);
  row('น้ำมัน', `${fuel.name}  (RON ${f0(fuel.ron)})`); row('รอบตัด', `${fmtN(d.Limiter)} rpm`);
  sec('OUTPUT'); row('กำลังสูงสุด', `${f1(m.PeakHp)} HP @ ${fmtN(m.PeakPsRpm)} rpm`); row('แรงบิดสูงสุด', `${f1(m.PeakTq)} N·m @ ${fmtN(m.PeakTqRpm)} rpm`);
  sec('DRIVETRAIN');
  if (d.Auto === 1) { row('ระบบ', 'ออโต้ CVT'); row('ชาม ใน/นอก / เม็ด / สปริง', `${f1(d.RampDeg)}°/${f1(d.RampOutDeg)}° / ${d.Rollers} × ${+d.RollerG.toFixed(1)} g / ${f0(d.SpringK)} N/มม.`); row('เฟืองท้าย', `${d.GearF}/${d.GearR}  (ทดรวม ${f2(Cvt.final(d))})`); }
  else { row('ระบบ', 'เกียร์ธรรมดา 4 สปีด'); row('สเตอร์ หน้า/หลัง', `${f0(d.SprocketF)}/${f0(d.SprocketR)}  (ทดสุดท้าย ${Veh.final(d).toFixed(3)})`); }

  const rec = R(1280, 124, 600, 400);
  ui.panelBox(rec, 'สถิติบนถนน');
  let ry = 184;
  const T = v => (v < 0 ? '-' : f2(v) + ' วิ');
  const rrow = (k, v) => { ui.txt(k, F.sm, C.Dim, rec.x + 28, ry + 4); ui.txtR(v, F.num, C.Accent, rec.x + rec.w - 28, ry); ry += 54; };
  rrow('0–60 กม./ชม.', T(save.best.b060)); rrow('100 เมตร', T(save.best.b100)); rrow('402 เมตร', T(save.best.b402));
  rrow('ท็อปสปีด', save.best.top <= 0 ? '-' : `${f0(save.best.top)} กม./ชม.`);
  ui.btn(R(1280, 556, 600, 88), 'ไปห้องทดสอบ', false, C.Green, () => goTo('drive'), F.lg);
  ui.btn(R(1280, 660, 600, 88), 'อู่ออกแบบเครื่อง', false, C.Accent, () => goTo('garage'), F.lg);
  ui.btn(R(1280, 764, 600, 70), 'ตั้งค่า', false, C.Blue, () => goTo('settings'), F.md);
}

// ================================================================= ตั้งค่า
function drawSettings() {
  drawTopBar('ตั้งค่า', 'Esc กลับ  •  F11 เต็มจอ/หน้าต่าง');
  const box = R(252, 124, 1100, 790);
  ui.panelBox(box, 'ระบบ');
  const x = box.x + 40, w = box.w - 80; let y = box.y + 80;
  ui.txt('ระดับเสียง', F.md, C.Text, x, y); ui.txtR(`${f0(save.volume * 100)}%`, F.num, C.Accent, x + w, y);
  ui.slider(R(x, y + 48, w, 36), save.volume, C.Accent, v => setVolume(v));
  y += 110; ui.txt('ความดังเสียงเดินเบา', F.md, C.Text, x, y); ui.txtR(save.idleGain <= 0.002 ? 'ปกติ (ไม่ขยาย)' : `ขยาย x${+(1 + save.idleGain * 30).toFixed(1)}`, F.num, C.Accent, x + w, y);
  ui.slider(R(x, y + 48, w, 36), save.idleGain, C.Accent, v => { save.idleGain = clamp(v, 0, 1); persist(); });
  ui.txt('ขยายเฉพาะช่วงรอบเดินเบา ลดลงจนไม่มีผลเมื่อรอบสูงกว่ารอบเดินเบา ~2,500  (เพิ่มมากไปเสียงจะเริ่มแตก)', F.xs, C.Dim, x, y + 92);
  y += 160; ui.txt("การแสดงผล", F.md, C.Text, x, y);
  ui.btn(R(x + w - 520, y - 10, 520, 60), document.fullscreenElement ? 'เต็มจอ (กดเพื่อเป็นหน้าต่าง)' : 'หน้าต่าง (กดเพื่อเต็มจอ)', false, C.Blue, toggleFullscreen, F.sm);
  y += 90; ui.txt('แสดงเฟรมเรต (FPS)', F.md, C.Text, x, y);
  ui.btn(R(x + w - 260, y - 10, 260, 60), showFps ? 'เปิด' : 'ปิด', showFps, C.Green, () => { showFps = !showFps; }, F.sm);
  y += 90; ui.txt('ล้างสถิติบนถนน', F.md, C.Text, x, y);
  ui.btn(R(x + w - 260, y - 10, 260, 60), 'ล้างสถิติ', false, C.Red, () => {
    save.best = { b060: -1, b100: -1, b402: -1, top: 0 }; last060 = last100 = last402 = -1; runDist = -1; topSession = 0; cur = newRun(); before = null; persist(); toast('ล้างสถิติแล้ว');
  }, F.sm);
  y += 90; ui.txt('ช่วยกำครัตช์อัตโนมัติ (มือถือ)', F.md, C.Text, x, y);
  ui.btn(R(x + w - 260, y - 10, 260, 60), save.assist ? 'เปิด' : 'ปิด', save.assist, C.Green, () => { save.assist = !save.assist; persist(); }, F.sm);
}

// ================================================================= ห้องทดสอบ
const gaugeMaxOf = lim => Math.max(10000, Math.ceil((lim + 1000) / 1000) * 1000);
let needle = 0;
function drawDrive() {
  drawTopBar('ห้องทดสอบ', 'W เร่งสุด  •  X ค่อย ๆ เร่ง (ไม่ตัดรอบ)  •  Z ค่อย ๆ เร่ง (แตะรอบตัดเบา ๆ)  •  Space เบรก  •  S สตาร์ท  •  E/Q เกียร์  •  C ครัตช์  •  P ขับเอง  •  V พื้นถนน  •  D ไดโน  •  [ ] ความชัน  •  G อู่');
  const m = installed, d = m.D, auto = d.Auto === 1;
  ui.txt('พื้นถนน  [V]', F.sm, C.Dim, 252, 116);
  Surfaces.forEach((s, i) => ui.btn(R(420 + i * 150, 108, 140, 44), s.name, save.surface === i, C.Accent, () => setSurface(i), F.sm));
  const sf = Surfaces[save.surface];
  ui.txt(`${sf.note}   •   แรงเกาะ x${f2(sf.muK)}   •   ต้านการกลิ้ง x${f2(sf.crrK)}`, F.xs, C.Dim, 254, 158);
  drawGauge(640, 552, 335);
  ui.btn(R(868, 196, 168, 34), 'M  จอ: ' + (save.gaugeMode === 1 ? 'TFT' : 'เข็ม'), false, C.Blue, toggleGaugeMode, F.cap);
  drawThrottle(1056, 170, 64, 660);

  ui.txt('ระดับเสียง', F.sm, C.Dim, 270, 954);
  ui.slider(R(430, 946, 560, 40), save.volume, C.Accent, v => setVolume(v));
  ui.scope(R(270, 1000, 720, 64), S.scope, S.scopeW);

  const px = 1180, pw = 700;
  ui.panelBox(R(px, 100, pw, 176), auto ? 'รถออโต้ (CVT)' : 'รถเกียร์ธรรมดา');
  if (auto) {
    const w = d.Rollers === 6 && d.RollerG !== d.RollerG2 ? `${+d.RollerG.toFixed(1)}/${+d.RollerG2.toFixed(1)} g` : `${+d.RollerG.toFixed(1)} g`;
    ui.txt(`ชาม ${f1(d.RampDeg)}°/${f1(d.RampOutDeg)}°  •  ${d.Rollers} เม็ด ${w}  •  สปริง ${f0(d.SpringK)} N/มม.`, F.md, C.Text, px + 20, 140);
    ui.txt(`${f0(m.Cc)} cc  •  ${f1(m.PeakHp)} HP @ ${fmtN(m.PeakPsRpm)}`, F.sm, C.Dim, px + 20, 176);
    ui.txt(`สายพาน ${f2(Cvt.ratio(S.cvtS))}  •  เฟืองท้าย ${d.GearF}/${d.GearR} x ${f2(d.FirstRed)} (ทดรวม ${f2(Cvt.final(d))})`, F.sm, C.Dim, px + 20, 208);
    ui.bar(R(px + 20, 240, pw - 40, 12), S.cvtS, C.Accent);
    if (autoDrive) ui.txt(apText(), F.xs, C.Green, px + 20, 254);
  } else {
    ui.txt(`เกียร์ 4 สปีด  •  สเตอร์ ${f0(d.SprocketF)}/${f0(d.SprocketR)}`, F.md, C.Text, px + 20, 140);
    ui.txt(`${f0(m.Cc)} cc  •  ${f1(m.PeakHp)} HP @ ${fmtN(m.PeakPsRpm)}`, F.sm, C.Dim, px + 20, 176);
    ui.txt(`ทดสุดท้าย ${Veh.final(d).toFixed(3)}   รถ+คน ${f0(Veh.mass(d))} กก.`, F.sm, C.Dim, px + 20, 208);
    if (autoDrive) ui.txt(apText(), F.xs, C.Green, px + 20, 244);
  }
  ui.btn(R(px + pw - 236, 108, 216, 34), autoDrive ? 'P ขับเอง: เปิด' : 'P ขับเอง: ปิด', autoDrive, C.Green, toggleAutopilot, F.xs);

  const bw = 163, bg = 16, by = 292, bh = 64;
  ui.btn(R(px, by, bw, bh), 'S สตาร์ท', mouseStarter || down('s'), C.Green, null, F.md, () => { send('ignition', true); mouseStarter = true; }, () => { mouseStarter = false; });
  ui.btn(R(px + (bw + bg), by, bw, bh), 'K ดับเครื่อง', !S.ignition, C.Red, () => send('ignition', !S.ignition), F.md);
  ui.btn(R(px + 2 * (bw + bg), by, bw, bh), 'D ไดโน', S.dynoPhase !== 0, C.Green, startDynoRun, F.md);
  ui.btn(R(px + 3 * (bw + bg), by, bw, bh), 'G อู่', false, C.Accent, () => goTo('garage'), F.md);

  ui.panelBox(R(px, 372, pw, 148), auto ? 'คลัตช์แรงเหวี่ยง (ไม่ต้องเปลี่ยนเกียร์ จับเองที่ ~3,200 รอบ)'
    : save.clutchSys ? 'เกียร์  (E ขึ้น  Q ลง  N ว่าง)  •  ค้าง Shift = กำครัตช์' : 'เกียร์  (E ขึ้น  Q ลง  N ว่าง)  •  ครัตช์มือปิด เปลี่ยนเกียร์ได้ทันที');
  if (auto) {
    const kb = R(px + 20, 424, pw - 40, 44);
    ui.bar(kb, S.clutchLock, C.Green); ui.txtC(`คลัตช์จับ ${f0(S.clutchLock * 100)}%`, F.sm, C.Text, kb.x + kb.w / 2, kb.y + kb.h / 2);
  } else {
    const ng = d.Gears, gsp = (pw - 40) / (ng + 1);
    for (let i = 0; i <= ng; i++) ui.btn(R(px + 20 + i * gsp, 412, gsp - 12, 52), i === 0 ? 'N' : String(i), gearSel === i, C.Accent, () => selectGear(i), F.lg);
    if (save.clutchSys) {
      ui.bar(R(px + 20, 482, pw - 160, 14), S.lever, S.lever > 0.6 ? C.Blue : C.Accent);
      ui.hit(R(px + 20, 476, pw - 160, 26), () => { mouseClutch = true; }, null, () => { mouseClutch = false; });
      ui.txt(S.lever > 0.6 ? 'กำครัตช์อยู่ (เปลี่ยนเกียร์ได้)' : 'ครัตช์ปล่อย  [Shift หรือกดค้างแถบนี้]', F.xs, C.Dim, px + 22, 496);
    } else ui.txt('ครัตช์มือปิด: ไม่ต้องกำครัตช์ เปลี่ยนเกียร์ได้เลย (คลัตช์แรงเหวี่ยงจับเอง)', F.xs, C.Dim, px + 22, 486);
    ui.btn(R(px + pw - 130, 476, 112, 34), save.clutchSys ? 'C ครัตช์: เปิด' : 'C ครัตช์: ปิด', save.clutchSys, C.Blue, toggleClutchSys, F.xs);
  }

  const slopeText = slope === 0 ? 'ถนนราบ' : (slope > 0 ? 'ขึ้นเนิน +' : 'ลงเนิน ') + slope + '%';
  ui.panelBox(R(px, 536, pw, 96), 'ถนน / ความชัน   ( [ ] ปรับ )   ' + slopeText);
  ui.smallBtn(R(px + 20, 576, 52, 44), '−', () => changeSlope(-1));
  [['ราบ', 0], ['เนิน 6%', 6], ['ชัน 12%', 12], ['ลง −4%', -4]].forEach(([n, v], i) => ui.btn(R(px + 82 + i * 132, 576, 124, 44), n, slope === v, C.Blue, () => { slope = v; }, F.sm));
  ui.smallBtn(R(px + pw - 72, 576, 52, 44), '+', () => changeSlope(1));

  ui.panelBox(R(px, 648, pw, 170), null);
  ui.btn(R(px + 16, 654, 150, 32), 'คลัตช์ / ยาง', driveTab === 0, C.Green, () => { driveTab = 0; }, F.cap);
  ui.btn(R(px + 174, 654, 150, 32), 'Live data', driveTab === 1, C.Blue, () => { driveTab = 1; }, F.cap);
  ui.btn(R(px + 332, 654, 150, 32), 'ความร้อน / สึก', driveTab === 2, C.Red, () => { driveTab = 2; }, F.cap);
  let ry = 698;
  const srow = (label, frac, col, value) => {
    ui.txt(label, F.sm, C.Dim, px + 20, ry); ui.bar(R(px + 190, ry + 6, 250, 16), frac, col); ui.txt(value, F.sm, col, px + 456, ry); ry += 34;
  };
  if (driveTab === 2) {
    const T = S.engT ?? 35, visc = S.oilVisc ?? 1, wear = S.wear ?? 0, oilT = S.oilT ?? 35;
    const tCol = T > 170 ? C.Red : T > 135 ? C.Yellow : T < 60 ? C.Blue : C.Green, tTxt = T > 170 ? 'ร้อนจัด! ลูกเสี่ยงละลาย' : T > 135 ? 'ร้อน กำลังตก น็อกง่าย' : T < 60 ? 'เครื่องยังเย็น' : 'อุณหภูมิทำงาน';
    const vCol = visc < 0.45 ? C.Red : visc < 0.7 ? C.Yellow : C.Green, vTxt = visc < 0.45 ? 'บางมาก ฟิล์มขาด' : visc < 0.7 ? 'บาง' : visc > 1.8 ? 'หนืด (เย็น)' : 'ปกติ';
    const wCol = wear > 0.5 ? C.Red : wear > 0.2 ? C.Yellow : C.Green, wTxt = S.seized ? S.seizeCause : wear > 0.5 ? 'ลูกสึกหนัก ไม่ข้ามรอบ แก๊ก' : wear > 0.2 ? 'ลูกเริ่มสึก มีเสียงแก๊ก' : 'ปกติ';
    ui.btn(R(px + pw - 196, 654, 180, 32), 'ซ่อม / เปลี่ยนน้ำมัน', !!S.seized, C.Green, () => send('repairNow', 1), F.cap);
    srow('เครื่อง', clamp((T - 30) / 190, 0, 1), tCol, `${f0(T)} °C  ${tTxt}`);
    srow('น้ำมัน', clamp(visc / 2.2, 0, 1), vCol, `${f0(oilT)}°C  ${visc.toFixed(1)}x  ${vTxt}`);
    srow('ลูกสูบ', wear, wCol, `สึก ${f0(wear * 100)}%  ${wTxt}`);
  } else if (driveTab === 1) {
    const lm = (S.ecuAfr ?? 14.7) / installed.Stoich, sy = mixSymptom(lm), adv = S.ecuAdv ?? 0, mbt = S.ecuMbt ?? 0, kn = S.knock ?? 0, egt = S.ecuEgt ?? 0;
    const items = [
      ['AFR', f1(S.ecuAfr ?? 14.7), sy ? sy[1] : C.Text],
      ['จุดไฟ', `${f0(adv)}° / MBT ${f0(mbt)}°`, Math.abs(adv - mbt) > 8 ? C.Yellow : C.Text],
      ['หัวฉีด', `${f0((S.ecuDuty ?? 0) * 100)}%  ${f2(S.ecuPw ?? 0)} ms`, (S.ecuDuty ?? 0) > 0.85 ? C.Red : C.Text],
      ['EGT', `${f0(egt)} °C`, egt > 1050 ? C.Red : C.Text],
      ['น็อก', `${f0(kn * 100)}%`, kn > 0.05 ? C.Red : C.Text],
      ['โหลด', `${f0((S.load ?? 0) * 100)}%`, C.Text],
    ];
    items.forEach(([lb, val, col], i) => { const x = px + 20 + (i % 2) * (pw / 2), y = 696 + Math.floor(i / 2) * 32; ui.txt(lb, F.xs, C.Dim, x, y + 5); ui.txt(val, F.sm, col, x + 96, y); });
    ui.txt(sy ? 'อาการ: ' + sy[0] : 'ส่วนผสมปกติ: ไม่มีอาการสะดุด/แห้ง', F.xs, sy ? sy[1] : C.Green, px + 20, 796);
  } else {
  const srow = (label, frac, col, value) => {
    ui.txt(label, F.sm, C.Dim, px + 20, ry); ui.bar(R(px + 190, ry + 6, 250, 16), frac, col); ui.txt(value, F.sm, col, px + 456, ry); ry += 34;
  };
  const lk = S.clutchLock;
  srow('คลัตช์จับ', lk, C.Green, `${f0(lk * 100)}%  ` + (lk > 0.95 ? 'จับสนิท' : lk > 0.05 ? 'กำลังจับ' : 'ปล่อย/ยังไม่จับ'));
  const slipRpm = Math.abs(S.slipRpm), slipCol = lk < 0.05 ? C.Dim : slipRpm < 150 ? C.Green : slipRpm < 900 ? C.Yellow : C.Red;
  srow('คลัตช์ลื่น', Math.min(slipRpm / 3000, 1), slipCol, lk < 0.05 ? 'ไม่ส่งกำลัง' : `${fmtN(slipRpm)} rpm  ` + (slipRpm < 150 ? 'ไม่ลื่น' : slipRpm < 900 ? 'ลื่นเล็กน้อย' : 'ลื่น (ออกตัว)'));
  const tu = S.tracUse, tCol = S.spin ? C.Red : tu > 0.85 ? C.Yellow : C.Green;
  srow('แรงเกาะยาง', Math.min(tu, 1), tCol, `${f0(tu * 100)}%  ` + (S.spin ? 'ล้อฟรี! ยางลื่น' : tu > 0.85 ? 'ใกล้ลื่น' : 'ยางเกาะดี'));
  ui.txt(`แรงขับ ${fmtN(S.driveForce)} N / เกาะได้สูงสุด ${fmtN(S.gripMax)} N  •  ยาง ${tireCode(d)} ขอบ ${f2(d.RimW)}`, F.xs, C.Dim, px + 20, ry - 2);
  }

  ui.panelBox(R(px, 834, pw, 216), 'สถิติ');
  const bf = before;
  ui.txtR('ดีสุด', F.xs, C.Dim, px + 300, 846); ui.txtR('ก่อนแก้', F.xs, C.Dim, px + 430, 846); ui.txtR('ล่าสุด', F.xs, C.Dim, px + 560, 846);
  ui.txtR(bf ? 'ต่างจากก่อนแก้' : 'ต่างจากดีสุด', F.xs, C.Dim, px + pw - 20, 846);
  const N = (v, dec) => (v <= 0 ? '-' : v.toFixed(dec));
  let sy = 878;
  const stRow = (a, best, bef, last, lowerBetter, dec, lastTxt = '') => {
    ui.txt(a, F.sm, C.Dim, px + 20, sy); ui.txtR(N(best, dec), F.sm, C.Text, px + 300, sy); ui.txtR(N(bef, dec), F.sm, C.Dim, px + 430, sy);
    ui.txtR(lastTxt || N(last, dec), F.sm, C.Accent, px + 560, sy);
    const ref = bf ? bef : best;
    if (ref > 0 && last > 0) {
      const diff = lowerBetter ? ref - last : last - ref;
      ui.txtR((diff > 0.0049 ? '+' : '') + diff.toFixed(dec), F.sm, Math.abs(diff) < 0.005 ? C.Dim : diff > 0 ? C.Green : C.Red, px + pw - 20, sy);
    } else ui.txtR('-', F.sm, C.Dim, px + pw - 20, sy);
    sy += 28;
  };
  const hpNow = dynoRes ? dynoRes.ps * PsToHp : 0;
  stRow('รอบสูงสุด (rpm)', cur.peakRpm, bf?.peakRpm ?? 0, peakRpm, false, 0);
  stRow('0–60 (วิ)', save.best.b060, bf?.b060 ?? 0, last060, true, 2);
  stRow('100 ม. (วิ)', save.best.b100, bf?.b100 ?? 0, last100, true, 2);
  stRow(last402 > 0 ? `402 ม. (วิ) @${f0(trap402)}` : '402 ม. (วิ)', save.best.b402, bf?.b402 ?? 0, last402, true, 2);
  stRow('ท็อป (กม./ชม.)', save.best.top, bf?.top ?? 0, topSession, false, 0);
  stRow('แรงม้า (HP)', cur.hp, bf?.hp ?? 0, hpNow, false, 1);
}


function drawThrottle(x, y, w, h) {
  ui.txtC('คันเร่ง', F.sm, C.Dim, x + w / 2, y - 28);
  const r = R(x, y, w, h);
  ui.fillRR(r, 3, 'rgb(14,17,21)');
  const thr = S.thr, fh = h * clamp(thr, 0, 1);
  if (fh > 2) {
    const g = ui.g, gr = g.createLinearGradient(0, y, 0, y + h); gr.addColorStop(0, C.Red); gr.addColorStop(1, C.Accent);
    g.fillStyle = gr; g.fillRect(x, y + h - fh, w, fh);
  }
  ui.drawRR(r, 3, C.Border, 1);
  ui.txtC(Math.floor(thr * 100) + '%', F.md, C.Text, x + w / 2, y + h + 32);
  const braking = down(' ') || down('b') || mouseBrake;
  const br = R(x - 6, y + h + 62, w + 12, 46);
  ui.fillRR(br, 3, braking ? C.Red : C.Panel); ui.drawRR(br, 3, braking ? C.Red : C.Border, 1);
  ui.txtC('เบรก', F.sm, braking ? C.White : C.Dim, br.x + br.w / 2, br.y + br.h / 2);
  ui.hit(br, () => { mouseBrake = true; }, null, () => { mouseBrake = false; });
  const apply = p => { mouseThrottle = true; mouseThrottleValue = clamp(1 - (p.y - y) / h, 0, 1); };
  ui.hit(R(x - 30, y - 10, w + 60, h + 20), apply, apply, () => { mouseThrottle = false; });
}

/** ระยะจากจอด + ข้อความสถานะ (ใช้ร่วมกันทั้งจอเข็มและจอ TFT) */
function driveOverlay(cx, msgY) {
  const g = ui.g;
  ui.txt('ระยะจากจอด', F.xs, C.Dim, 262, 800);
  ui.txt(runDist < 0 ? '-' : `${runDist < 1000 ? f0(runDist) + ' ม.' : f2(runDist / 1000) + ' กม.'}`, F.num, C.Accent, 262, 826);
  ui.txt(runDist < 0 ? 'ออกตัวจากหยุดนิ่งเพื่อวัด' : `${f1(runTime)} วิ  ${runFull ? '• เต็มคันเร่ง' : '• ไม่เต็มคันเร่ง'}`, F.xs, runDist >= 0 && runFull ? C.Green : C.Dim, 262, 876);
  if (S.dynoPhase !== 0) ui.txtC('กำลังวัดไดโน...', F.btn, C.Green, cx, msgY);
  else if (S.seized) ui.txtC('ลูกสูบล็อก! ' + S.seizeCause, F.btn, C.Red, cx, msgY);
  else if ((S.engT ?? 0) > 150) ui.txtC('เครื่องร้อนจัด! ปล่อยคันเร่ง ให้ลมระบาย', F.btn, C.Red, cx, msgY);
  else if (!S.running) ui.txtC(S.ignition ? 'กด S ค้างเพื่อสตาร์ท' : 'ดับเครื่องอยู่ - กด S ค้างเพื่อสตาร์ท', F.md, C.Accent, cx, msgY);
}

const toggleGaugeMode = () => { save.gaugeMode = save.gaugeMode === 1 ? 0 : 1; persist(); };
/** จอ TFT แบบ XMAX: จอเล็กด้านบน (ความเร็ว/อุณหภูมิ/ระยะ) + จอใหญ่เกจรอบเป็นแท่งโค้งเรียงจากซ้ายไปขวา */
function drawGaugeTft(cx, cy) {
  const g = ui.g, lim = installed.D.Limiter, red = lim - 600, gm = gaugeMaxOf(lim), T = S.engT ?? 30;
  // ---- จอเล็กด้านบน ----
  const top = R(cx - 330, cy - 330, 660, 150);
  ui.fillRR(top, 14, 'rgb(8,11,16)'); ui.drawRR(top, 14, 'rgb(46,52,62)', 3);
  ui.txtR(String(Math.round(S.speed * 3.6)), F.speed, 'rgb(236,242,248)', top.x + 250, top.y + 14);
  ui.txt('km/h', F.sm, C.Dim, top.x + 262, top.y + 96);
  const gx = top.x + 340, gw = 280, hot = T > 135;
  ui.txt('C', F.cap, C.Dim, gx - 22, top.y + 22); ui.txt('H', F.cap, hot ? C.Red : C.Dim, gx + gw + 8, top.y + 22);
  const filled = Math.round(clamp((T - 30) / 160, 0, 1) * 10);
  for (let i = 0; i < 10; i++) { const x = gx + i * (gw / 10); ui.rect(x + 2, top.y + 16, gw / 10 - 6, 30, i < filled ? (hot && i > 6 ? C.Red : 'rgb(232,238,244)') : 'rgb(28,34,42)'); }
  ui.txt(`${f0(T)}°C`, F.xs, hot ? C.Red : C.Dim, gx, top.y + 54);
  ui.txt(S.isAuto ? 'AUTO ' + f2(Cvt.ratio(S.cvtS)) : 'GEAR ' + (S.gear === 0 ? 'N' : S.gear), F.sm, C.Text, gx + 150, top.y + 52);
  ui.txt('TRIP', F.cap, C.Dim, gx, top.y + 98); ui.txtR((S.dist / 1000).toFixed(2) + ' km', F.sm, 'rgb(236,242,248)', gx + gw, top.y + 92);
  // ---- จอ TFT หลัก ----
  const tft = R(cx - 350, cy - 160, 700, 340);
  ui.fillRR(tft, 16, 'rgb(4,7,12)'); ui.drawRR(tft, 16, 'rgb(46,52,62)', 3);
  const d = new Date(), pad2 = v => String(v).padStart(2, '0');
  ui.txt(`${pad2(d.getHours())}:${pad2(d.getMinutes())}`, F.md, 'rgb(236,242,248)', tft.x + 28, tft.y + 14);
  ui.txtR(`${f0(T)}°C`, F.md, hot ? C.Red : 'rgb(236,242,248)', tft.x + tft.w - 28, tft.y + 14);
  const abs = R(tft.x + tft.w / 2 - 28, tft.y + 18, 56, 26); ui.fillRR(abs, 13, S.spin ? C.Accent : 'rgb(60,50,20)'); ui.txtC('ABS', F.cap, S.spin ? C.Black : 'rgb(150,120,40)', abs.x + 28, abs.y + 13);
  const x0 = tft.x + 44, w = tft.w - 88, yT = tft.y + 70, yB = tft.y + 262, yM = (yT + yB) / 2, n = Math.max(20, Math.round(gm / 250));
  g.lineCap = 'butt';
  for (let i = 0; i <= n; i++) {
    const rp = i * gm / n, x = x0 + w * i / n, on = rp <= needle, isRed = rp >= red, bow = 40 * Math.pow(1 - i / n, 1.5) + 4;
    let col;
    if (isRed) col = on ? 'rgb(255,72,60)' : 'rgb(96,28,28)';
    else col = on ? `rgb(${Math.round(150 + 90 * (1 - i / n))},${Math.round(190 + 50 * (1 - i / n))},255)` : 'rgb(30,56,92)';
    const head = on && rp > needle - gm / n * 3;
    g.save(); if (head) { g.shadowColor = isRed ? 'rgb(255,70,60)' : 'rgb(80,150,255)'; g.shadowBlur = 20; }
    g.strokeStyle = col; g.lineWidth = on ? 3.4 : 1.7; g.beginPath(); g.moveTo(x, yT); g.quadraticCurveTo(x - bow, yM, x, yB); g.stroke(); g.restore();
  }
  for (let r = 0; r <= gm; r += 500) {
    const x = x0 + w * r / gm, major = r % 1000 === 0, on = r <= needle;
    if (major) { ui.rect(x - 13, yM - 14, 26, 28, 'rgba(4,7,12,0.78)'); ui.txtC(String(r / 1000), F.sm, r >= red ? C.Red : on ? 'rgb(240,246,255)' : 'rgb(110,122,140)', x, yM); }
    else { g.beginPath(); g.arc(x, yM, 2.6, 0, 7); g.fillStyle = on ? 'rgb(240,246,255)' : 'rgb(80,90,106)'; g.fill(); }
  }
  ui.txtR('x1000r/min', F.xs, C.Dim, tft.x + tft.w - 24, tft.y + tft.h - 36);
  // ---- ตัวเลขรอบ + ไฟตัดรอบ ----
  ui.txtC(String(Math.round(needle / 10) * 10), F.numL, C.Text, cx, cy + 238);
  const limOn = S.cut || needle > lim - 150, lamp = R(cx - 70, cy + 296, 140, 28);
  ui.fillRR(lamp, 8, limOn ? C.Red : 'rgb(50,54,62)'); ui.txtC('ตัดรอบ', F.sm, limOn ? C.White : C.Dim, lamp.x + lamp.w / 2, lamp.y + lamp.h / 2);
  driveOverlay(cx, cy - 172);
}

function drawGauge(cx, cy, r) {
  if (save.gaugeMode === 1) { drawGaugeTft(cx, cy); return; }
  const g = ui.g, k = r / 215, lim = installed.D.Limiter, red = lim - 600, gm = gaugeMaxOf(lim);
  const ang = rpm => (135 + 270 * clamp(rpm, 0, gm) / gm) * Math.PI / 180;
  g.beginPath(); g.arc(cx, cy, r + 26, 0, 7); g.fillStyle = 'rgb(14,17,21)'; g.fill(); g.strokeStyle = C.Border; g.lineWidth = 3; g.stroke();
  g.beginPath(); g.arc(cx, cy, r - 34 * k, 0, 7); g.strokeStyle = 'rgb(30,35,42)'; g.lineWidth = 1.5; g.stroke();
  g.lineCap = 'butt';
  g.beginPath(); g.arc(cx, cy, r, ang(0), ang(red)); g.strokeStyle = 'rgb(70,76,88)'; g.lineWidth = 10 * k; g.stroke();
  g.beginPath(); g.arc(cx, cy, r, ang(red), ang(gm)); g.strokeStyle = C.Red; g.lineWidth = 10 * k; g.stroke();
  for (let rpm = 0; rpm <= gm; rpm += 500) {
    const a = ang(rpm), major = rpm % 1000 === 0, c = Math.cos(a), s = Math.sin(a);
    ui.line(cx + c * (r - 8 * k), cy + s * (r - 8 * k), cx + c * (r - (major ? 30 : 20) * k), cy + s * (r - (major ? 30 : 20) * k), major ? C.Text : C.Dim, (major ? 3 : 1.5) * k);
    if (major) ui.txtC(String(rpm / 1000), F.gaugeNum, rpm >= red ? C.Red : C.Text, cx + c * (r - 54 * k), cy + s * (r - 54 * k));
  }
  ui.txtC('x1000 rpm', F.sm, C.Dim, cx, cy - 80 * k);
  ui.txtC(String(Math.round(needle / 10) * 10), F.rpm, C.Text, cx, cy + 105 * k);
  const limOn = S.cut || needle > lim - 150, lamp = R(cx - 70 * k, cy + 148 * k, 140 * k, 26 * k);
  ui.fillRR(lamp, 8, limOn ? C.Red : 'rgb(50,54,62)');
  ui.txtC('ตัดรอบ', F.sm, limOn ? C.White : C.Dim, lamp.x + lamp.w / 2, lamp.y + lamp.h / 2);
  const gear = S.isAuto ? 'ออโต้ ' + f2(Cvt.ratio(S.cvtS)) : 'เกียร์ ' + (S.gear === 0 ? 'N' : S.gear);
  ui.txtC(f0(S.speed * 3.6), F.speed, C.Accent, cx, cy + 205 * k);
  ui.txtC(`กม./ชม.     ${gear}`, F.sm, C.Text, cx, cy + 238 * k);
  driveOverlay(cx, cy - 112 * k);
  const na = ang(needle);
  g.lineCap = 'round'; g.strokeStyle = C.Accent; g.lineWidth = 9 * k; g.beginPath();
  g.moveTo(cx + Math.cos(na + Math.PI) * 26 * k, cy + Math.sin(na + Math.PI) * 26 * k); g.lineTo(cx + Math.cos(na) * (r - 26 * k), cy + Math.sin(na) * (r - 26 * k)); g.stroke();
  g.lineCap = 'butt';
  g.beginPath(); g.arc(cx, cy, 16 * k, 0, 7); g.fillStyle = 'rgb(60,66,78)'; g.fill();
}

/** อาการตามส่วนผสม (lambda): [ข้อความ, สี] หรือ null ถ้าปกติ */
function mixSymptom(lm) {
  if (lm > 1.40) return ['บางมาก: ติดไม่ครบ สะดุด/ดับ', C.Red];
  if (lm > 1.22) return ['บาง: แห้ง ๆ กระตุก เร่งไม่ขึ้น', C.Red];
  if (lm > 1.10) return ['บางเล็กน้อย: เครื่องแห้ง ร้อน', C.Yellow];
  if (lm < 0.60) return ['หนามาก: สะดุด ควันดำ อาจดับ', C.Red];
  if (lm < 0.72) return ['หนา: อืด สะดุด ควันดำ', C.Red];
  if (lm < 0.80) return ['หนาเล็กน้อย: อืด เสียงทุ้มอุ้ม', C.Yellow];
  return null;
}
function drawDynoResult() {
  const g = ui.g;
  let runs = dynoRuns.filter(r => r.visible);
  if (!runs.length && dynoRuns.length) { dynoRuns[dynoRuns.length - 1].visible = true; runs = [dynoRuns[dynoRuns.length - 1]]; }
  ui.hit(R(0, 0, VW, VH), () => { showDyno = false; });
  g.fillStyle = 'rgba(0,0,0,0.8)'; g.fillRect(0, 0, VW, VH);
  const box = R(70, 24, 1780, 1032);
  ui.fillRR(box, 4, 'rgba(15,18,24,0.99)'); ui.drawRR(box, 4, C.Border, 1.5);
  ui.hit(box, () => { });
  ui.rect(box.x, box.y, box.w, 84, 'rgb(8,11,18)'); ui.rect(box.x, box.y + 84, box.w, 4, 'rgb(0,122,224)');
  ui.txt('REV LAB  DYNO REPORT', F.title, '#fff', box.x + 36, box.y + 14);
  const last = dynoRuns[dynoRuns.length - 1], pad2 = v => String(v).padStart(2, '0');
  ui.txtR('Rev Lab Dyno', F.md, '#fff', box.x + box.w - 36, box.y + 12);
  ui.txtR(`${last.when.getFullYear()}-${pad2(last.when.getMonth() + 1)}-${pad2(last.when.getDate())}  ${pad2(last.when.getHours())}:${pad2(last.when.getMinutes())}`, F.sm, C.Dim, box.x + box.w - 36, box.y + 48);
  const ix = box.x + 48, iy = box.y + 112;
  const info = (label, val, x, w) => { ui.txt(label, F.cap, C.Dim, x, iy); ui.txt(val, F.sm, C.Text, x, iy + 26); ui.line(x, iy + 64, x + w, iy + 64, C.Border, 1); };
  info('VEHICLE', last.vehicle, ix, 520); info('ENGINE', last.spec, ix + 560, 560);
  info('ROTATING INERTIA', `${last.inertia.toFixed(4)} kg·m²`, ix + 1160, 280);
  info('CALIBRATION', Math.abs(last.calK - 1) < 0.005 ? 'none (x1.000)' : `CalK x${last.calK.toFixed(3)}`, ix + 1480, 250);

  const sorted = [...runs].sort((p, q) => p.n - q.n), newest = sorted[sorted.length - 1], oldest = sorted[0], two = newest.n !== oldest.n;
  const hpN = newest.r.ps * PsToHp, hpO = oldest.r.ps * PsToHp, dP = hpN - hpO, dT = newest.r.peakTq - oldest.r.peakTq, cy = box.y + 200, ch = 120, cw = 404, gc = 'rgb(40,170,90)';
  const card = (x, title, big, unit, sub, accent, bigCol) => {
    const r = R(x, cy, cw, ch); ui.fillRR(r, 5, 'rgb(22,26,34)'); ui.drawRR(r, 5, C.Border, 1); ui.rect(r.x, r.y + 8, 5, r.h - 16, accent);
    ui.txt(title, F.cap, C.Dim, r.x + 24, r.y + 10); ui.txt(big, F.numL, bigCol, r.x + 22, r.y + 20);
    ui.txt(unit, F.sm, C.Dim, r.x + 30 + 40 * big.length, r.y + 62); ui.txt(sub, F.cap, C.Dim, r.x + 24, r.y + r.h - 26);
  };
  const sg = v => (v >= 0 ? '+' : '') + v.toFixed(1);
  card(box.x + 48, 'PEAK POWER', f1(hpN), 'HP', `@ ${fmtN(newest.r.psRpm)} rpm  (Run ${newest.n})`, C.Green, C.Green);
  card(box.x + 48 + 436, 'PEAK TORQUE', f1(newest.r.peakTq), 'N·m', `@ ${fmtN(newest.r.tqRpm)} rpm  (Run ${newest.n})`, C.Red, C.Red);
  card(box.x + 48 + 872, two ? `Run ${newest.n} vs Run ${oldest.n}  POWER` : 'POWER (เทียบ)', two ? sg(dP) : '-', two ? 'HP' : '', two ? `${dP >= 0 ? 'ดีขึ้น' : 'แย่ลง'} ${(hpO > 0 ? dP / hpO * 100 : 0).toFixed(1)}%` : 'เปิดอย่างน้อย 2 รอบ', C.Border, two ? (dP >= 0 ? gc : C.Red) : C.Dim);
  card(box.x + 48 + 1308, 'TORQUE', two ? sg(dT) : '-', two ? 'N·m' : '', two ? `${dT >= 0 ? 'ดีขึ้น' : 'แย่ลง'} ${(oldest.r.peakTq > 0 ? dT / oldest.r.peakTq * 100 : 0).toFixed(1)}%` : '', C.Border, two ? (dT >= 0 ? gc : C.Red) : C.Dim);

  const plot = R(box.x + 100, box.y + 392, box.w - 200, 236), hpOf = (r, i) => (Number.isNaN(r.curve[i]) ? NaN : Hp(r.curve[i], i * 100));
  let iMin = 1e9, iMax = -1, maxHp = 1, maxTq = 1;
  for (const run of runs) run.r.curve.forEach((t, i) => { if (Number.isNaN(t)) return; iMin = Math.min(iMin, i); iMax = Math.max(iMax, i); maxHp = Math.max(maxHp, hpOf(run.r, i)); maxTq = Math.max(maxTq, t); });
  if (iMax < 0) { iMin = 20; iMax = 100; }
  const xMin = Math.floor(iMin * 100 / 500) * 500, xMax = Math.max(xMin + 1000, Math.ceil(iMax * 100 / 500) * 500), hpStep = niceStep(maxHp * 1.08), tqStep = niceStep(maxTq * 1.08);
  const XP = rpm => plot.x + (rpm - xMin) / (xMax - xMin) * plot.w, gHp = 'rgb(70,200,120)', gTq = 'rgb(235,90,100)';
  ui.fillRR(plot, 3, 'rgb(14,17,22)'); ui.drawRR(plot, 3, C.Border, 1);
  ui.txt('HP', F.cap, gHp, plot.x - 60, plot.y - 30); ui.txt('N·m', F.cap, gTq, plot.x + plot.w + 14, plot.y - 30);
  for (let k = 0; k <= 5; k++) {
    const y = plot.y + plot.h - k / 5 * plot.h;
    ui.line(plot.x, y, plot.x + plot.w, y, 'rgb(34,40,50)', 1); ui.txtR(String(k * hpStep), F.xs, gHp, plot.x - 10, y - 12); ui.txt(String(k * tqStep), F.xs, gTq, plot.x + plot.w + 10, y - 12);
  }
  const xs = xMax - xMin > 6000 ? 1000 : 500;
  for (let r = xMin; r <= xMax + 1; r += xs) { const x = XP(r); ui.line(x, plot.y, x, plot.y + plot.h, 'rgb(34,40,50)', 1); ui.txtC(String(r), F.xs, C.Dim, x, plot.y + plot.h + 18); }
  ui.txtC('RPM', F.xs, C.Dim, plot.x + plot.w / 2, plot.y + plot.h + 42);
  for (const run of runs) {
    const c = RunColors[(run.n - 1) % RunColors.length], ph = [], pt = [];
    run.r.curve.forEach((t, i) => { if (Number.isNaN(t)) return; const x = XP(i * 100); ph.push([x, plot.y + plot.h - clamp(hpOf(run.r, i) / (hpStep * 5), 0, 1) * plot.h]); pt.push([x, plot.y + plot.h - clamp(t / (tqStep * 5), 0, 1) * plot.h]); });
    const stroke = (pts, w, dash) => { if (pts.length < 2) return; g.strokeStyle = c; g.lineWidth = w; g.lineJoin = 'round'; g.setLineDash(dash); g.beginPath(); pts.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y))); g.stroke(); g.setLineDash([]); };
    stroke(pt, 2.4, [4, 3]); stroke(ph, 3.4, []);
    const px = XP(run.r.psRpm), py = plot.y + plot.h - clamp(run.r.ps * PsToHp / (hpStep * 5), 0, 1) * plot.h;
    g.beginPath(); g.arc(px, py, 7, 0, 7); g.fillStyle = 'rgb(15,18,24)'; g.fill(); g.strokeStyle = c; g.lineWidth = 3; g.stroke();
    ui.txtC(f1(run.r.ps * PsToHp), F.sm, c, px, py + ((run.n % 2) ? 12 : -24));
  }
  let lx = plot.x + 24; const ly = plot.y + 22;
  for (const run of sorted) { const c = RunColors[(run.n - 1) % RunColors.length]; ui.line(lx, ly, lx + 34, ly, c, 4); ui.txt(`Run ${run.n}`, F.xs, C.Text, lx + 44, ly - 13); lx += 150; }
  ui.line(lx + 20, ly, lx + 54, ly, C.Dim, 2.4); ui.txt('HP', F.xs, C.Dim, lx + 62, ly - 13);
  g.strokeStyle = C.Dim; g.lineWidth = 2.4; g.setLineDash([4, 3]); g.beginPath(); g.moveTo(lx + 120, ly); g.lineTo(lx + 154, ly); g.stroke(); g.setLineDash([]); ui.txt('N·m', F.xs, C.Dim, lx + 162, ly - 13);

  let ty = plot.y + plot.h + 72;
  ui.rect(box.x + 48, ty, box.w - 96, 30, 'rgb(24,28,37)');
  const cxs = [box.x + 90, box.x + 540, box.x + 860, box.x + 1150, box.x + 1490, box.x + 1660];
  ['RUN', 'PEAK POWER (HP)', '@ RPM', 'PEAK TORQUE (N·m)', '@ RPM', 'SAMPLES'].forEach((h, i) => ui.txt(h, F.cap, C.Dim, cxs[i], ty + 5));
  ty += 36;
  for (const run of dynoRuns) {
    const c = RunColors[(run.n - 1) % RunColors.length], row = R(box.x + 48, ty, box.w - 96, 30), tc = run.visible ? C.Text : C.Dim;
    if (!run.visible) ui.rect(row.x, row.y, row.w, row.h, 'rgb(14,16,21)');
    ui.rect(box.x + 60, ty + 7, 16, 16, run.visible ? c : 'rgb(70,76,88)');
    ui.txt(`Run ${run.n}   ${run.vehicle}`, F.xs, tc, cxs[0], ty + 3); ui.txt(f1(run.r.ps * PsToHp), F.xs, tc, cxs[1], ty + 3); ui.txt(fmtN(run.r.psRpm), F.xs, C.Dim, cxs[2], ty + 3);
    ui.txt(f1(run.r.peakTq), F.xs, tc, cxs[3], ty + 3); ui.txt(fmtN(run.r.tqRpm), F.xs, C.Dim, cxs[4], ty + 3); ui.txt(String(run.r.curve.filter(t => !Number.isNaN(t)).length), F.xs, C.Dim, cxs[5], ty + 3);
    ui.line(row.x, row.y + row.h, row.x + row.w, row.y + row.h, C.Border, 1);
    ui.hit(row, () => { run.visible = !run.visible; if (!dynoRuns.some(q => q.visible)) run.visible = true; });
    ty += 31;
  }
  ui.btn(R(box.x + box.w - 48 - 190, ty + 6, 190, 34), 'ล้างประวัติรอบวัด', false, C.Red, () => { const keep = dynoRuns[dynoRuns.length - 1]; dynoRuns.length = 0; dynoRuns.push(keep); keep.visible = true; }, F.cap);
  ui.txt('คลิกแถวในตารางเพื่อเปิด/ปิดเส้นของรอบนั้น  •  เส้นทึบ = HP  เส้นประ = N·m', F.cap, C.Dim, box.x + 52, ty + 14);
  const ny = ty + 58; ui.rect(box.x + 40, ny + 2, 4, 20, C.Yellow); ui.txt('NOTES', F.cap, C.Dim, box.x + 56, ny);
  const notes = [];
  if (two) {
    const o = oldest.r, n = newest.r, rpmA = [], h1 = [], h0 = [];
    for (let i = 0; i < Math.min(o.curve.length, n.curve.length); i++) { if (Number.isNaN(n.curve[i])) continue; rpmA.push(i * 100); h1.push(hpOf(n, i)); h0.push(hpOf(o, i)); }
    chartZones(rpmA, h1, h0).slice(0, 3).forEach(z => notes.push([`Run ${newest.n} เทียบ Run ${oldest.n}: ช่วง ${(z.lo / 1000).toFixed(1)}–${(z.hi / 1000).toFixed(1)}k rpm กำลัง${z.good ? 'เพิ่ม' : 'ลด'}สูงสุด ${sg(z.peak)} HP`, z.good ? C.Green : C.Red]));
    if (!notes.length) notes.push(['สองรอบต่างกันน้อยมากในทุกช่วงรอบ', C.Dim]);
    if (newest.r.psRpm !== oldest.r.psRpm) notes.push([`พีคกำลังย้ายจาก ${fmtN(oldest.r.psRpm)} ไป ${fmtN(newest.r.psRpm)} rpm`, C.Dim]);
  } else notes.push(['วิ่งไดโนอีกครั้งหลังแก้ค่า แล้วจะเทียบกับรอบนี้ให้ทั้งกราฟและตาราง', C.Dim]);
  const rd = newest.r.model.D;
  if (rd.RealHp > 0 || rd.RealTq > 0) {
    let cmp = 'เทียบรถจริง:  ';
    if (rd.RealHp > 0) cmp += `แรงม้า เกม ${f1(hpN)} / จริง ${f1(rd.RealHp)} HP (${((hpN / rd.RealHp - 1) * 100).toFixed(0)}%)   `;
    if (rd.RealTq > 0) cmp += `แรงบิด เกม ${f1(newest.r.peakTq)} / จริง ${f1(rd.RealTq)} N·m (${((newest.r.peakTq / rd.RealTq - 1) * 100).toFixed(0)}%)`;
    notes.push([cmp, C.Yellow]);
  }
  const nMax = Math.max(1, Math.min(3, Math.floor((box.y + box.h - 52 - (ny + 30)) / 28)));
  notes.slice(0, nMax).forEach(([t, c], i) => ui.txt(t, F.xs, c, box.x + 60, ny + 30 + i * 28));
  ui.txtR('Generated by REV LAB (simulated dyno)  •  Enter / Esc / คลิกนอกรายงานเพื่อปิด', F.cap, C.Dim, box.x + box.w - 36, box.y + box.h - 28);
}

// ================================================================= สรุปการแก้ไข (เทียบเครื่องที่ติดตั้งอยู่)
let showReport = false, repData = null;
function openReport() {
  const m0 = installed, m1 = preview, ch = [], notes = [];
  for (const p of Params) {
    if (p.key === 'seal' || p.key === 'gtop' || p.key === 'psi') continue;
    const a = p.get(m0.D), b = p.get(m1.D);
    if (typeof a === 'number' && Math.abs(a - b) > Math.max(1e-5, (p.step || 0) * 0.01)) ch.push([p.name, paramDisplay(p, m0.D), paramDisplay(p, m1.D)]);
  }
  const tab = (name, f0, f1, tol) => { let n = 0; for (let r = 0; r < EcuN; r++) for (let c = 0; c < EcuN; c++) if (Math.abs(f0(c, r) - f1(c, r)) > tol) n++; if (n) ch.push([name, 'ค่าเดิม', `แก้ ${n} ช่อง`]); };
  tab('ตารางน้ำมัน (ms)', (c, r) => m0.CellMs(c, r), (c, r) => m1.CellMs(c, r), 0.005);
  tab('ตารางองศาฉีด', (c, r) => m0.CellEoi(c, r), (c, r) => m1.CellEoi(c, r), 0.5);
  tab('ตารางองศาจุด', (c, r) => m0.CellAdv(c, r), (c, r) => m1.CellAdv(c, r), 0.05);
  tab('ตารางไทม์คอยล์', (c, r) => m0.CellDwell(c, r), (c, r) => m1.CellDwell(c, r), 0.005);
  tab('ตาราง MAP', (c, r) => m0.CellMap(c, r), (c, r) => m1.CellMap(c, r), 0.5);
  const p0 = Adv.predict(m0), p1 = Adv.predict(m1), auto = m1.D.Auto === 1;
  const ra = Math.round(m1.D.Limiter * 0.35 / 100) * 100, rb = Math.round(m1.D.Limiter * 0.80 / 100) * 100;
  const up0 = m0.RevUpTime(ra, rb), up1 = m1.RevUpTime(ra, rb), h0 = auto ? Cvt.holdRpm(m0) : 0, h1 = auto ? Cvt.holdRpm(m1) : 0;
  const rpmA = [], hp = [], hp0 = [];
  for (let r = 2000; r <= m1.D.Limiter; r += 100) { rpmA.push(r); hp.push(Math.max(0, Hp(m1.BrakeTorqueWot(r), r))); hp0.push(r <= m0.D.Limiter ? Math.max(0, Hp(m0.BrakeTorqueWot(r), r)) : NaN); }
  const zones = chartZones(rpmA, hp, hp0); let maxD = 0; for (let i = 0; i < hp.length; i++) if (!Number.isNaN(hp0[i])) maxD = Math.max(maxD, Math.abs(hp[i] - hp0[i]));
  if (!ch.length) notes.push(['ยังไม่ได้แก้อะไรจากเครื่องที่ติดตั้งอยู่', 0]);
  else {
    const dh = m1.PeakHp - m0.PeakHp, dt = m1.PeakTq - m0.PeakTq, sg = v => (v >= 0 ? '+' : '') + v.toFixed(1);
    if (maxD < 0.05) notes.push(['กราฟแรงม้า/แรงบิดของตัวเครื่องไม่เปลี่ยน — สิ่งที่แก้อยู่ที่ชุดส่งกำลัง/ตัวรถ (ชามเม็ด เม็ด สปริง เฟือง ล้อ น้ำหนัก ฯลฯ) ผลจึงดูที่เวลาวิ่งและรอบที่ CVT ถือ', 0]);
    else {
      notes.push([`กำลังสูงสุด ${f1(m0.PeakHp)} → ${f1(m1.PeakHp)} HP (${sg(dh)}) @ ${fmtN(m1.PeakPsRpm)} rpm   แรงบิดสูงสุด ${f1(m0.PeakTq)} → ${f1(m1.PeakTq)} N·m (${sg(dt)})`, dh >= 0.05 ? 1 : dh < -0.05 ? 2 : 0]);
      zones.slice(0, 3).forEach(z => notes.push([`ช่วง ${(z.lo / 1000).toFixed(1)}–${(z.hi / 1000).toFixed(1)}k rpm กำลัง${z.good ? 'เพิ่ม' : 'ลด'}สูงสุด ${sg(z.peak)} HP`, z.good ? 1 : 2]));
      if (!zones.length) notes.push(['ต่างกันนิดเดียวทุกช่วงรอบ (ไม่ถึงเกณฑ์บอกเป็นช่วง)', 0]);
      if (m1.PeakPsRpm !== m0.PeakPsRpm) notes.push([`พีคกำลังย้ายจาก ${fmtN(m0.PeakPsRpm)} ไป ${fmtN(m1.PeakPsRpm)} rpm (${m1.PeakPsRpm > m0.PeakPsRpm ? 'ไปรอบสูงขึ้น' : 'ลงมารอบต่ำ'})`, 0]);
    }
    const d402 = p1.t402 - p0.t402;
    notes.push([`402 ม. ${p0.t402.toFixed(2)} → ${p1.t402.toFixed(2)} วิ (${d402 <= 0 ? 'เร็วขึ้น ' : 'ช้าลง '}${Math.abs(d402).toFixed(2)})   0–60 ${p0.t060.toFixed(2)} → ${p1.t060.toFixed(2)}   ท็อป ${f0(p0.top)} → ${f0(p1.top)} กม./ชม.`, d402 < -0.02 ? 1 : d402 > 0.02 ? 2 : 0]);
    if (auto && Math.abs(h1 - h0) > 20) { const pk = m1.PeakPsRpm, nearer = Math.abs(h1 - pk) < Math.abs(h0 - pk); notes.push([`CVT ถือรอบ ${fmtN(h0)} → ${fmtN(h1)} rpm (พีคกำลัง ${fmtN(pk)}) = ${nearer ? 'ใกล้พีคขึ้น ดี' : 'ห่างพีคขึ้น แย่ลง'}`, nearer ? 1 : 2]); }
    if (m1.KnockWot(3500) > 0.02 && m0.KnockWot(3500) <= 0.02) notes.push(['เริ่มน็อกที่โหลดสูง (กำลังอัดสูงเกินน้ำมัน) — ลด CR/องศาจุด หรือใช้ออกเทนสูงขึ้น', 2]);
    if (m1.D.Limiter >= m1.FloatRpm && m0.D.Limiter < m0.FloatRpm) notes.push(['รอบตัดสูงกว่ารอบวาล์วลอยแล้ว กำลังจะตกที่ปลายรอบ', 2]);
    if (Number.isFinite(up1) && Number.isFinite(up0) && Math.abs(up1 - up0) > 0.03) notes.push([`เร่งรอบเครื่องเปล่า ${up0.toFixed(2)} → ${up1.toFixed(2)} วิ (${up1 < up0 ? 'ไวขึ้น' : 'ยานขึ้น'})`, 0]);
  }
  repData = { m0, m1, ch, notes, p0, p1, up0, up1, h0, h1, auto };
  showReport = true;
}
function drawReport() {
  const g = ui.g, d = repData; g.fillStyle = 'rgba(6,8,11,0.91)'; g.fillRect(0, 0, VW, VH);
  const box = R(200, 40, 1520, 1000);
  ui.fillRR(box, 6, C.Panel); ui.drawRR(box, 6, C.Accent, 2);
  ui.txt('สรุปการแก้ไข  (เทียบเครื่องที่ติดตั้งอยู่)', F.title, C.Accent, box.x + 36, box.y + 18);
  ui.txtR('คลิกที่ไหนก็ได้ / Esc / R เพื่อปิด', F.xs, C.Dim, box.x + box.w - 36, box.y + 34);
  const lx = box.x + 36, ly = box.y + 104, lw = 500;
  ui.rect(lx, ly + 2, 4, 24, C.Yellow); ui.txt(`สิ่งที่เปลี่ยน (${d.ch.length} รายการ)`, F.md, C.Text, lx + 16, ly);
  let y = ly + 50; const maxRows = 14;
  d.ch.slice(0, maxRows).forEach(([n, a, b]) => { ui.txt(n, F.cap, C.Dim, lx, y); ui.txt(`${a}  →  ${b}`, F.xs, C.Accent, lx, y + 20); ui.line(lx, y + 50, lx + lw, y + 50, 'rgb(40,46,56)', 1); y += 58; });
  if (d.ch.length > maxRows) ui.txt(`... และอีก ${d.ch.length - maxRows} รายการ`, F.xs, C.Dim, lx, y);
  if (!d.ch.length) ui.txt('ไม่มี', F.sm, C.Dim, lx, y);
  const rx = box.x + 580, rw = 900;
  drawCompareChart(R(rx, box.y + 100, rw, 380), d.m1, d.m1.D.Limiter);
  let ty = box.y + 500;
  ui.txt('ผลเทียบ', F.cap, C.Dim, rx, ty); ui.txtR('ก่อน', F.cap, C.Dim, rx + 470, ty); ui.txtR('หลัง', F.cap, C.Dim, rx + 640, ty); ui.txtR('ต่าง', F.cap, C.Dim, rx + rw, ty);
  ty += 26;
  const row = (name, a, b, diff, lowerBetter, dec) => {
    ui.txt(name, F.xs, C.Text, rx, ty); ui.txtR(a, F.xs, C.Dim, rx + 470, ty); ui.txtR(b, F.xs, C.Text, rx + 640, ty);
    const same = Math.abs(diff) < 0.0049, good = lowerBetter ? diff < 0 : diff > 0;
    ui.txtR(same ? '-' : (diff > 0 ? '▲ +' : '▼ ') + diff.toFixed(dec), F.xs, same ? C.Dim : good ? C.Green : C.Red, rx + rw, ty); ty += 28;
  };
  const { m0, m1, p0, p1 } = d;
  row('กำลังสูงสุด (HP)', f1(m0.PeakHp), f1(m1.PeakHp), m1.PeakHp - m0.PeakHp, false, 1);
  row('แรงบิดสูงสุด (N·m)', f1(m0.PeakTq), f1(m1.PeakTq), m1.PeakTq - m0.PeakTq, false, 1);
  row('0–60 กม./ชม. (วิ)', p0.t060.toFixed(2), p1.t060.toFixed(2), p1.t060 - p0.t060, true, 2);
  row('100 ม. (วิ)', p0.t100.toFixed(2), p1.t100.toFixed(2), p1.t100 - p0.t100, true, 2);
  row('402 ม. (วิ)', p0.t402.toFixed(2), p1.t402.toFixed(2), p1.t402 - p0.t402, true, 2);
  row('ความเร็วปลาย 402 ม.', f0(p0.trap), f0(p1.trap), p1.trap - p0.trap, false, 0);
  row('ท็อปสปีด (กม./ชม.)', f0(p0.top), f0(p1.top), p1.top - p0.top, false, 0);
  if (d.auto) row('CVT ถือรอบ (rpm)', fmtN(d.h0), fmtN(d.h1), d.h1 - d.h0, false, 0);
  { const t = v => (Number.isFinite(v) ? v.toFixed(2) : '∞'); row('เร่งรอบเครื่องเปล่า (วิ)', t(d.up0), t(d.up1), Number.isFinite(d.up0) && Number.isFinite(d.up1) ? d.up1 - d.up0 : 0, true, 2); }
  ty += 12; ui.rect(rx, ty + 2, 4, 22, C.Yellow); ui.txt('สรุป', F.md, C.Text, rx + 16, ty); ty += 40;
  for (const [t, st] of d.notes) {
    const col = st === 1 ? C.Green : st === 2 ? C.Red : C.Dim, yy = ui.wrap((st === 1 ? '▲ ' : st === 2 ? '▼ ' : '• ') + t, F.xs, col, rx, ty, rw);
    ty = yy + 6; if (ty > box.y + box.h - 40) break;
  }
  ui.hit(R(0, 0, VW, VH), () => { showReport = false; });
}

// ================================================================= อู่
const GL = 252, GW = 896;
let gTab = 0, gSel = 0, gPartRow = 0, advisorOn = false;
const advC = Adv.newConstraints();
let advGoal = 0, advKey = '', advTips = [], advList = [];

const vis = () => { const keys_ = GarageTabs[gTab][1]; return Params.filter(p => keys_.includes(p.key) && (!p.visible || p.visible(draft))); };
function draftChanged() { preview = new EngineModel(draft); if (benchApplied) benchDirty = true; }
function setTab(t) { advisorOn = false; gTab = (t + GarageTabs.length) % GarageTabs.length; gSel = 0; }
function nudgeRow(i, dir, coarse) { const v = vis(); if (i < 0 || i >= v.length) return; nudge(v[i], draft, dir, coarse); draftChanged(); }
function partStep(row, dir) {
  gPartRow = row;
  if (row === 0) {
    const sets = Presets, curI = sets.findIndex(s => JSON.stringify(s[1]) === JSON.stringify(draft)), n = sets.length;
    const ni = curI < 0 ? (dir > 0 ? 0 : n - 1) : (curI + dir + n) % n;
    draft = { ...sets[ni][1] }; draftChanged(); toast('เช็ตเครื่อง: ' + sets[ni][0]); return;
  }
  const cat = PartCats[row - 1], c = partCurrent(cat, draft), n = cat.items.length;
  cat.items[c < 0 ? (dir > 0 ? 0 : n - 1) : clamp(c + dir, 0, n - 1)].apply(draft); draftChanged();
}
function autoTuneRollers() {
  if (draft.Auto !== 1) return toast('จูนเม็ดอัตโนมัติใช้กับรถออโต้เท่านั้น');
  const { g, hold } = Cvt.suggestRoller(preview);
  draft.RollerG = g; draft.RollerG2 = g; draftChanged();
  toast(`จูนเม็ดเป็น ${g} กรัม ทุกเม็ด  CVT ถือรอบ ~${fmtN(hold)} rpm (พีคกำลัง ${fmtN(preview.PeakPsRpm)})${g <= 4 ? ' (ถึงขอบเขตเบาสุดแล้ว ลองเพิ่มองศาชาม/ลดจำนวนเม็ด)' : g >= 20 ? ' (ถึงขอบเขตหนักสุดแล้ว)' : ''}`);
}
function calibrateToReal() {
  const k = calibrate(draft);
  if (k == null) return toast('ใส่แรงม้าหรือแรงบิดของรถจริง (แท็บ รถจริง) ก่อน');
  draft.CalK = k; draftChanged(); toast(`ปรับเทียบแล้ว: ตัวคูณ ${f2(k)}  โมเดล ${f1(preview.PeakHp)} HP / ${f1(preview.PeakTq)} N·m`);
}
function loadPreset(i) { draft = { ...Presets[i][1] }; draftChanged(); toast('โหลดแบบ: ' + Presets[i][0]); }
function assemble() {
  const changed = JSON.stringify(draft) !== JSON.stringify(design);
  if (changed) {
    if (runAny(cur)) before = cur;
    cur = newRun(); last060 = last100 = last402 = -1; runDist = -1; peakRpm = 0; topSession = 0; if (dynoRes) dynoPrev = dynoRes; dynoRes = null;
  }
  design = { ...draft }; save.design = design; persist();
  installed = new EngineModel(design);
  node?.port.postMessage({ type: 'assemble', design });
  gearSel = 0; roadArmed = false; roadTiming = false;
  goTo('drive');
  toast(design.Auto === 1 ? 'ประกอบแล้ว กด S ค้างเพื่อสตาร์ท แล้วบิดคันเร่งออกตัวได้เลย (ออโต้)' : 'ประกอบแล้ว กด S ค้างเพื่อสตาร์ท แล้วเข้าเกียร์ด้วย E');
}
function garageKey(k, shift) {
  if (advisorOn && ['arrowup', 'arrowdown', 'arrowleft', 'arrowright'].includes(k)) return;
  if (!advisorOn && isEcuTab() && (ecuTypeKey(k) || ecuKey(k, shift))) return;      // แท็บจูนกล่อง: ตัวเลขใช้พิมพ์ใส่ตาราง ไม่ใช้สลับรถ
  const n = vis().length, parts = gTab === 0, prow = PartCats.length + 1;
  switch (k) {
    case 'arrowup': if (parts) gPartRow = (gPartRow + prow - 1) % prow; else if (n > 0) gSel = (gSel + n - 1) % n; break;
    case 'arrowdown': if (parts) gPartRow = (gPartRow + 1) % prow; else if (n > 0) gSel = (gSel + 1) % n; break;
    case 'arrowleft': if (parts) partStep(gPartRow, -1); else nudgeRow(gSel, -1, shift); break;
    case 'arrowright': if (parts) partStep(gPartRow, 1); else nudgeRow(gSel, 1, shift); break;
    case 'tab': setTab(gTab + (shift ? -1 : 1)); break;
    case 't': advisorOn = !advisorOn; advKey = ''; break;
    case 'r': openReport(); break;
    case 'c': calibrateToReal(); break;
    case 'a': autoTuneRollers(); break;
    case 'enter': assemble(); break;
    case 'g': goTo('drive'); break;
    case 'escape': goTo('menu'); break;
  }
}

// ================================================================= แท็บ "จูนกล่อง": ตาราง ECU 20x20 (น้ำมัน % / องศาจุด / AFR)
let ecuMode = 0, ecuC0 = 0, ecuR0 = 19, ecuC1 = 0, ecuR1 = 19;
const EcuModeNames = ['น้ำมัน ms', 'องศาฉีด °', 'องศาจุด °', 'ไทม์คอยล์ ms', 'AFR', 'MAP kPa'];
const isEcuTab = () => GarageTabs[gTab][0] === 'จูนกล่อง';
const mixC = (a, b, t) => { t = clamp(t, 0, 1); return `rgb(${Math.round(a[0] + (b[0] - a[0]) * t)},${Math.round(a[1] + (b[1] - a[1]) * t)},${Math.round(a[2] + (b[2] - a[2]) * t)})`; };
const ecuRange = () => ({ c0: Math.min(ecuC0, ecuC1), c1: Math.max(ecuC0, ecuC1), r0: Math.min(ecuR0, ecuR1), r1: Math.max(ecuR0, ecuR1) });
// ตารางเป็นแบบ copy-on-write เสมอ (ไม่ให้ไปแก้ตารางของเครื่องที่ติดตั้งแล้ว)
// โหมด: 0 น้ำมัน ms, 1 องศาฉีด (EOI), 2 องศาจุด, 3 ไทม์คอยล์, 4 AFR (แก้ที่ตารางน้ำมัน), 5 MAP (อ่านอย่างเดียว)
const ECU_PROP = ['EcuMs', 'EcuEoi', 'EcuIgn', 'EcuDwell', 'EcuMs', 'EcuMap'];
const ECU_SPEC = [[0.1, 0.5, 0, 40, 0.01], [5, 20, 0, 560, 1], [0.5, 2, -5, 50, 0.1], [0.1, 0.5, 0.5, 9, 0.01], [0.1, 0.5, 0, 40, 0.01], [1, 5, 0, 115, 1]];     // [ละเอียด, หยาบ, ต่ำสุด, สูงสุด, ปัดเศษ]
const ecuFactory = mode => [preview.FactoryMsTable(), preview.FactoryEoiTable(), preview.FactoryIgnTable(), preview.FactoryDwellTable(), preview.FactoryMsTable(), preview.FactoryMapTable()][mode];
const ecuTableW = mode => { const p = ECU_PROP[mode]; draft.EcuAx = 2; return (draft[p] = (draft[p] || ecuFactory(mode)).slice()); };
const editMode = () => (ecuMode === 4 ? 0 : ecuMode);
const ecuReadOnly = () => false;
function ecuAdjust(dir, coarse) {
  if (ecuReadOnly()) return;
  const { c0, c1, r0, r1 } = ecuRange(), m = preview;
  if (ecuMode === 4) {
    const t = ecuTableW(0);
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) {      // โหมด AFR: ตั้ง AFR เป้าหมายของช่อง แล้วแปลงเป็นเวลาฉีด (ms)
      const rpmC = m.EcuRpmAt(c), tpsR = EngineModel.TpsAxis(r), loadR = m._loadOf(tpsR, rpmC), cur = m.EcuAt(rpmC, tpsR, {}).Afr;
      t[r * EcuN + c] = clamp(Math.round(m.MsForAfr(rpmC, loadR, cur + (coarse ? 0.5 : 0.1) * dir) * 100) / 100, 0, 40);
    }
  } else {
    const [fine, crs, lo, hi, rnd] = ECU_SPEC[ecuMode], t = ecuTableW(ecuMode), st = (coarse ? crs : fine) * dir;
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) t[r * EcuN + c] = clamp(Math.round((t[r * EcuN + c] + st) / rnd) * rnd, lo, hi);
  }
  draftChanged();
}
function ecuEdit(fn) {
  if (ecuReadOnly()) return;
  const { c0, c1, r0, r1 } = ecuRange(), t = ecuTableW(editMode());
  fn(t, c0, c1, r0, r1); draftChanged();
}
function ecuSmooth() {
  const rnd = ECU_SPEC[editMode()][4];
  ecuEdit((t, c0, c1, r0, r1) => {
    const src = t.slice();
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) {
      let s = 0, n = 0;
      for (let dr = -1; dr <= 1; dr++) for (let dc = -1; dc <= 1; dc++) { const rr = r + dr, cc = c + dc; if (rr < 0 || cc < 0 || rr >= EcuN || cc >= EcuN) continue; s += src[rr * EcuN + cc]; n++; }
      t[r * EcuN + c] = Math.round(s / n / rnd) * rnd;
    }
  });
  if (ecuMode !== 5) toast('เกลี่ยค่าที่เลือกแล้ว');
}
function ecuEqualize() { ecuEdit((t, c0, c1, r0, r1) => { const v = t[ecuR0 * EcuN + ecuC0]; for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) t[r * EcuN + c] = v; }); }
function ecuFactorySel() { if (ecuReadOnly()) return; const fac = ecuFactory(editMode()); ecuEdit((t, c0, c1, r0, r1) => { for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) t[r * EcuN + c] = fac[r * EcuN + c]; }); }
function ecuFactoryAll() { if (ecuReadOnly()) return; draft[ECU_PROP[editMode()]] = null; draftChanged(); toast('คืนตารางเป็นค่าโรงงานแล้ว'); }
function ecuNearMbt() {
  if (ecuReadOnly()) return;
  const m = preview, t = ecuTableW(2), { c0, c1, r0, r1 } = ecuRange();
  for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) t[r * EcuN + c] = Math.round((m.EcuAt(m.EcuRpmAt(c), EngineModel.TpsAxis(r), {}).Mbt - 1) * 2) / 2;
  draftChanged();
  toast('ตั้งองศาใกล้ MBT แล้ว (ระวังน็อกถ้ากำลังอัดสูง/ออกเทนต่ำ)');
}
// ---------- พิมพ์ตัวเลขใส่ตาราง ----------
let ecuInput = '';
/** ใส่ค่าที่พิมพ์ลงช่องที่เลือก (คลุมช่วงแล้วพิมพ์ Enter)  โหมด AFR = ตั้ง AFR เป้าหมาย  MAP = kPa */
function ecuApplyValue(v) {
  const { c0, c1, r0, r1 } = ecuRange(), m = preview;
  if (ecuMode === 4) {
    const t = ecuTableW(0);
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) { const rpmC = m.EcuRpmAt(c), tpsR = EngineModel.TpsAxis(r); t[r * EcuN + c] = clamp(Math.round(m.MsForAfr(rpmC, m._loadOf(tpsR, rpmC), Math.max(v, 1)) * 100) / 100, 0, 40); }
  } else {
    const [, , lo, hi, rnd] = ECU_SPEC[ecuMode], t = ecuTableW(ecuMode), val = clamp(Math.round(v / rnd) * rnd, lo, hi);
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) t[r * EcuN + c] = val;
  }
  draftChanged();
  toast(`ใส่ค่า ${+v.toFixed(2)} ในช่องที่เลือก ${(c1 - c0 + 1) * (r1 - r0 + 1)} ช่องแล้ว`);
}
/** พิมพ์ตัวเลข: 0-9 . - ใส่ในกล่อง  Enter ใช้ค่า  Esc ยกเลิก  Backspace ลบ (เลขไม่ไปสลับรถ) */
function ecuTypeKey(k) {
  if (/^[0-9.-]$/.test(k)) {
    if (k === '.' && ecuInput.includes('.')) return true;
    if (k === '-' && ecuInput.length > 0) return true;
    if (ecuInput.length < 7) ecuInput += k;
    return true;
  }
  if (!ecuInput) return false;
  if (k === 'backspace') { ecuInput = ecuInput.slice(0, -1); return true; }
  if (k === 'escape') { ecuInput = ''; return true; }
  if (k === 'enter') { const v = parseFloat(ecuInput); if (Number.isFinite(v)) ecuApplyValue(v); ecuInput = ''; return true; }
  return false;
}
function ecuKey(k, shift) {
  // แกนนอน = โหลด/คันเร่ง (ซ้าย-ขวา)  แกนตั้ง = รอบ (ขึ้น-ลง)
  const mv = (dc, dr) => { ecuC1 = clamp(ecuC1 + dc, 0, EcuN - 1); ecuR1 = clamp(ecuR1 + dr, 0, EcuN - 1); if (!shift) { ecuC0 = ecuC1; ecuR0 = ecuR1; } return true; };
  switch (k) {
    case 'arrowleft': return mv(0, -1);
    case 'arrowright': return mv(0, 1);
    case 'arrowup': return mv(-1, 0);
    case 'arrowdown': return mv(1, 0);
    case ']': case 'pageup': ecuAdjust(1, shift || k === 'pageup'); return true;
    case '[': case 'pagedown': ecuAdjust(-1, shift || k === 'pagedown'); return true;
    case 'k': send('ignition', !S.ignition); return true;
    case 'e': if (benchApplied) shiftGear(1); return true;
    case 'q': if (benchApplied) shiftGear(-1); return true;
    case 'n': if (benchApplied) selectGear(0); return true;
    case 'f1': case '?': case '/': showEcuHelp = true; return true;
    case 'd': ecuMode = (ecuMode + 1) % EcuModeNames.length; return true;
  }
  return false;
}
let benchOn = false, benchApplied = false, benchDirty = false, showEcuHelp = false, benchTrail = [], liveTab = 0;
const EcuHelp = [
  ['AFR และ λ (แลมบ์ดา)', ['AFR = มวลอากาศ ÷ มวลน้ำมันที่เข้าห้องเผาไหม้  เบนซินเผาหมดพอดี (สโตอิก) = 14.7 : 1', 'λ = AFR ÷ สโตอิก   λ 1.00 = พอดี   ต่ำกว่า 1 = หนา   สูงกว่า 1 = บาง', 'แรงสูงสุดที่ AFR ~12.5–13.2 (λ 0.85–0.90)  ประหยัดตอนวิ่งเบา ~14.7', 'บางเกิน (>15.5) = ร้อน น็อก แรงตก   หนาเกิน (<11) = แรงตก เปลืองน้ำมัน เขม่า', 'E85 สโตอิก 9.76 / เมทานอล 6.45 ต้องฉีดมากกว่าเบนซินหลายเท่า']],
  ['MBT และ ไฟแก่ / ไฟอ่อน', ['MBT = Minimum advance for Best Torque คือองศาจุดระเบิดก่อนศูนย์ตายบน (°BTDC) ที่ได้แรงบิดสูงสุดพอดี', 'ไฟแก่ = จุดเร็ว (องศามาก)   ไฟอ่อน = จุดช้า (องศาน้อย)', 'แก่กว่า MBT = แรงดันพีคมาเร็วเกินจนต้านลูกสูบขาขึ้น แรงไม่เพิ่มแต่น็อกง่าย', 'อ่อนกว่า MBT = แรงตก ไอเสียร้อน', 'MBT สูงขึ้นเมื่อรอบสูง โหลดเบา ส่วนผสมบาง ลูกโต / ต่ำลงเมื่อ CR สูง ก้านยาว น้ำมันเผาไว (E85/เมทานอล)', 'ตั้งต่ำกว่า MBT 1–3° เป็นขอบปลอดภัย']],
  ['EGT (อุณหภูมิก๊าซไอเสีย)', ['EGT = Exhaust Gas Temperature อุณหภูมิก๊าซไอเสียที่คอท่อ บอกว่าเผาไหม้ร้อนแค่ไหน', 'เบนซินเครื่องเล็กตอนวิ่งหนักปกติ ~700–900°C   เกิน ~1,000°C เสี่ยงวาล์วไอเสียไหม้ ลูกสูบหลอม', 'ที่ทำให้สูง: ส่วนผสมบาง, ไฟอ่อน (ไฟยังเผาอยู่ตอนไอเสียเปิด), โหลดและรอบสูง', 'ส่วนผสมหนากว่าเล็กน้อยช่วยลด EGT (น้ำมันระเหยดูดความร้อน) แต่หนาเกินแรงตก', 'ค่าในเกมเป็นการประมาณ ใช้ดูแนวโน้ม']],
  ['หัวฉีด (Duty)', ['Duty = สัดส่วนเวลาที่หัวฉีดเปิดต่อ 1 รอบเครื่อง', 'ยิ่งรอบสูง โหลดหนัก หรือใช้น้ำมันที่ต้องฉีดเยอะ (E85 เมทานอล) ค่ายิ่งสูง', 'เกิน ~85% ใกล้สุดกำลังหัวฉีด   100% = เต็ม น้ำมันไม่พอ ส่วนผสมบางลงเอง (ร้อน แรงตก)', 'แก้ด้วยหัวฉีดที่ใหญ่ขึ้น (แท็บ อัด·น้ำมัน แถว หัวฉีด)', 'ใส่เวลาฉีด 0 ms = ตัดน้ำมันช่องนั้น ไม่มีการเผาไหม้ ตัดช่วงเดินเบาแล้วเครื่องจะเดินหอบเอง (รอบขึ้น-ลงตามระบบคุมรอบเดินเบา) ลดค่าด้วยปุ่ม − จนถึง 0 ได้เอง']],
  ['MAP (แรงดันท่อไอดี)', ['MAP = Manifold Absolute Pressure แรงดันสัมบูรณ์ในท่อไอดี หน่วย kPa  บรรยากาศ ~101 kPa  ยิ่งต่ำยิ่งสุญญากาศลึก', 'ลิ้นเร่งแคบ + รอบสูง = ดูดแรง MAP ต่ำ (~30–40 kPa) อากาศเข้าน้อย โหลดเบา   ลิ้นเปิดเต็ม MAP ใกล้บรรยากาศ', 'ECU ใช้ MAP บอกว่าเครื่องกินอากาศเท่าไร แล้วเลือกเวลาฉีด/องศาจุดตามโหลดนั้น', 'แก้ตารางนี้ได้ = คุมลิ้นเร่งไฟฟ้า: ตั้ง MAP เป้าหมายต่อคันเร่ง (TPS) x รอบ เครื่องจะได้อากาศตามนั้น (ไม่เกินที่ลิ้นเปิดสุดผ่านได้) ใส่ 0 = ไม่มีอากาศเข้า', 'ใช้จำกัดกำลังที่คันเร่งต่ำ หรือทำอาการเดินหอบจากช่วงเดินเบา (เช่นตั้ง MAP ช่อง TPS 0-5% ต่ำเกิน ระบบคุมรอบเดินเบาจะเปิดลิ้นตามจนรอบเหวี่ยง)']],
  ['องศาจบฉีด (EOI)', ['EOI = End Of Injection องศาที่หัวฉีดหยุดฉีด วัดเป็น °ก่อนศูนย์ตายบนของจังหวะดูด (ยิ่งมากยิ่งฉีดเสร็จเร็ว)', 'ฉีดให้เสร็จก่อนวาล์วไอดีเปิด น้ำมันมีเวลาระเหยบนวาล์วปิดที่ร้อน ผสมกับอากาศดี', 'ฉีดตอนวาล์วเปิดแล้ว (EOI น้อยกว่าองศาวาล์วเปิด) ละอองน้ำมันระเหยไม่ทัน กำลังตกเล็กน้อย โดยเฉพาะรอบต่ำ', 'รอบสูงเวลาฉีดยาว ต้องเลื่อน EOI ให้ช้าลงเพื่อฉีดทันในรอบเดียว ถ้าเลยจะฉีดข้ามไปรอบก่อน กำลังตก', 'ฉีดเร็วเกินไป (EOI มาก) น้ำมันเกาะผนังท่อไอดี ตอบสนองคันเร่งช้า']],
  ['ไทม์คอยล์ (Dwell)', ['Dwell = เวลาที่ ECU ปล่อยกระแสชาร์จคอยล์จุดระเบิดก่อนตัดให้เกิดประกายไฟ ยิ่งนานยิ่งเก็บพลังงานมากขึ้นจนอิ่มตัว', 'สั้นเกินไป = ประกายอ่อน จุดไม่ติด โดยเฉพาะโหลดหนัก กำลังอัดสูง ส่วนผสมบาง (ต้องใช้ประกายแรงกว่า)', 'นานเกินไป = คอยล์ร้อน เปลืองกระแส ประสิทธิภาพตก เสี่ยงคอยล์ไหม้ (เครื่องสูบเดียวมีเวลาเหลือเฟือ ปกติ ~2–6 ms)', 'ค่าโรงงานคำนวณให้พลังงานประกายเกินที่ต้องการราว 8%']],
  ['น็อก (Knock)', ['การเผาไหม้ระเบิดเองก่อนเปลวไฟมาถึง (เสียงกริ๊งโลหะ) แรงดันกระแทกลูกสูบ', 'เกิดเมื่อกำลังอัดสูง ออกเทนต่ำ ไฟแก่เกิน ส่วนผสมบาง หรือโหลดหนักรอบต่ำ', 'แก้: ลดองศา เติมน้ำมันเพิ่มเล็กน้อย ใช้ออกเทนสูงขึ้น หรือลด CR', 'ในเกมน็อกทำให้แรงบิดตกและเสียงหยาบ']],
  ['ลำดับจูนที่แนะนำ', ['1) ตั้ง AFR ก่อน: โหลดหนักให้หนาเล็กน้อย (~12.8–13.0) โหลดเบา ~14.7', '2) เดินองศาทีละ 1° ที่โหลดสูง ดูแรงบิดบนกราฟ / น็อก / EGT', '3) แรงบิดหยุดเพิ่ม = ถึง MBT แล้ว ให้ถอยกลับ 2°', '4) ตรวจ Duty ไม่ควรเกิน 85%   5) เกลี่ยตารางให้เรียบ ไม่กระโดด', '6) เปิดเครื่องจูนสด เร่งเครื่อง ดูจุดวิ่งบนตาราง แล้วแก้ช่องที่น็อก/ร้อน']],
];
function liveBar(x, y, w, label, value, frac, col, marker = -1) {
  ui.txt(label, F.xs, C.Dim, x, y); ui.txtR(value, F.xs, col, x + w, y);
  ui.fillRR(R(x, y + 25, w, 8), 2, 'rgb(40,46,56)');
  const bw = clamp(frac, 0, 1) * w; if (bw > 1) ui.fillRR(R(x, y + 25, bw, 8), 2, col);
  if (marker >= 0) ui.rect(x + clamp(marker, 0, 1) * w - 1.5, y + 22, 3, 14, C.Text);
}
/** สเปกตรัมความร้อน: 0 = น้ำเงิน → เขียว → เหลือง → 1 = แดง  คืน [สี, ความสว่าง] */
function heat(t) {
  t = clamp(t, 0, 1);
  const h = 240 * (1 - t) / 60, sat = 0.78, v = 0.88, i = Math.floor(h) % 6, f = h - Math.floor(h);
  const pp = v * (1 - sat), q = v * (1 - sat * f), tt = v * (1 - sat * (1 - f));
  const [R_, G_, B_] = [[v, tt, pp], [q, v, pp], [pp, v, tt], [pp, q, v], [tt, pp, v], [v, pp, q]][i].map(x => Math.round(x * 255));
  return [`rgb(${R_},${G_},${B_})`, 0.299 * R_ + 0.587 * G_ + 0.114 * B_];
}
function drawEcu() {
  const m = preview, g = ui.g, bench = benchOn && benchApplied;
  const LX = 252, labW = 56, cw = 46, cellH = 28, gridY = 256, hdrY = 232, gx = LX + labW, RX = 1264, RW = 616;
  const modeCol = [C.Blue, C.Accent, C.Red, C.Yellow, C.Green, 'rgb(150,120,255)'];
  EcuModeNames.forEach((n, i) => ui.btn(R(LX + i * 142, 176, 134, 44), n, ecuMode === i, modeCol[i], () => { ecuMode = i; }, F.xs));
  ui.btn(R(gx + EcuN * cw - 90, 176, 90, 44), '? ศัพท์', false, C.Yellow, () => { showEcuHelp = true; }, F.xs);

  const { c0: sc0, c1: sc1, r0: sr0, r1: sr1 } = ecuRange();
  let fMin = 1e9, fMax = -1e9;      // ช่วงค่าของตาราง ใช้ไล่สี
  const cellVal = ecuMode === 0 ? (c, r) => m.CellMs(c, r) : ecuMode === 1 ? (c, r) => m.CellEoi(c, r) : ecuMode === 3 ? (c, r) => m.CellDwell(c, r) : ecuMode === 5 ? (c, r) => m.CellMap(c, r) : null;
  if (cellVal) for (let r = 0; r < EcuN; r++) for (let c = 0; c < EcuN; c++) { const v = cellVal(c, r); fMin = Math.min(fMin, v); fMax = Math.max(fMax, v); }
  ui.txt('TPS %', F.cap, C.Dim, LX + 4, hdrY + 2);
  for (let r = 0; r < EcuN; r++) ui.txtC(String(+(EngineModel.TpsAxis(r) * 100).toFixed(1)), F.cap, r >= sr0 && r <= sr1 ? C.Accent : C.Dim, gx + (r + 0.5) * cw, hdrY + 10);
  const e = {};
  for (let c = 0; c < EcuN; c++) {
    const y = gridY + c * cellH;
    ui.txtC((m.EcuRpmAt(c) / 1000).toFixed(1), F.cap, c >= sc0 && c <= sc1 ? C.Accent : C.Dim, LX + labW / 2 - 4, y + cellH / 2);
    for (let r = 0; r < EcuN; r++) {
      m.EcuAt(m.EcuRpmAt(c), EngineModel.TpsAxis(r), e);
      let col, txt, dMbt = 0;
      if (ecuMode === 0) { const v = m.CellMs(c, r); col = heat((v - fMin) / Math.max(0.01, fMax - fMin)); txt = v >= 10 ? v.toFixed(1) : v.toFixed(2); }
      else if (ecuMode === 1) { const v = m.CellEoi(c, r); col = heat((v - fMin) / Math.max(1, fMax - fMin)); txt = v.toFixed(0); }
      else if (ecuMode === 2) { const v = m.CellAdv(c, r); dMbt = v - e.Mbt; col = heat(v / 45); txt = v.toFixed(1); }
      else if (ecuMode === 3) { const v = m.CellDwell(c, r); col = heat((v - fMin) / Math.max(0.05, fMax - fMin)); txt = v.toFixed(2); }
      else if (ecuMode === 5) { const v = m.CellMap(c, r); col = heat((v - fMin) / Math.max(1, fMax - fMin)); txt = v.toFixed(0); }
      else { col = heat((e.Lambda - 0.7) / 0.6); txt = e.Afr >= 90 ? '∞' : e.Afr.toFixed(1); }
      const cx_ = gx + r * cw + 1.5, cy_ = y + 1.5, cw_ = cw - 3, ch_ = cellH - 3;      // เว้นร่องระหว่างช่อง ให้ตาโล่ง
      ui.rect(cx_, cy_, cw_, ch_, col[0]);
      ui.txtC(txt, F.cap, col[1] > 135 ? 'rgb(18,20,24)' : '#fff', cx_ + cw_ / 2, cy_ + ch_ / 2);
      let mk = null;
      if (ecuMode === 2) mk = dMbt > 1 ? 'rgb(255,30,30)' : dMbt > -2.5 ? '#fff' : 'rgb(20,20,30)';        // เทียบ MBT: แดง = แก่เกิน  ขาว = ใกล้  ดำ = ไฟอ่อน
      else if (ecuMode === 1 && m.OpenValveFrac(c, r) > 0.1) mk = 'rgb(20,20,30)';                           // ดำ = ฉีดตอนวาล์วเปิดแล้ว
      else if (ecuMode === 3) { if (e.Spark < 0.95) mk = 'rgb(255,30,30)'; else if (m.CoilDuty(c, r) > 0.55) mk = 'rgb(20,20,30)'; }   // แดง = ประกายอ่อน  ดำ = คอยล์ร้อน
      if (mk) { g.fillStyle = mk; g.beginPath(); g.moveTo(cx_ + cw_ - 8, cy_); g.lineTo(cx_ + cw_, cy_); g.lineTo(cx_ + cw_, cy_ + 8); g.closePath(); g.fill(); }
    }
  }
  ui.drawRR(R(gx + sr0 * cw, gridY + sc0 * cellH, (sr1 - sr0 + 1) * cw, (sc1 - sc0 + 1) * cellH), 0, C.Accent, 3);
  if (bench && S.running) {     // จุดทำงานสด + ร่องรอย
    const colF = clamp((S.rpm - 1000) / (m.EcuRpmMax - 1000) * (EcuN - 1), 0, EcuN - 1), rowF = EngineModel.TpsToIndex(S.thrEff ?? S.thr);
    const px_ = gx + (rowF + 0.5) * cw, py_ = gridY + (colF + 0.5) * cellH;
    benchTrail.push([px_, py_]); if (benchTrail.length > 50) benchTrail.shift();
    benchTrail.forEach(([x, y], i) => { g.beginPath(); g.arc(x, y, 3, 0, 7); g.fillStyle = `rgba(255,255,255,${Math.min(0.8, (20 + i * 4) / 255)})`; g.fill(); });
    g.beginPath(); g.arc(px_, py_, 10, 0, 7); g.strokeStyle = C.Text; g.lineWidth = 3; g.stroke();
    g.beginPath(); g.arc(px_, py_, 13, 0, 7); g.strokeStyle = '#000'; g.lineWidth = 1; g.stroke();
  } else benchTrail = [];
  const cellAt = p => [clamp(Math.floor((p.y - gridY) / cellH), 0, EcuN - 1), clamp(Math.floor((p.x - gx) / cw), 0, EcuN - 1)];      // [รอบ, โหลด]
  ui.hit(R(gx, gridY, EcuN * cw, EcuN * cellH), p => { const [c, r] = cellAt(p); ecuC0 = ecuC1 = c; ecuR0 = ecuR1 = r; },
    p => { const [c, r] = cellAt(p); ecuC1 = c; ecuR1 = r; }, () => { }, dir => ecuAdjust(dir, keys.has('shift')));

  const ly = gridY + EcuN * cellH + 10;
  for (let i = 0; i < 240; i++) ui.rect(gx + i, ly + 3, 1.3, 14, heat(i / 239)[0]);
  ui.txtR(ecuMode === 0 ? `${fMin.toFixed(1)} ms` : ecuMode === 1 ? `${fMin.toFixed(0)}°` : ecuMode === 2 ? '0°' : ecuMode === 3 ? `${fMin.toFixed(1)} ms` : ecuMode === 5 ? `${fMin.toFixed(0)} kPa` : 'λ0.7 หนา', F.cap, C.Dim, gx - 8, ly);
  ui.txt(ecuMode === 0 ? `${fMax.toFixed(1)} ms` : ecuMode === 1 ? `${fMax.toFixed(0)}°` : ecuMode === 2 ? '45°' : ecuMode === 3 ? `${fMax.toFixed(1)} ms` : ecuMode === 5 ? `${fMax.toFixed(0)} kPa` : 'λ1.3 บาง', F.cap, C.Dim, gx + 250, ly);
  ui.txtR(ecuMode === 0 ? 'ฟ้า = ฉีดน้อย (โหลดเบา)  •  แดง = ฉีดมาก (TPS เปิดมาก)'
    : ecuMode === 1 ? 'ฟ้า = จบฉีดช้า  •  แดง = จบฉีดเร็ว  •  มุมดำ = ฉีดตอนวาล์วเปิด'
      : ecuMode === 2 ? 'มุมขวาบน: แดง = แก่เกิน MBT  •  ขาว = ใกล้  •  ดำ = ไฟอ่อน'
        : ecuMode === 3 ? 'มุมแดง = ประกายอ่อน (ชาร์จไม่พอ)  •  มุมดำ = คอยล์ร้อน'
          : ecuMode === 5 ? 'แก้ได้ = MAP เป้าหมายต่อ TPS x รอบ (ลิ้นเร่งไฟฟ้า)  •  0 = ไม่มีอากาศ'
            : 'ฟ้า = หนา  •  เขียว = แรงสุด  •  เหลือง = บาง  •  แดง = บางเกิน', F.cap, C.Dim, gx + EcuN * cw, ly);

  let bx = LX; const by = 866;
  const B = (t, w, fn, col = C.Accent) => { ui.btn(R(bx, by, w, 44), t, false, col, fn, F.xs); bx += w + 10; };
  B('−−', 50, () => ecuAdjust(-1, true)); B('−', 50, () => ecuAdjust(-1, false)); B('+', 50, () => ecuAdjust(1, false)); B('++', 50, () => ecuAdjust(1, true));
  B('เกลี่ย', 66, ecuSmooth, C.Blue); B('เท่ากัน', 76, ecuEqualize, C.Blue);
  B('โรงงาน(เลือก)', 128, ecuFactorySel, C.Green); B('โรงงาน(ทั้งหมด)', 152, ecuFactoryAll, C.Red);
  if (ecuMode === 2) B('ใกล้ MBT', 104, ecuNearMbt, C.Yellow);

  // ซ้ายล่าง: ช่องที่เลือก
  const prpm = m.EcuRpmAt(ecuC1), ptps = EngineModel.TpsAxis(ecuR1), pload = m._loadOf(ptps, prpm), s = m.EcuAt(prpm, ptps, {}), dl2 = s.Adv - s.Mbt;
  ui.fillRR(R(LX, 928, 976, 116), 4, C.Panel); ui.drawRR(R(LX, 928, 976, 116), 4, C.Border, 1); ui.rect(LX + 14, 940, 4, 18, C.Accent);
  ui.txt(`ช่องที่เลือก   ${fmtN(prpm)} rpm  •  TPS ${f0(ptps * 100)}%  •  โหลด ${f0(pload * 100)}%`, F.cap, C.Dim, LX + 28, 938);
  const warn = s.Egt > 1050 ? 'ไอเสียร้อนจัด! เสี่ยงลูกสูบพัง' : m.KnockWot(prpm) > 0.02 && pload > 0.7 ? 'น็อกที่ช่องนี้' : dl2 < -10 ? 'ไฟอ่อนมาก แรงตก' : s.Lambda > 1.35 || s.Lambda < 0.6 ? 'ส่วนผสมนอกช่วงจุดติด' : '';
  ui.txt(`AFR ${s.Afr >= 90 ? '∞ ตัดน้ำมัน' : f1(s.Afr)} (λ ${f2(s.Lambda)})   ฉีด ${s.PulseMs.toFixed(2)} ms (${f0(s.Duty * 100)}%)   จบฉีด ${f0(s.Eoi)}°   MAP ${f0(m.MapKpa(pload))} kPa`, F.xs, s.Lambda > 1.12 ? C.Red : s.Lambda < 0.7 ? C.Blue : s.Lambda > 0.97 ? C.Yellow : C.Green, LX + 28, 972);
  ui.txt(`จุด ${f1(s.Adv)}° / MBT ${f1(s.Mbt)}°   คอยล์ ${s.DwellMs.toFixed(2)} ms (ประกาย ${f0(s.Spark * 100)}%)   แรงบิด x${s.Mult.toFixed(3)}   EGT ${f0(s.Egt)}°C` + (warn ? '   ⚠ ' + warn : ''), F.xs, warn ? C.Yellow : dl2 > 1.5 || s.Spark < 0.95 ? C.Red : C.Dim, LX + 28, 1008);

  // ขวา: เครื่องจูนสด
  const ix = RX + 28, iw = RW - 56;
  if (!bench) {
    ui.panelBox(R(RX, 176, RW, 564), 'เครื่องจูนสด  (ติดเครื่องจริงบนแท่น)');
    ui.wrap('เปิดโหมดนี้เพื่อติดเครื่องที่กำลังออกแบบอยู่บนแท่น แล้วสตาร์ท เร่งเครื่อง เข้าเกียร์ ลองขับ ฟังเสียง และดูข้อมูลสดของเครื่อง (MAP, AFR, ฉีด, จุดระเบิด, คอยล์, EGT ฯลฯ) พร้อมจุดทำงานบนตาราง', F.sm, C.Dim, ix, 236, iw, 120);
    ui.wrap('ค่าที่แก้ในตารางมีผลทันที ออกจากแท็บนี้แล้วเครื่องที่ติดตั้งจริงจะกลับมาตามเดิม และสถิติจะไม่ถูกบันทึกขณะจูน', F.sm, C.Dim, ix, 360, iw, 120);
    ui.btn(R(ix, 500, iw, 68), 'เปิดเครื่องจูนสด', false, C.Green, () => { benchOn = true; benchDirty = true; benchTrail = []; }, F.md);
    ui.txt('ปุ่มลัด:   S สตาร์ท (ค้าง)   W เร่งเต็ม   K จุดระเบิด   E/Q เปลี่ยนเกียร์', F.cap, C.Dim, ix, 596);
    drawCompareChart(R(RX, 756, RW, 284), m, draft.Limiter);
  } else {
    const rpmNow = S.rpm, loadNow = S.load ?? 0, mult = S.ecuMult ?? 1, adv = S.ecuAdv ?? 0, mbt = S.ecuMbt ?? 0, duty = S.ecuDuty ?? 0, egt = S.ecuEgt ?? 0, kn = S.knock ?? 0;
    const afr = S.ecuAfr ?? 14.7, lam = afr / m.Stoich, pw = S.ecuPw ?? 0, eoi = S.ecuEoi ?? 0, dwell = S.ecuDwell ?? 0, spark = S.ecuSpark ?? 1, dlt = adv - mbt;
    ui.panelBox(R(RX, 176, RW, 864), 'เครื่องจูนสด');
    ui.btn(R(RX + 214, 180, 124, 32), 'ข้อมูลสด', liveTab === 0, C.Green, () => { liveTab = 0; }, F.cap);
    ui.btn(R(RX + 344, 180, 124, 32), 'กราฟ ก่อน/หลัง', liveTab === 1, C.Blue, () => { liveTab = 1; }, F.cap);
    ui.btn(R(RX + RW - 28 - 100, 180, 100, 32), 'ปิดจูนสด', false, C.Red, () => { benchOn = false; mouseThrottle = false; mouseStarter = false; }, F.cap);
    ui.txtR(!S.ignition ? 'ปิดจุดระเบิด' : S.running ? 'ติดเครื่อง' : 'ดับ', F.sm, S.running ? C.Green : C.Dim, RX + RW - 28, 228);
    ui.txt(fmtN(rpmNow), F.numL, C.Accent, ix, 238); ui.txt('รอบเครื่อง rpm', F.xs, C.Dim, ix + 2, 318);
    ui.txt(f0(S.speed * 3.6), F.numL, C.Text, ix + 330, 238);
    const gearTxt = S.isAuto ? `ออโต้  (สายพาน x${f2(Cvt.ratio(S.cvtS))})` : S.gear === 0 ? 'เกียร์ว่าง (N)' : `เกียร์ ${S.gear}`;
    ui.txt('กม./ชม.   ' + gearTxt, F.xs, C.Dim, ix + 332, 318);
    ui.txt(`คันเร่ง ${f0(S.thr * 100)}%`, F.xs, C.Dim, ix, 350);
    const tb = R(ix + 130, 354, iw - 130, 24);
    ui.fillRR(tb, 3, 'rgb(14,17,21)'); ui.rect(tb.x, tb.y, tb.w * clamp(S.thr, 0, 1), tb.h, C.Accent); ui.drawRR(tb, 3, C.Border, 1);
    const thr = p => { mouseThrottle = true; mouseThrottleValue = clamp((p.x - tb.x) / tb.w, 0, 1); };
    ui.hit(R(tb.x - 10, tb.y - 12, tb.w + 20, tb.h + 24), thr, thr, () => { mouseThrottle = false; });
    const cwd = (iw - 20) / 3;
    ui.btn(R(ix, 396, cwd, 42), 'S สตาร์ท (ค้าง)', mouseStarter || down('s'), C.Green, null, F.xs, () => { send('ignition', true); mouseStarter = true; }, () => { mouseStarter = false; });
    ui.btn(R(ix + cwd + 10, 396, cwd, 42), 'W เร่งเต็ม (ค้าง)', down('w'), C.Accent, null, F.xs, () => { mouseThrottle = true; mouseThrottleValue = 1; }, () => { mouseThrottle = false; });
    ui.btn(R(ix + 2 * (cwd + 10), 396, cwd, 42), S.ignition ? 'K ปิดจุดระเบิด' : 'K เปิดจุดระเบิด', !S.ignition, C.Red, () => send('ignition', !S.ignition), F.xs);
    if (!S.isAuto) {
      const ng = draft.Gears, gsp = (iw + 8) / (ng + 1);
      for (let i = 0; i <= ng; i++) ui.btn(R(ix + i * gsp, 450, gsp - 8, 38), i === 0 ? 'N' : String(i), gearSel === i, C.Accent, () => selectGear(i), F.sm);
    } else ui.txt('รถออโต้ CVT: บิดคันเร่งแล้วออกตัวได้เลย', F.xs, C.Dim, ix, 458);
    ui.line(ix, 506, ix + iw, 506, C.Border, 1);
    if (liveTab === 1) {
      drawCompareChart(R(RX + 16, 520, RW - 32, 504), m, draft.Limiter);
    } else {
      const afrCol = lam > 1.12 ? C.Red : lam < 0.7 ? C.Blue : lam > 0.97 ? C.Yellow : C.Green, advCol = dlt > 1.5 ? C.Red : dlt < -8 ? C.Blue : C.Green, egtCol = egt > 1050 ? C.Red : egt > 950 ? C.Yellow : C.Green, kw3 = iw / 3;
      ui.txt('AFR (อากาศ:น้ำมัน)', F.cap, C.Dim, ix, 520); ui.txt(afr >= 90 ? '∞' : f1(afr), F.md, afrCol, ix, 540); ui.txt(afr >= 90 ? 'ตัดน้ำมัน' : `λ ${f2(lam)}`, F.xs, C.Dim, ix, 574);
      ui.txt('จุดระเบิด', F.cap, C.Dim, ix + kw3, 520); ui.txt(`${f1(adv)} °`, F.md, advCol, ix + kw3, 540); ui.txt(`${dlt >= 0 ? 'แก่' : 'อ่อน'} ${f1(Math.abs(dlt))}° (MBT ${f1(mbt)})`, F.cap, C.Dim, ix + kw3, 576);
      ui.txt('EGT (ก๊าซไอเสีย)', F.cap, C.Dim, ix + 2 * kw3, 520); ui.txt(`${f0(egt)} °C`, F.md, egtCol, ix + 2 * kw3, 540); ui.txt(egt > 1050 ? 'ร้อนจัด!' : egt > 950 ? 'ร้อน' : 'ปกติ', F.xs, C.Dim, ix + 2 * kw3, 574);
      ui.line(ix, 610, ix + iw, 610, C.Border, 1);
      const cyc = 120000 / Math.max(rpmNow, 200), airCyc = m.AirPerCycleG(rpmNow, loadNow), fuelCyc = Math.max(0, Math.min(pw, cyc) - InjDeadMs) * m.InjFlowGpMs;
      const airGs = airCyc * rpmNow / 120, fuelGs = fuelCyc * rpmNow / 120;
      const tq = S.running ? m.TorqueInd(rpmNow) * loadNow * mult * (1 - 0.5 * kn) - m.Friction(rpmNow) : 0, kw = tq * rpmNow * 2 * Math.PI / 60000;
      const bsfc = kw > 0.4 ? fuelGs * 3600 / kw : 0, lph = fuelGs / m.FuelDensity * 3.6, pv = m.PistonSpeed(rpmNow);
      const cells = [
        ['MAP (แรงดันท่อไอดี)', `${f0(m.MapKpa(loadNow))} kPa`, C.Text], ['โหลดอากาศ', `${f0(loadNow * 100)} %`, C.Text], ['VE (หายใจ)', `${f0(m.VeAbs(rpmNow) * 100)} %`, C.Text],
        ['แรงบิดขณะนี้', `${f1(tq)} N·m`, C.Blue], ['กำลังขณะนี้', `${f1(Hp(tq, rpmNow))} HP`, C.Accent], ['ตัวคูณจากตาราง', `x${mult.toFixed(3)}`, mult < 0.97 ? C.Red : mult > 1.005 ? C.Green : C.Text],
        ['เวลาฉีด', `${pw.toFixed(2)} ms`, C.Text], ['หัวฉีดเปิด (Duty)', `${f0(duty * 100)} %` + (duty > 1 ? ' เต็ม!' : ''), duty > 1 ? C.Red : duty > 0.85 ? C.Yellow : C.Text], ['จบฉีด (EOI)', `${f0(eoi)} °`, C.Text],
        ['อากาศเข้า', `${f1(airGs)} g/s`, C.Text], ['น้ำมัน', `${f1(lph)} ล./ชม.`, C.Text], ['BSFC', bsfc > 0 ? `${f0(bsfc)} g/kWh` : '-', bsfc > 380 ? C.Yellow : C.Text],
        ['จุดระเบิด', `${f1(adv)} °`, advCol], ['TPS ลิ้นเร่งจริง', `${f1((S.thrEff ?? S.thr) * 100)} %`, C.Text], ['ไทม์คอยล์', `${dwell.toFixed(2)} ms`, C.Text],
        ['ประกายไฟ', `${f0(spark * 100)} %`, spark < 0.95 ? C.Red : C.Green], ['ความเร็วลูกสูบ', `${f1(pv)} ม./วิ`, pv > 22 ? C.Yellow : C.Text], ['น็อก', `${f0(kn * 100)} %`, kn > 0.05 ? C.Red : C.Green],
      ];
      const cwid = iw / 3;
      cells.forEach(([l, v, c], i) => { const cx = ix + (i % 3) * cwid, cy = 624 + Math.floor(i / 3) * 58; ui.txt(l, F.cap, C.Dim, cx, cy); ui.txt(v, F.sm, c, cx, cy + 20); });
    }
  }
  if (ecuInput) {
    const { c0: ic0, c1: ic1, r0: ir0, r1: ir1 } = ecuRange(), ib = R(gx + 230, gridY + 230, 440, 120);
    ui.fillRR(ib, 6, 'rgba(14,17,21,0.94)'); ui.drawRR(ib, 6, C.Accent, 2);
    ui.txt(`ใส่ค่าในช่องที่เลือก ${(ic1 - ic0 + 1) * (ir1 - ir0 + 1)} ช่อง`, F.cap, C.Dim, ib.x + 20, ib.y + 14);
    ui.txt(ecuInput + (Math.floor(Date.now() / 500) % 2 === 0 ? '▮' : ' '), F.numL, C.Accent, ib.x + 20, ib.y + 30);
    ui.txt('Enter = ใช้ค่า  •  Esc = ยกเลิก  •  Backspace = ลบ', F.cap, C.Dim, ib.x + 20, ib.y + ib.h - 26);
  } else ui.txtR('คลุมช่อง แล้วพิมพ์เลข + Enter เพื่อใส่ค่า', F.cap, C.Dim, LX + 960, 938);
  if (showEcuHelp) drawEcuHelp();
}
function drawEcuHelp() {
  const g = ui.g; g.fillStyle = 'rgba(6,8,11,0.88)'; g.fillRect(0, 0, VW, VH);
  const box = R(200, 40, 1520, 1000);
  ui.fillRR(box, 6, C.Panel); ui.drawRR(box, 6, C.Accent, 2);
  ui.txt('อธิบายศัพท์จูนกล่อง', F.title, C.Accent, box.x + 36, box.y + 18);
  ui.txtR('คลิกที่ไหนก็ได้ / Esc เพื่อปิด', F.xs, C.Dim, box.x + box.w - 36, box.y + 34);
  const colW = (box.w - 144) / 3;
  EcuHelp.forEach(([title, lines], i) => {
    const x = box.x + 36 + (i % 3) * (colW + 36), y = box.y + 96 + Math.floor(i / 3) * 300;
    ui.rect(x, y + 4, 4, 26, C.Yellow); ui.txt(title, F.md, C.Text, x + 16, y);
    let yy = y + 46; for (const ln of lines) yy = ui.wrap(ln, F.xs, C.Dim, x + 16, yy, colW - 16) + 3;
  });
  ui.hit(R(0, 0, VW, VH), () => { showEcuHelp = false; });
}

/** กราฟเทียบ: เส้นทึบ = เครื่องที่กำลังแก้ (หลัง)  เส้นประ = เครื่องที่ติดตั้งอยู่ (ก่อน) */
function drawCompareChart(rect, m, limiter) {
  const rpms = [], tq = [], hp = [], tq0 = [], hp0 = [], bm = installed;
  for (let r = 2000; r <= limiter; r += 100) {
    const t = m.BrakeTorqueWot(r); rpms.push(r); tq.push(Math.max(0, t)); hp.push(Math.max(0, Hp(t, r)));
    if (r <= bm.D.Limiter) { const t0 = bm.BrakeTorqueWot(r); tq0.push(Math.max(0, t0)); hp0.push(Math.max(0, Hp(t0, r))); } else { tq0.push(NaN); hp0.push(NaN); }
  }
  ui.chart(rect, rpms, tq, hp, limiter, { tq: tq0, hp: hp0 });
}

function drawGarage() {
  drawTopBar('อู่ออกแบบเครื่อง', '↑ ↓ เลือก  •  ← → ปรับ (Shift = เร็ว)  •  Tab เปลี่ยนหมวด  •  A จูนเม็ด  •  T ที่ปรึกษาจูน  •  C ปรับเทียบรถจริง  •  Enter ประกอบแล้วไปทดสอบ');
  const tw = (GW - 136) / GarageTabs.length;
  GarageTabs.forEach(([name], i) => {
    const r = R(GL + i * tw, 112, tw - 4, 52), act = i === gTab && !advisorOn, hov = inRect(r, ui.mouse);
    ui.rect(r.x, r.y, r.w, r.h, act ? C.PanelHi : hov ? 'rgb(26,30,37)' : C.Panel);
    if (act) ui.rect(r.x, r.y + r.h - 4, r.w, 4, C.Accent);
    ui.txtC(name, F.xs, act ? C.Text : C.Dim, r.x + r.w / 2, r.y + 26);
    ui.hit(r, () => setTab(i));
  });
  ui.btn(R(GL + GW - 132, 112, 132, 52), 'T ที่ปรึกษา', advisorOn, C.Blue, () => { advisorOn = !advisorOn; advKey = ''; }, F.sm);
  if (advisorOn) drawAdvisor();
  else if (gTab === 0) drawParts();
  else if (isEcuTab()) drawEcu();
  else { drawSpecRows(); if (GarageTabs[gTab][1].includes('realhp')) drawCalibrateButtons(); }
  if (!isEcuTab() || advisorOn) drawGarageStats();
  if (showReport) drawReport();
}

function hintBox(text) {
  const box = R(GL, 900, GW, 120);
  ui.fillRR(box, 4, C.Panel); ui.drawRR(box, 4, C.Border, 1); ui.rect(box.x + 14, box.y + 14, 4, 20, C.Yellow);
  ui.txt('คำอธิบาย', F.cap, C.Dim, box.x + 28, box.y + 14);
  ui.wrap(text, F.sm, C.Yellow, box.x + 20, box.y + 42, box.w - 40, box.h - 46);
}

let specScroll = 0, partScroll = 0, specSelSeen = -1, partSelSeen = -1;     // แถวแรกที่เห็นในรายการ (เลื่อนเมื่อมีแถวเกินจอ จะได้ไม่เบียดกัน)
function drawListScroll(y0, height, first, shown, total, setFirst) {
  if (total <= shown) return;
  const jump = p => setFirst(clamp(Math.round((p.y - y0 - 20) / Math.max(1, height - 40) * (total - shown)), 0, total - shown));
  ui.hit(R(GL + GW + 2, y0, 24, height), jump, jump);      // คลิก/ลากแถบเลื่อนด้านขวา
  const th = Math.max(30, height * shown / total), ty = y0 + (height - th) * first / Math.max(1, total - shown);
  ui.rect(GL + GW + 6, y0, 9, height, 'rgb(40,46,56)'); ui.rect(GL + GW + 6, ty, 9, th, C.Accent);
  ui.txtR(`แสดง ${first + 1}–${first + shown} จาก ${total}  (ลูกกลิ้งเมาส์ / ลากแถบขวา / ↑ ↓)`, F.xs, C.Dim, GL + GW, y0 + height + 8);
}

function drawSpecRows() {
  const y0 = 184, rowH = 84, listH = 672, visible = 8, vs = vis(), total = vs.length;
  specScroll = clamp(specScroll, 0, Math.max(0, total - visible));
  if (gSel !== specSelSeen) { specSelSeen = gSel; if (gSel < specScroll) specScroll = gSel; if (gSel >= specScroll + visible) specScroll = gSel - visible + 1; }      // ตามแถวที่เลือกเฉพาะตอนเลือกใหม่ (ไม่ดีดกลับตอนเลื่อนลูกกลิ้ง)
  const shown = Math.min(visible, total - specScroll);
  ui.hit(R(GL, y0, GW, listH), null, null, null, dir => { specScroll = clamp(specScroll - dir * 2, 0, Math.max(0, total - visible)); });
  for (let k = 0; k < shown; k++) {
    const i = specScroll + k, def = vs[i], v = def.get(draft), y = y0 + k * rowH, rowRect = R(GL, y, GW, rowH - 8), selected = i === gSel;
    if (selected) { ui.rect(rowRect.x, rowRect.y, rowRect.w, rowRect.h, C.PanelHi); ui.rect(rowRect.x, rowRect.y, 4, rowRect.h, C.Accent); }
    else if (inRect(rowRect, ui.mouse)) ui.rect(rowRect.x, rowRect.y, rowRect.w, rowRect.h, 'rgb(24,28,35)');
    ui.hit(rowRect, () => { gSel = i; });
    ui.txt(def.name, F.md, selected ? C.Text : C.Dim, GL + 20, y + 8);
    ui.txtR(paramDisplay(def, draft), F.num, selected ? C.Accent : C.Text, GL + GW - 20, y + 6);
    const sy = y + 40;
    ui.smallBtn(R(GL + 16, sy, 40, 30), '−', () => { gSel = i; nudgeRow(i, -1, keys.has('shift')); });
    const frac = (v - def.min) / (def.max - def.min), sr = R(GL + 70, sy - 2, GW - 140, 34);
    ui.slider(sr, frac, selected ? C.Accent : 'rgb(150,100,25)', f => {
      gSel = i; let nv = def.min + f * (def.max - def.min); nv = Math.round(nv / def.step) * def.step;
      def.set(draft, clamp(+nv.toFixed(6), def.min, def.max)); draftChanged();
    });
    ui.smallBtn(R(GL + GW - 56, sy, 40, 30), '+', () => { gSel = i; nudgeRow(i, 1, keys.has('shift')); });
  }
  drawListScroll(y0, listH - 8, specScroll, shown, total, v => { specScroll = v; });
  if (total > 0) hintBox(vs[gSel].name + ': ' + vs[gSel].hint);
  if (gTab === 3) drawPsiScale(184 + total * rowH + 10);
}

function drawPsiScale(y) {
  const m = preview, psi = m.Psi, box = R(GL, y, GW, 150);
  ui.fillRR(box, 4, C.Panel); ui.drawRR(box, 4, C.Border, 1); ui.rect(box.x + 14, box.y + 14, 4, 20, C.Accent);
  ui.txt('เกจแรงดันอัด (วัดตอนสตาร์ท)', F.sm, C.Dim, box.x + 28, box.y + 10);
  ui.txtR(`${f0(psi)} psi  =  ${f0(psi * 6.895)} kPa  =  ${(psi / 14.5038).toFixed(1)} bar`, F.numS, C.Text, box.x + box.w - 20, box.y + 12);
  const lo = 100, hi = 480, bx = box.x + 24, bw = box.w - 48, by = box.y + 64;
  [[100, 150, C.Red, 'ต่ำ/สึก'], [150, 220, C.Green, 'เดิมโรงงาน'], [220, 300, C.Yellow, 'แต่งแรง'], [300, 480, C.Red, 'สูงจัด']].forEach(([a, b, c, n]) => {
    const x1 = bx + (a - lo) / (hi - lo) * bw, x2 = bx + (b - lo) / (hi - lo) * bw;
    ui.g.globalAlpha = 0.47; ui.rect(x1, by, x2 - x1 - 2, 18, c); ui.g.globalAlpha = 1;
    ui.txtC(n, F.xs, C.Dim, (x1 + x2) / 2, by + 36);
  });
  const mx = bx + clamp((psi - lo) / (hi - lo), 0, 1) * bw, g = ui.g;
  ui.rect(mx - 2, by - 12, 4, 42, C.Text);
  g.fillStyle = C.Text; g.beginPath(); g.moveTo(mx - 8, by - 20); g.lineTo(mx + 8, by - 20); g.lineTo(mx, by - 8); g.closePath(); g.fill();
  ui.txt(`CR ${f1(draft.Cr)}:1 สถิต  →  ${f1(m.CrDyn)}:1 จริงในกระบอก (แคม IN ${f0(draft.Duration)}° / EX ${f0(draft.DurationEx)}°)`, F.xs, C.Dim, box.x + 24, box.y + box.h - 30);
}

function drawCalibrateButtons() {
  const y = 184 + 5 * Math.min(88, 700 / 5) + 20;
  ui.btn(R(GL, y, 520, 60), 'C  ปรับเทียบโมเดลให้ตรงรถจริง', Math.abs(draft.CalK - 1) > 0.005, C.Green, calibrateToReal, F.sm);
  ui.btn(R(GL + 536, y, GW - 536, 60), 'ล้างค่ารถจริง/ปรับเทียบ', false, C.Red, () => { draft.RealHp = draft.RealHpRpm = draft.RealTq = draft.RealTqRpm = 0; draft.CalK = 1; draftChanged(); toast('ล้างค่ารถจริงแล้ว'); }, F.sm);
}

function drawParts() {
  const y0 = 184, rowH = 64, listH = 640, rows = PartCats.length + 1, visible = Math.floor(listH / rowH);
  partScroll = clamp(partScroll, 0, Math.max(0, rows - visible));
  if (gPartRow !== partSelSeen) { partSelSeen = gPartRow; if (gPartRow < partScroll) partScroll = gPartRow; if (gPartRow >= partScroll + visible) partScroll = gPartRow - visible + 1; }
  const shown = Math.min(visible, rows - partScroll);
  ui.hit(R(GL, y0, GW, listH), null, null, null, dir => { partScroll = clamp(partScroll - dir * 2, 0, Math.max(0, rows - visible)); });
  for (let k = 0; k < shown; k++) {
    const r = partScroll + k, row = r, y = y0 + k * rowH, rowRect = R(GL, y, GW, rowH - 8), selected = r === gPartRow;
    if (selected) { ui.rect(rowRect.x, rowRect.y, rowRect.w, rowRect.h, C.PanelHi); ui.rect(rowRect.x, rowRect.y, 4, rowRect.h, C.Accent); }
    else if (inRect(rowRect, ui.mouse)) ui.rect(rowRect.x, rowRect.y, rowRect.w, rowRect.h, 'rgb(24,28,35)');
    ui.hit(rowRect, () => { gPartRow = row; });
    let name, shownTxt;
    if (r === 0) {
      name = 'เช็ตเครื่องสำเร็จรูป';
      const ci = Presets.findIndex(s => JSON.stringify(s[1]) === JSON.stringify(draft));
      shownTxt = ci < 0 ? 'ปรับเอง  (ใช้ลูกศรซ้าย-ขวาเลือกเช็ต)' : Presets[ci][0];
    } else {
      const cat = PartCats[r - 1]; name = cat.name; const c = partCurrent(cat, draft);
      shownTxt = c < 0 ? 'ปรับเอง  (ไม่ตรงชุดสำเร็จรูป)' : cat.items[c].name;
    }
    const cy = y + (rowH - 8) / 2;
    ui.txt(name, F.sm, selected ? C.Text : C.Dim, GL + 20, cy - 14);
    ui.smallBtn(R(GL + 300, y + 4, 52, rowH - 16), '◀', () => partStep(row, -1));
    ui.fillRR(R(GL + 360, y + 4, 480, rowH - 16), 4, 'rgb(14,17,21)');
    ui.txtC(shownTxt, F.sm, selected ? C.Accent : C.Text, GL + 600, cy);
    ui.smallBtn(R(GL + 848, y + 4, 40, rowH - 16), '▶', () => partStep(row, 1));
  }
  drawListScroll(y0, listH - 8, partScroll, shown, rows, v => { partScroll = v; });
  hintBox(gPartRow === 0 ? 'เลือกเช็ตเครื่องทั้งชุด (* = ตัวอย่างสมมติ ไม่ใช่สเปกรถจริง) แล้วสลับอะไหล่ทีละชิ้นต่อได้' : PartCats[gPartRow - 1].name + ': ' + PartCats[gPartRow - 1].hint);
}

function drawGarageStats() {
  const m = preview, d = draft, X = 1180, W = 700;
  ui.panelBox(R(X, 112, W, 214), 'สมรรถนะ (คาดการณ์)');
  ui.btn(R(X + W - 232, 118, 216, 36), 'R  สรุปการแก้ไข', false, C.Blue, openReport, F.cap);
  ui.txt(f1(m.PeakHp), F.numL, C.Accent, X + 24, 150); ui.txt(`HP @ ${fmtN(m.PeakPsRpm)} rpm`, F.sm, C.Dim, X + 28, 232);
  ui.txt(f1(m.PeakTq), F.numL, C.Blue, X + 360, 150); ui.txt(`N·m @ ${fmtN(m.PeakTqRpm)} rpm`, F.sm, C.Dim, X + 364, 232);
  ui.txt(`${f1(m.Cc)} cc   •   ก้าน/ชัก ${f2(m.RodRatio)}   •   รถ+คน ${f0(Veh.mass(d))} กก.   •   ${f0(m.PeakHp / m.Cc * 1000)} HP/ลิตร`, F.xs, C.Dim, X + 28, 268);
  if (d.RealHp > 0 || d.RealTq > 0) {
    let s = 'รถจริง:  ';
    if (d.RealHp > 0) s += `${f1(d.RealHp)} HP${d.RealHpRpm > 0 ? ' @ ' + fmtN(d.RealHpRpm) : ''} (โมเดลต่าง ${((m.PeakHp / d.RealHp - 1) * 100).toFixed(0)}%)   `;
    if (d.RealTq > 0) s += `${f1(d.RealTq)} N·m${d.RealTqRpm > 0 ? ' @ ' + fmtN(d.RealTqRpm) : ''} (ต่าง ${((m.PeakTq / d.RealTq - 1) * 100).toFixed(0)}%)`;
    ui.txt(s, F.xs, Math.abs(d.CalK - 1) > 0.005 ? C.Green : C.Yellow, X + 28, 298);
  }
  drawCompareChart(R(X, 338, W, 246), m, d.Limiter);

  ui.panelBox(R(X, 598, W, 390), 'ตรวจสภาพเครื่อง');
  let y = 636;
  const line = (s, st) => {
    ui.g.beginPath(); ui.g.arc(X + 28, y + 13, 6, 0, 7); ui.g.fillStyle = st === 2 ? C.Yellow : st === 1 ? C.Green : 'rgb(90,98,110)'; ui.g.fill();
    ui.txt(s, F.xs, st === 2 ? C.Yellow : C.Text, X + 46, y); y += 28.5;
  };
  line(`เดินเบา ~${fmtN(m.IdleRpm)} rpm  •  โอเวอร์แลป ${f0(m.Overlap)}°${m.Overlap > 40 ? '  (สะดุด)' : ''}`, m.Overlap > 40 ? 2 : 1);
  const flowLimit = m.RpmFlow < m.PeakPsRpm; line(`ไอดีเริ่มอั้นที่ ${fmtN(m.RpmFlow)} rpm${flowLimit ? '  (เรือน/วาล์วเล็กไป)' : ''}`, flowLimit ? 2 : 1);
  { const wb = m.ValveFit < 0.999 || m.LiftDia > 0.34; line(`วาล์ว ไอดี ${f1(m.VdIn)} / ไอเสีย ${f1(m.VdEx)} มม. x${d.Valves === 4 ? 2 : 1}  •  ยก/ขนาด ${f2(m.LiftDia)}` + (m.ValveFit < 0.999 ? '  หัวสูบจุไม่พอ ถูกลดขนาด' : m.LiftDia > 0.34 ? '  ยกเกินที่วาล์วใช้ได้' : m.LiftDia < 0.2 ? '  ยกน้อย ไหลไม่เต็ม' : ''), wb ? 2 : 1); }
  const floatBad = d.Limiter >= m.FloatRpm; line(`วาล์วลอยที่ ${fmtN(m.FloatRpm)} rpm${floatBad ? '  รอบตัดสูงไป (ใส่สปริงวาล์วแข็งขึ้น)' : ''}`, floatBad ? 2 : 1);
  { const ra = Math.round(d.Limiter * 0.35 / 100) * 100, rb = Math.round(d.Limiter * 0.80 / 100) * 100, up = m.RevUpTime(ra, rb), dn = m.CoastTime(rb, ra);
    line(`เร่งรอบ ${fmtN(ra)}→${fmtN(rb)}: ${up.toFixed(2)} วิ  •  ปล่อยคันเร่งตก: ${dn.toFixed(2)} วิ  (เครื่องเปล่า)` + (up > 1.6 ? '  เร่งยาน' : up < 0.35 ? '  เร่งไวมาก' : ''), up > 1.6 ? 2 : 1); }
  { const rb = Math.round(d.Limiter * 0.6 / 500) * 500; line(`เบรกเครื่อง @${fmtN(rb)} rpm: ${m.EngineBrake(rb).toFixed(1)} N·m (หนืดตามรอบ + แรงอัด ${m.CompDrag.toFixed(2)}x)`, 0); }
  const vpL = m.PistonSpeed(d.Limiter); line(`ลูกสูบเฉลี่ย ${f1(vpL)} ม./วิ ที่รอบตัด${vpL > 24 ? '  (ชักยาวเกิน กำลังตก)' : ''}`, vpL > 24 ? 2 : vpL > 22 ? 0 : 1);
  const kn = m.Knock(3500, m.Load(1, 3500)), margin = m.CrLimit - m.CrDyn, fuel = Fuels[clamp(d.Fuel, 0, 6)].name;
  line(kn > 0.02 ? `น็อก! CR ${f1(d.Cr)} (${f0(m.Psi)} psi) เกินพิกัด ${fuel} (${f1(m.CrLimit)})` : `CR ${f1(d.Cr)}  •  ${f0(m.Psi)} psi  •  พิกัด ${fuel} ${f1(m.CrLimit)} (เหลือ ${f1(margin)})`, kn > 0.02 || margin < 0.5 ? 2 : 1);
  if (d.Cr < 8.8) line('กำลังอัดต่ำ: เดินเบาจุดไม่ติดเป็นพัก ๆ', 2);
  if (m.Hunt > 0.25) line(`แคมองศามาก: เดินเบาหอบ (รอบเหวี่ยงขึ้น-ลง ~${f0(m.Hunt * 100)}%)`, 2);
  const idle = m.Lope < 0.12 ? 'เดินเบานิ่ง' : m.Lope < 0.5 ? 'เดินเบาสะดุดเบา ๆ' : 'เดินเบาลูปจัด';
  const tone = m.SoundBright < -0.35 ? 'นุ่ม' : m.SoundBright > 0.55 ? 'แผดคม' : 'กลาง';
  const size = m.PitchStretch < 0.92 ? '  •  เสียงเล็กแหลม' : m.PitchStretch > 1.08 ? '  •  เสียงใหญ่ทุ้ม' : '';
  const ep = preview.EcuAt(m.PeakPsRpm, 1, {});
  line(`ECU @พีค: AFR ${f1(ep.Afr)}  •  จุด ${f1(ep.Adv)}° (MBT ${f1(ep.Mbt)})  •  หัวฉีด ${f0(ep.Duty * 100)}%`, ep.Duty > 1 || ep.Egt > 1050 || ep.Adv - ep.Mbt > 1.5 ? 2 : 1);
  line(`เสียง: ${idle}  •  โทน${tone}${size}`, 0);
  if (d.Auto === 1) {
    const hold = Cvt.holdRpm(m), ok = hold >= m.PeakPsRpm * 0.93 && hold <= m.PeakPsRpm * 1.08;
    const v = hold < m.PeakPsRpm * 0.93 ? 'ถือรอบต่ำกว่าพีค' : hold > m.PeakPsRpm * 1.08 ? 'ถือรอบเกินพีค' : 'ใกล้รอบพีค (ดี)';
    line(`CVT ถือ ~${fmtN(hold)} rpm: ${v}  •  ท็อป ${f0(Veh.topSpeedAuto(m).kmh)} กม./ชม.`, ok ? 1 : 2);
  } else {
    const sp = Array.from({ length: d.Gears }, (_, i) => f0(d.Limiter * 2 * Math.PI / 60 * Veh.wheelRadius(d) / Veh.total(d, i + 1) * 3.6));
    line(`ที่รอบตัด (กม./ชม.) เกียร์ 1-${d.Gears}: ${sp.join(' / ')}`, 0);
    const top = Veh.topSpeed(m, d.Gears), v = top.rpm >= d.Limiter - 150 ? '  รอบตัดมาก่อน ปลายสั้น' : top.rpm < m.PeakPsRpm * 0.88 ? '  ปลายยาว ลากไม่ถึงพีค' : '';
    line(`ท็อปเกียร์ ${d.Gears}: ${f0(top.kmh)} กม./ชม. @ ${fmtN(top.rpm)} rpm${v}`, v ? 2 : 1);
  }
  const same = JSON.stringify(design) === JSON.stringify(d);
  if (d.Auto === 1) {
    ui.btn(R(X, 1000, 230, 54), 'A  จูนเม็ด', false, C.Blue, autoTuneRollers, F.md);
    ui.btn(R(X + 246, 1000, W - 246, 54), same ? 'Enter  ไปทดสอบ (ติดตั้งอยู่)' : 'Enter  ประกอบแล้วไปทดสอบ', !same, C.Green, assemble, F.md);
  } else ui.btn(R(X, 1000, W, 54), same ? 'Enter  ไปทดสอบ (แบบนี้ติดตั้งอยู่)' : 'Enter  ประกอบแล้วไปทดสอบ', !same, C.Green, assemble, F.md);
}

// ================================================================= ที่ปรึกษาจูน
function refreshAdvisor() {
  const key = JSON.stringify([draft, advGoal, advC, design, last402, topSession, peakRpm, dynoRes?.ps, before?.b402]);
  if (key === advKey) return;
  advKey = key;
  advList = Adv.suggest(draft, advGoal, 5, advC);
  let capped = draft;
  if (advC.rpmCap > 0 && advList.length && advList[0].gain >= 999) capped = advList[0].design;
  const t = { peakRpm, top: topSession, last060, last100, last402, trap402,
    dyno: dynoRes ? { ps: dynoRes.ps, psRpm: dynoRes.psRpm, tq: dynoRes.peakTq, tqRpm: dynoRes.tqRpm } : null, before, cur };
  advTips = [...Adv.capTips(capped, advC), ...Adv.observe(installed, t)];
}
function stepCap(delta) { const c = advC.rpmCap > 0 ? advC.rpmCap : Math.round(draft.Limiter / 100) * 100; advC.rpmCap = clamp(c + delta, 6000, 15000); advKey = ''; }
function drawAdvisor() {
  refreshAdvisor();
  const bw = (GW - 20) / 3;
  Adv.GoalNames.forEach((n, i) => ui.btn(R(GL + i * (bw + 10), 176, bw, 46), 'เป้าหมาย: ' + n, advGoal === i, C.Accent, () => { advGoal = i; advKey = ''; }, F.sm));
  ui.txt('รอบที่อยากใช้', F.xs, C.Dim, GL + 4, 242);
  ui.smallBtn(R(GL + 150, 232, 44, 34), '−', () => stepCap(-100));
  ui.fillRR(R(GL + 200, 232, 180, 34), 4, 'rgb(14,17,21)');
  ui.txtC(advC.rpmCap <= 0 ? 'ไม่จำกัด' : `${fmtN(advC.rpmCap)} rpm`, F.sm, advC.rpmCap > 0 ? C.Accent : C.Dim, GL + 290, 250);
  ui.smallBtn(R(GL + 386, 232, 44, 34), '+', () => stepCap(100));
  ui.btn(R(GL + 450, 232, 150, 34), advC.rpmCap > 0 ? 'ยกเลิกรอบ' : 'ใช้รอบเครื่องนี้', false, C.Blue, () => { advC.rpmCap = advC.rpmCap > 0 ? 0 : Math.round(draft.Limiter / 100) * 100; advKey = ''; }, F.xs);
  ui.btn(R(GL + 616, 232, GW - 616, 34), advC.street ? 'โจทย์ใช้ถนน: เปิด (เดินเบาต้องนิ่ง)' : 'โจทย์ใช้ถนน: ปิด', advC.street, C.Green, () => { advC.street = !advC.street; advKey = ''; }, F.xs);
  ui.txt('ล็อก (ใช้อะไหล่ตามที่เราตั้ง ที่ปรึกษาไม่แตะ):', F.xs, C.Dim, GL + 4, 278);
  const perRow = 5, cw = (GW - (perRow - 1) * 6) / perRow;
  Adv.Groups.forEach(([name], i) => {
    const on = (advC.lockMask & (1 << i)) !== 0;
    ui.btn(R(GL + (i % perRow) * (cw + 6), 304 + Math.floor(i / perRow) * 40, cw, 34), (on ? '[ล็อก] ' : '') + name, on, C.Yellow, () => { advC.lockMask ^= 1 << i; advKey = ''; }, F.xs);
  });
  const box = R(GL, 392, GW, 240);
  ui.panelBox(box, 'วิเคราะห์จากผลทดสอบ (เครื่องที่ติดตั้งอยู่)');
  let y = box.y + 50;
  for (const [lv, text] of advTips) {
    const w = box.w - 64, col = lv === 2 ? C.Yellow : lv === 1 ? C.Green : C.Text;
    const lines = Math.ceil(ui.measure(text, F.xs) / w), h = lines * 19 * 1.3;
    if (y + h > box.y + box.h - 8) { ui.txt('…', F.xs, C.Dim, box.x + 24, y - 4); break; }
    ui.g.beginPath(); ui.g.arc(box.x + 25, y + 11, 5, 0, 7); ui.g.fillStyle = lv === 2 ? C.Yellow : lv === 1 ? C.Green : 'rgb(90,98,110)'; ui.g.fill();
    y = ui.wrap(text, F.xs, col, box.x + 42, y, w) + 6;
  }
  const sb = R(GL, 642, GW, 372);
  ui.panelBox(sb, 'ควรปรับอะไร  (ตามโจทย์ จัดอันดับตามเป้าหมาย)');
  if (!advList.length) { ui.wrap('ไม่พบการปรับค่าเดี่ยว ๆ ที่ดีขึ้นอย่างมีนัยสำคัญโดยไม่ทำให้น็อก/วาล์วลอย — ลองเปลี่ยนหลายค่าพร้อมกัน เช่น ท่อ+แคม+รอบตัด', F.sm, C.Dim, sb.x + 24, sb.y + 64, sb.w - 48, 120); return; }
  let ry = sb.y + 50; const rh = 58;
  for (const s of advList) {
    const row = R(sb.x + 10, ry, sb.w - 20, rh - 6);
    if (inRect(row, ui.mouse)) ui.rect(row.x, row.y, row.w, row.h, 'rgb(24,28,35)');
    ui.txt(s.label, F.sm, C.Text, row.x + 10, row.y + 2);
    const a = s.after, b = s.before;
    ui.txt(`402 ม. ${f2(b.t402)}→${f2(a.t402)} วิ  •  0–60 ${f2(b.t060)}→${f2(a.t060)}  •  ท็อป ${f0(b.top)}→${f0(a.top)} กม./ชม.`, F.xs, C.Green, row.x + 10, row.y + 30);
    ui.btn(R(row.x + row.w - 120, row.y + 4, 112, row.h - 8), 'ใช้ค่านี้', false, C.Green, () => { draft = { ...s.design }; draftChanged(); advKey = ''; toast('ปรับตามคำแนะนำ: ' + s.label); }, F.xs);
    ry += rh;
  }
  const n = advList.find(s => s.note);
  if (n) ui.wrap('ข้อควรระวัง: ' + n.note, F.xs, C.Yellow, sb.x + 20, sb.y + sb.h - 30, sb.w - 40, 26);
}

// ================================================================= ลูปหลัก
const cv = document.getElementById('screen');
const ui = new Ui(cv);
let lastT = now(), frame = 0, fps = 144;

function tick() {
  requestAnimationFrame(tick);
  const t = now(), dt = Math.min(0.1, t - lastT); lastT = t;
  if (dt > 0) fps += (1 / dt - fps) * 0.05;

  let thr = 0, brake = 0, starter = false;
  // เครื่องจูนสด: ติดเครื่องที่กำลังออกแบบ (แบบร่าง) บนแท่น เกียร์ว่าง ในแท็บจูนกล่อง
  const bench = benchOn && page === 'garage' && isEcuTab() && !advisorOn;
  if (bench && (!benchApplied || benchDirty)) { if (!benchApplied) gearSel = 0; node?.port.postMessage({ type: 'assemble', design: draft }); benchApplied = true; benchDirty = false; }
  else if (!bench && benchApplied) { node?.port.postMessage({ type: 'assemble', design }); benchApplied = false; gearSel = 0; }
  if ((page === 'drive' && !showDyno) || bench) {
    const zk = down('z'), xk = down('x');
    if (zk || xk) {
      const ceil = zk ? S.limiter - 40 : S.limiter - 800, pred = S.rpm + rpmRate * 0.4;
      gradThr = clamp(gradThr + 0.8 * clamp((ceil - pred) / 350, -1.5, 1) * dt, 0, 1);
    } else gradThr = Math.max(0, gradThr - 3 * dt);
    thr = gradThr;
    if (down('w')) { thr = 1; gradThr = 1; }
    if (mouseThrottle) thr = mouseThrottleValue;
    if (down(' ') || down('b') || mouseBrake) brake = 1;
    starter = down('s') || mouseStarter;
    if (starter && down('s')) send('ignition', true);
    if (autoDrive && page === 'drive') { const o = autopilotStep(t); thr = o.thr; brake = o.brake; starter = o.starter; }
  } else { gradThr = 0; autoDrive = false; }
  send('throttleInput', +thr.toFixed(3)); send('brakeInput', brake); send('starterHeld', starter);
  send('clutchEnabled', save.clutchSys); send('surface', save.surface); send('idleBoost', +(save.idleGain * 30).toFixed(2));
  const shiftHeld = down('shift');
  send('clutchInput', ((page === 'drive' && !showDyno) || bench) && (shiftHeld || mouseClutch || t < assistUntil || t < apClutchUntil) ? 1 : 0);
  send('gearInput', gearSel); send('slopePct', slope);
  if (dt > 0) { rpmRate += ((S.rpm - lastRpmG) / dt - rpmRate) * 0.3; lastRpmG = S.rpm; }
  needle += (S.rpm - needle) * Math.min(1, dt * 25);
  if (!bench && S.rpm > peakRpm) peakRpm = S.rpm;
  if (!bench && peakRpm > cur.peakRpm) cur.peakRpm = peakRpm;
  if (!bench) roadTests(t);
  frame++;

  ui.begin();
  if (page === 'menu') drawMenu(); else if (page === 'settings') drawSettings(); else if (page === 'garage') drawGarage(); else drawDrive();
  if (audioError) ui.txt('ไม่มีเสียง: ' + audioError, F.sm, C.Red, 40, VH - 64);
  if (showDyno && dynoRes) drawDynoResult();
  if (t < toastUntil) {
    const tx = page === 'drive' ? 640 : 1070, r = R(tx - 460, VH - 58, 920, 48);
    ui.fillRR(r, 4, 'rgba(26,30,38,0.96)'); ui.drawRR(r, 4, C.Accent, 1);
    ui.txtC(toastText, F.md, C.Accent, tx, r.y + r.h / 2);
  }
  if (showFps) ui.txtR(`${f0(fps)} fps`, F.xs, C.Dim, VW - 20, 2);
}

document.getElementById('startBtn').onclick = async () => {
  const msg = document.getElementById('splashMsg'), btn = document.getElementById('startBtn');
  btn.disabled = true; msg.textContent = 'กำลังโหลดเสียงเครื่องยนต์…';
  try {
    await startAudio();
    for (let i = 0; i < 100 && !ready; i++) await new Promise(r => setTimeout(r, 50));
    document.getElementById('splash').hidden = true;
  } catch (err) {
    console.error(err);
    audioError = String(err.message || err);
    msg.innerHTML = 'เปิดเสียงไม่สำเร็จ: ' + audioError + '<br>ลองใช้ Chrome / Safari เวอร์ชันล่าสุด หรือเล่นต่อแบบไม่มีเสียง';
    btn.disabled = false; btn.textContent = 'เล่นต่อโดยไม่มีเสียง';
    btn.onclick = () => { document.getElementById('splash').hidden = true; };
  }
};
const hashV = location.hash.replace('#', '').split(',');
if (hashV.includes('nosound')) document.getElementById('splash').hidden = true;
const startPage = hashV.find(x => ['menu', 'garage', 'drive', 'settings'].includes(x));
if (startPage) page = startPage;
if (hashV.includes('advisor')) { page = 'garage'; advisorOn = true; }
window.__S = S;
tick();
