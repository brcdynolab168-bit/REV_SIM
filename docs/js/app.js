// Rev Lab (เว็บ/มือถือ): UI + ลูปเกม  (เสียง/ฟิสิกส์อยู่ใน AudioWorklet: worklet.js + sim.js)
import {
  EngineModel, Presets, Params, paramByKey, paramDisplay, nudge, GarageTabs, PartCats, partCurrent, Fuels, newDesign,
  Veh, Cvt, clamp, Hp, Ps, PsToHp, calibrate, tireCode,
} from './engine.js';
import * as Adv from './advisor.js';

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const fmtN = v => Math.round(v).toLocaleString('en-US');

// ================================================================= บันทึก
const KEY = 'revlab.web.v1';
const save = (() => {
  const s = { design: null, best: { b060: -1, b100: -1, b402: -1, top: 0 }, volume: 0.8, assist: matchMedia('(pointer:coarse)').matches };
  try { Object.assign(s, JSON.parse(localStorage.getItem(KEY) || '{}')); } catch { /* เริ่มใหม่ */ }
  return s;
})();
let saveTimer = 0;
const persist = () => { clearTimeout(saveTimer); saveTimer = setTimeout(() => { try { localStorage.setItem(KEY, JSON.stringify(save)); } catch { /* ไม่เป็นไร */ } }, 250); };

let design = newDesign(save.design || {});
let draft = { ...design };
let installed = new EngineModel(design);
let preview = new EngineModel(draft);

// ================================================================= สถิติ
const newRun = () => ({ b060: -1, b100: -1, b402: -1, top: 0, peakRpm: 0, hp: 0 });
const runAny = r => r.b060 > 0 || r.b100 > 0 || r.b402 > 0 || r.top > 0 || r.hp > 0;
const bestOf = (cur, v) => (v > 0 && (cur < 0 || v < cur) ? v : cur);
let cur = newRun(), before = null;
let last060 = -1, last100 = -1, last402 = -1, trap402 = 0, topSession = 0, peakRpm = 0;
let dynoRes = null;

// ================================================================= เสียง / worklet
let ctx = null, node = null, ready = false;
const S = { rpm: 0, thr: 0, cut: false, softK: 0, speed: 0, dist: 0, gear: 0, clutchLock: 0, cfLock: 0, lever: 0, cvtS: 0, running: false, ignition: true, dynoPhase: 0, dynoVersion: 0, dynoFailed: false, limiter: 9500, isAuto: false };
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
      const n = Math.floor(Math.min(len, dv.byteLength - pos - 8) / 2 / ch);
      const x = new Float32Array(n);
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

async function startAudio() {
  const AC = window.AudioContext || window.webkitAudioContext;
  ctx = new AC({ latencyHint: 'interactive' });
  if (ctx.state === 'suspended') await ctx.resume();
  await ctx.audioWorklet.addModule('js/worklet.js');
  node = new AudioWorkletNode(ctx, 'rev-engine', { numberOfInputs: 0, outputChannelCount: [1] });
  node.connect(ctx.destination);
  const [wavBuf, marks] = await Promise.all([fetch('assets/engine_ref.wav').then(r => r.arrayBuffer()), fetch('assets/engine_ref.marks').then(r => r.text())]);
  const { x, fs } = parseWav(wavBuf);
  const ratio = ctx.sampleRate / fs;
  const wav = resample(x, fs, ctx.sampleRate);
  const scaled = ratio === 1 ? marks : marks.split(/\r?\n/).map(l => (/^\d+$/.test(l.trim()) ? String(Math.round(+l * ratio)) : l)).join('\n');
  node.port.onmessage = onWorkletMsg;
  node.port.postMessage({ type: 'init', wav, marks: scaled, design, volume: save.volume }, [wav.buffer]);
  document.addEventListener('visibilitychange', () => { if (document.hidden) ctx.suspend(); else ctx.resume(); });
}

let dynoSeen = 0;
function onWorkletMsg(e) {
  const m = e.data;
  if (m.type === 'ready') { ready = true; return; }
  if (m.type !== 'state') return;
  const { dynoTq, type, ...st } = m;
  Object.assign(S, st);
  if (dynoTq) finishDyno(dynoTq);
}

// ================================================================= toast
let toastT = 0;
function toast(msg) {
  const el = $('#toast');
  el.textContent = msg; el.classList.add('show');
  clearTimeout(toastT); toastT = setTimeout(() => el.classList.remove('show'), 3200);
}

// ================================================================= ตัวควบคุม
const keys = new Set();
const held = { start: false, clutch: false, brake: false, full: false, x: false, z: false };
let thrPointer = false, thrVal = 0, gradThr = 0, rpmRate = 0, lastRpmG = 0;
let gearSel = 0, slope = 0, assistUntil = 0;
let view = 'test';

function holdBtn(el, key, extra) {
  const dn = e => { e.preventDefault(); held[key] = true; el.classList.add('hold'); try { el.setPointerCapture(e.pointerId); } catch { /* ok */ } extra?.(true); };
  const up = () => { if (!held[key]) return; held[key] = false; el.classList.remove('hold'); extra?.(false); };
  el.addEventListener('pointerdown', dn);
  el.addEventListener('pointerup', up); el.addEventListener('pointercancel', up); el.addEventListener('lostpointercapture', up);
  el.addEventListener('contextmenu', e => e.preventDefault());
}
const releaseAll = () => { keys.clear(); Object.keys(held).forEach(k => { held[k] = false; }); thrPointer = false; $$('.hold').forEach(b => b.classList.remove('hold')); };
window.addEventListener('blur', releaseAll);

function selectGear(gi) {
  if (S.isAuto || gi === gearSel) return;
  const doIt = () => { gearSel = gi; };
  if (S.running && S.lever < 0.6 && S.cfLock > 0.05) {
    if (save.assist) { assistUntil = performance.now() / 1000 + 0.5; setTimeout(doIt, 130); return; }
    toast('กำครัตช์ก่อน: กดปุ่มครัตช์ค้าง (Shift) แล้วเปลี่ยนเกียร์'); return;
  }
  doIt();
}
const shiftGear = d => selectGear(clamp(gearSel + d, 0, 4));
const changeSlope = d => { slope = clamp(slope + d, -6, 20); };

function startDynoRun() {
  if (S.isAuto) { if (S.speed * 3.6 > 1) return toast('หยุดรถก่อนวิ่งไดโน'); }
  else if (gearSel !== 0) return toast('ใส่เกียร์ว่าง (N) ก่อนวิ่งไดโน');
  if (!S.running) return toast('ต้องสตาร์ทเครื่องให้ติดก่อน (กดปุ่มสตาร์ทค้าง)');
  node?.port.postMessage({ type: 'dyno' });
}

window.addEventListener('keydown', e => {
  if (e.target.matches?.('input[type=number],input[type=text],select')) return;
  const k = e.key.toLowerCase();
  if (view === 'test' && $('#modal').hidden) {
    if (['arrowup', 'arrowdown', ' '].includes(k)) e.preventDefault();
    if (!e.repeat) {
      if (k === 'k') send('ignition', !S.ignition);
      else if (k === 'd') startDynoRun();
      else if (k === 'e' || k === 'arrowup') shiftGear(1);
      else if (k === 'q' || k === 'arrowdown') shiftGear(-1);
      else if (k === 'n') selectGear(0);
      else if (k === '[') changeSlope(-1);
      else if (k === ']') changeSlope(1);
      else if (k === 's') send('ignition', true);
    }
    keys.add(k === ' ' ? 'space' : k);
  }
  if (k === '+' || k === '=') setVolume(clamp(save.volume + 0.05, 0, 1));
  if (k === '-') setVolume(clamp(save.volume - 0.05, 0, 1));
});
window.addEventListener('keyup', e => { const k = e.key.toLowerCase(); keys.delete(k === ' ' ? 'space' : k); });

function setVolume(v) { save.volume = v; send('volume', v); persist(); const el = $('#volRange'); if (el) el.value = v; }

// ================================================================= ทดสอบ: DOM
function buildTest() {
  const auto = design.Auto === 1;
  const el = $('#v-test');
  el.innerHTML = `
  <div class="test-land">
   <div>
    <div class="test-top">
      <div class="gauge-wrap"><canvas id="gauge"></canvas><div class="status" id="status"></div></div>
      <div class="thr" id="thr"><div class="cap">คันเร่ง</div><div class="fill" id="thrFill"></div><div class="lbl" id="thrLbl">0%</div></div>
    </div>
   </div>
   <div>
    <div class="ctrl">
      <button id="bStart" class="green">สตาร์ท (ค้าง)</button>
      <button id="bKill" class="red">ดับเครื่อง</button>
      <button id="bDyno" class="blue">ไดโน</button>
      <button id="bBrake" class="red">เบรก (ค้าง)</button>
      ${auto ? `<div class="wide panel" style="margin:0;padding:6px 10px"><div class="small dim">ออโต้ CVT <span id="cvtTxt"></span></div><div class="bar"><i id="cvtBar"></i></div></div>
      <button id="bFull">เร่งสุด (ค้าง)</button><button id="bGradX">ค่อย ๆ เร่ง (ค้าง)</button>` :
      `<button id="bClutch">ครัตช์ (ค้าง)</button><button id="bGdn">เกียร์ −</button><button id="bGear" class="act" style="pointer-events:none">เกียร์ N</button><button id="bGup">เกียร์ +</button>
      <button id="bFull">เร่งสุด (ค้าง)</button><button id="bGradX">ค่อย ๆ เร่ง (ค้าง)</button><button id="bGradZ" class="wide">ค่อย ๆ เร่ง แตะรอบตัด (ค้าง)</button>`}
      ${auto ? '<button id="bGradZ" class="wide">ค่อย ๆ เร่ง แตะรอบตัด (ค้าง)</button>' : ''}
    </div>
    <div class="panel" style="margin-top:10px"><h3>ถนน / ความชัน</h3>
      <div class="row"><button id="bSlM" class="sm">−</button><div class="grow" style="text-align:center" id="slopeTxt">ถนนราบ</div><button id="bSlP" class="sm">+</button></div>
      <div class="btns" style="margin-top:6px"><button class="sm" data-sl="0">ราบ</button><button class="sm" data-sl="6">เนิน 6%</button><button class="sm" data-sl="12">ชัน 12%</button><button class="sm" data-sl="-4">ลง −4%</button></div>
    </div>
    <div class="panel"><h3>สถิติ  <span class="small dim">(ดีสุด = สถิติสูงสุดที่เก็บไว้ • ก่อนแก้ = ชุดเครื่องก่อนหน้า • ต่าง + = ดีกว่า)</span></h3>
      <table class="stats" id="statTbl"></table>
      <div class="row" style="margin-top:6px"><span class="small dim">วิธีวัด: หยุดนิ่งแล้วออกตัว ระบบจับเวลา 0–60, 100 ม., 402 ม. ให้เอง</span></div>
    </div>
   </div>
  </div>`;

  $('#bStart') && holdBtn($('#bStart'), 'start', dn => { if (dn) send('ignition', true); });
  holdBtn($('#bBrake'), 'brake'); holdBtn($('#bFull'), 'full'); holdBtn($('#bGradX'), 'x'); holdBtn($('#bGradZ'), 'z');
  if (!auto) {
    holdBtn($('#bClutch'), 'clutch');
    $('#bGup').onclick = () => shiftGear(1); $('#bGdn').onclick = () => shiftGear(-1);
    $('#bGear').onclick = () => selectGear(0);
  }
  $('#bKill').onclick = () => send('ignition', !S.ignition);
  $('#bDyno').onclick = startDynoRun;
  $('#bSlM').onclick = () => changeSlope(-1); $('#bSlP').onclick = () => changeSlope(1);
  $$('[data-sl]').forEach(b => { b.onclick = () => { slope = +b.dataset.sl; }; });

  // คันเร่งแบบลาก (ปล่อยแล้วคืนศูนย์)
  const thr = $('#thr');
  const setFrom = e => { const r = thr.getBoundingClientRect(); thrVal = clamp(1 - (e.clientY - r.top) / r.height, 0, 1); };
  thr.addEventListener('pointerdown', e => { e.preventDefault(); thrPointer = true; thr.setPointerCapture(e.pointerId); setFrom(e); });
  thr.addEventListener('pointermove', e => { if (thrPointer) setFrom(e); });
  const end = () => { thrPointer = false; thrVal = 0; };
  thr.addEventListener('pointerup', end); thr.addEventListener('pointercancel', end); thr.addEventListener('lostpointercapture', end);
  thr.addEventListener('contextmenu', e => e.preventDefault());
  statCache = '';
}

// ----------------------------------------------------------------- เกจ
let gaugeMax = 11000;
function drawGauge() {
  const cv = $('#gauge');
  if (!cv) return;
  const dpr = Math.min(window.devicePixelRatio || 1, 2.5), w = cv.clientWidth, h = cv.clientHeight;
  if (!w) return;
  if (cv.width !== Math.round(w * dpr) || cv.height !== Math.round(h * dpr)) { cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr); }
  const g = cv.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, w, h);
  const cx = w / 2, cy = h / 2, r = Math.min(w, h) / 2 - 18, k = r / 215;
  const lim = installed.D.Limiter, red = lim - 600;
  gaugeMax = Math.max(10000, Math.ceil((lim + 1000) / 1000) * 1000);
  const ang = rpm => (135 + 270 * clamp(rpm, 0, gaugeMax) / gaugeMax) * Math.PI / 180;
  g.lineCap = 'butt';
  g.lineWidth = 10 * k; g.strokeStyle = '#464c58'; g.beginPath(); g.arc(cx, cy, r, ang(0), ang(red)); g.stroke();
  g.strokeStyle = '#ef4b4b'; g.beginPath(); g.arc(cx, cy, r, ang(red), ang(gaugeMax)); g.stroke();
  g.fillStyle = '#e8ecf1'; g.font = `${Math.max(12, 26 * k)}px system-ui`; g.textAlign = 'center'; g.textBaseline = 'middle';
  for (let rpm = 0; rpm <= gaugeMax; rpm += 500) {
    const a = ang(rpm), major = rpm % 1000 === 0, c = Math.cos(a), s = Math.sin(a);
    g.strokeStyle = major ? '#e8ecf1' : '#8b95a3'; g.lineWidth = (major ? 3 : 1.5) * k;
    g.beginPath(); g.moveTo(cx + c * (r - 8 * k), cy + s * (r - 8 * k)); g.lineTo(cx + c * (r - (major ? 30 : 20) * k), cy + s * (r - (major ? 30 : 20) * k)); g.stroke();
    if (major) { g.fillStyle = rpm >= red ? '#ef4b4b' : '#e8ecf1'; g.fillText(String(rpm / 1000), cx + c * (r - 54 * k), cy + s * (r - 54 * k)); }
  }
  g.fillStyle = '#8b95a3'; g.font = `${Math.max(10, 16 * k)}px system-ui`; g.fillText('x1000 rpm', cx, cy - 80 * k);
  // ตัวเลขรอบ
  g.fillStyle = '#e8ecf1'; g.font = `600 ${Math.max(22, 70 * k)}px system-ui`;
  g.fillText(String(Math.round(needle / 10) * 10), cx, cy + 95 * k);
  // ความเร็ว + เกียร์
  g.fillStyle = '#ffa21a'; g.font = `600 ${Math.max(22, 78 * k)}px system-ui`; g.fillText(String(Math.round(S.speed * 3.6)), cx, cy + 165 * k);
  g.fillStyle = '#e8ecf1'; g.font = `${Math.max(11, 20 * k)}px system-ui`;
  const gearTxt = S.isAuto ? 'ออโต้ ' + Cvt.ratio(S.cvtS).toFixed(2) : 'เกียร์ ' + (S.gear === 0 ? 'N' : S.gear);
  g.fillText(`กม./ชม.   ${gearTxt}`, cx, cy + 205 * k);
  if (S.dynoPhase !== 0) { g.fillStyle = '#3ccf7a'; g.font = `${Math.max(12, 24 * k)}px system-ui`; g.fillText('กำลังวัดไดโน...', cx, cy - 112 * k); }
  else if (!S.running) { g.fillStyle = '#ffa21a'; g.font = `${Math.max(12, 22 * k)}px system-ui`; g.fillText(S.ignition ? 'กดสตาร์ทค้าง' : 'ดับเครื่องอยู่ - กดสตาร์ท', cx, cy - 112 * k); }
  // เข็ม
  const na = ang(needle);
  g.strokeStyle = '#ffa21a'; g.lineWidth = 9 * k; g.lineCap = 'round';
  g.beginPath(); g.moveTo(cx + Math.cos(na + Math.PI) * 26 * k, cy + Math.sin(na + Math.PI) * 26 * k); g.lineTo(cx + Math.cos(na) * (r - 26 * k), cy + Math.sin(na) * (r - 26 * k)); g.stroke();
  g.fillStyle = '#3c424e'; g.beginPath(); g.arc(cx, cy, 16 * k, 0, 7); g.fill();
}

// ----------------------------------------------------------------- สถิติ (ตาราง)
let statCache = '';
function updateStats() {
  const b = before ?? newRun();
  const fmt = (v, f, u = '') => (v > 0 ? v.toFixed(f) + u : '-');
  const row = (name, best, bef, last, lowerBetter, f, lastTxt) => {
    const ref = before ? bef : best;
    let diff = '-', cls = 'dim';
    if (ref > 0 && last > 0) {
      const d = lowerBetter ? ref - last : last - ref;
      diff = (d > 0.0049 ? '+' : '') + d.toFixed(f); cls = Math.abs(d) < 0.005 ? 'dim' : d > 0 ? 'green-t' : 'red-t';
    }
    return `<tr><td>${name}</td><td>${fmt(best, f)}</td><td class="dim">${fmt(bef, f)}</td><td class="accent">${lastTxt ?? fmt(last, f)}</td><td class="${cls}">${diff}</td></tr>`;
  };
  const hp = dynoRes ? dynoRes.ps * PsToHp : 0;
  const html = `<tr><th></th><th>ดีสุด</th><th>ก่อนแก้</th><th>ล่าสุด</th><th>ต่าง</th></tr>` +
    row('รอบสูงสุด (rpm)', cur.peakRpm, b.peakRpm, peakRpm, false, 0) +
    row('0–60 (วิ)', save.best.b060, b.b060, last060, true, 2) +
    row('100 ม. (วิ)', save.best.b100, b.b100, last100, true, 2) +
    row(last402 > 0 ? `402 ม. (วิ) @${trap402.toFixed(0)}` : '402 ม. (วิ)', save.best.b402, b.b402, last402, true, 2) +
    row('ท็อป (กม./ชม.)', save.best.top, b.top, topSession, false, 0) +
    row('แรงม้า (HP)', cur.hp, b.hp, hp, false, 1);
  if (html !== statCache) { statCache = html; $('#statTbl').innerHTML = html; }
}

// ----------------------------------------------------------------- วัดถนน
let roadArmed = true, roadTiming = false, roadStart = 0, distStart = 0, got060 = false, got100 = false, got402 = false;
function roadTests(now) {
  const kmh = S.speed * 3.6;
  if (kmh > topSession) topSession = kmh;
  if (topSession > cur.top) cur.top = topSession;
  if (topSession > save.best.top + 0.5) { save.best.top = topSession; persist(); }
  if (kmh < 1) { roadArmed = true; roadTiming = false; distStart = S.dist; }
  else if (roadArmed && !roadTiming) {
    roadArmed = false;
    if (kmh < 8) { roadTiming = true; roadStart = now; got060 = got100 = got402 = false; }
  }
  if (!roadTiming) return;
  const el = now - roadStart, dist = S.dist - distStart;
  if (!got060 && kmh >= 60) {
    got060 = true; last060 = el;
    if (el >= 0.8) { cur.b060 = bestOf(cur.b060, el); if (save.best.b060 < 0 || el < save.best.b060) { save.best.b060 = el; persist(); } }
  }
  if (!got100 && dist >= 100) {
    got100 = true; last100 = el;
    if (el >= 2) { cur.b100 = bestOf(cur.b100, el); if (save.best.b100 < 0 || el < save.best.b100) { save.best.b100 = el; persist(); } }
  }
  if (!got402 && dist >= 402.3) {
    got402 = true; last402 = el; trap402 = kmh;
    if (el >= 5) { cur.b402 = bestOf(cur.b402, el); if (save.best.b402 < 0 || el < save.best.b402) { save.best.b402 = el; persist(); } }
  }
}

// ----------------------------------------------------------------- ลูปหลัก
let needle = 0, lastT = performance.now() / 1000, frame = 0;
function tick() {
  requestAnimationFrame(tick);
  const now = performance.now() / 1000;
  const dt = Math.min(0.1, now - lastT); lastT = now;
  const active = view === 'test' && $('#modal').hidden;
  let thr = 0, brake = false, starter = false;
  if (active) {
    const zk = keys.has('z') || held.z, xk = keys.has('x') || held.x;
    if (zk || xk) {
      const ceil = zk ? S.limiter - 40 : S.limiter - 800;
      const pred = S.rpm + rpmRate * 0.4;
      gradThr = clamp(gradThr + 0.8 * clamp((ceil - pred) / 350, -1.5, 1) * dt, 0, 1);
    } else gradThr = Math.max(0, gradThr - 3 * dt);
    thr = gradThr;
    if (keys.has('w') || held.full) { thr = 1; gradThr = 1; }
    if (thrPointer) thr = thrVal;
    brake = keys.has('space') || keys.has('b') || held.brake;
    starter = keys.has('s') || held.start;
  } else { gradThr = 0; }
  send('throttleInput', +thr.toFixed(3));
  send('brakeInput', brake ? 1 : 0);
  send('starterHeld', starter);
  send('clutchInput', active && (keys.has('shift') || held.clutch || now < assistUntil) ? 1 : 0);
  send('gearInput', gearSel);
  send('slopePct', slope);

  if (dt > 0) { rpmRate += ((S.rpm - lastRpmG) / dt - rpmRate) * 0.3; lastRpmG = S.rpm; }
  needle += (S.rpm - needle) * Math.min(1, dt * 25);
  if (S.rpm > peakRpm) peakRpm = S.rpm;
  if (peakRpm > cur.peakRpm) cur.peakRpm = peakRpm;
  roadTests(now);
  frame++;

  if (view === 'test') {
    drawGauge();
    if (frame % 3 === 0) updateTestDom();
  }
}

function updateTestDom() {
  const fill = $('#thrFill'); if (!fill) return;
  fill.style.height = (S.thr * 100).toFixed(0) + '%';
  $('#thrLbl').textContent = Math.round(S.thr * 100) + '%';
  const st = $('#status');
  const lim = S.cut || needle > S.limiter - 150;
  st.innerHTML = `<span class="chip ${S.cut ? 'on' : ''}">ตัดรอบ</span>` +
    (installed.D.SoftGap > 0 ? `<span class="chip ${S.softK > 0 && !S.cut ? 'on' : ''}">soft cut</span>` : '') +
    `<span class="chip ${S.clutchLock > 0.05 ? 'ok' : ''}">คลัตช์จับ ${Math.round(S.clutchLock * 100)}%</span>` +
    `<span class="chip">${installed.D.Auto ? 'CVT' : 'เกียร์ ' + (S.gear || 'N')}</span>` +
    `<span class="chip">${tireCode(installed.D)}</span>` + (lim && !S.cut ? '<span class="chip on">ใกล้รอบตัด</span>' : '');
  const bk = $('#bKill'); bk.classList.toggle('on', !S.ignition);
  const sl = slope === 0 ? 'ถนนราบ' : slope > 0 ? `ขึ้นเนิน ${slope}%` : `ลงเนิน ${-slope}%`;
  $('#slopeTxt').textContent = sl;
  $$('[data-sl]').forEach(b => b.classList.toggle('on', +b.dataset.sl === slope));
  if (S.isAuto) { const bar = $('#cvtBar'); if (bar) { bar.style.width = (S.cvtS * 100) + '%'; $('#cvtTxt').textContent = Cvt.ratio(S.cvtS).toFixed(2); } }
  else { const gb = $('#bGear'); if (gb) gb.textContent = 'เกียร์ ' + (gearSel === 0 ? 'N' : gearSel); }
  $('#topinfo').textContent = `${installed.Cc.toFixed(0)} cc • ${installed.PeakHp.toFixed(1)} HP`;
  updateStats();
}

// ================================================================= ไดโน
function finishDyno(tqRaw) {
  if (S.dynoFailed) { dynoRes = null; toast('วิ่งไดโนไม่จบ: เครื่องดับระหว่างวัด'); return; }
  const tq = Array.from(tqRaw);
  const sm = tq.map((_, i) => {
    let s = 0, n = 0;
    for (let j = Math.max(0, i - 2); j <= Math.min(tq.length - 1, i + 2); j++) if (!Number.isNaN(tq[j])) { s += tq[j]; n++; }
    return Number.isNaN(tq[i]) || n === 0 ? NaN : s / n;
  });
  let pk = { ps: 0, psRpm: 0, tq: 0, tqRpm: 0 }, any = false;
  sm.forEach((t, i) => {
    if (Number.isNaN(t)) return;
    any = true; const rpm = i * 100, ps = Ps(t, rpm);
    if (t > pk.tq) { pk.tq = t; pk.tqRpm = rpm; }
    if (ps > pk.ps) { pk.ps = ps; pk.psRpm = rpm; }
  });
  if (!any) return;
  dynoRes = { curve: sm, model: installed, ps: pk.ps, psRpm: pk.psRpm, peakTq: pk.tq, tqRpm: pk.tqRpm };
  cur.hp = pk.ps * PsToHp;
  openDynoModal();
}
function openDynoModal() {
  const r = dynoRes, m = r.model, d = m.D;
  $('#mTitle').textContent = `ผลวัดไดโน (${m.Cc.toFixed(0)} cc)`;
  $('#modal').hidden = false;
  const rpm = r.curve.map((_, i) => i * 100), hp = r.curve.map((t, i) => (Number.isNaN(t) ? NaN : Hp(t, i * 100)));
  requestAnimationFrame(() => drawChart($('#mChart'), rpm, r.curve, hp, d.Limiter));
  let cmp = '';
  if (d.RealHp > 0) cmp += `<div class="yellow-t">เทียบรถจริง: แรงม้า เกม ${(r.ps * PsToHp).toFixed(1)} / จริง ${d.RealHp.toFixed(1)} HP (${((r.ps * PsToHp / d.RealHp - 1) * 100).toFixed(0)}%)</div>`;
  if (d.RealTq > 0) cmp += `<div class="yellow-t">แรงบิด เกม ${r.peakTq.toFixed(1)} / จริง ${d.RealTq.toFixed(1)} N·m (${((r.peakTq / d.RealTq - 1) * 100).toFixed(0)}%)</div>`;
  $('#mBody').innerHTML = `<div class="accent" style="font-size:1.15rem">กำลังสูงสุด ${(r.ps * PsToHp).toFixed(1)} HP @ ${fmtN(r.psRpm)} rpm</div>
    <div class="blue-t" style="font-size:1.15rem">แรงบิดสูงสุด ${r.peakTq.toFixed(1)} N·m @ ${fmtN(r.tqRpm)} rpm</div>
    <div class="dim small">ซีซี ${m.Cc.toFixed(0)}  •  HP ต่อลิตร = ${(r.ps * PsToHp / m.Cc * 1000).toFixed(0)}</div>${cmp}`;
}
$('#mClose').onclick = () => { $('#modal').hidden = true; };

// ================================================================= กราฟ (แรงบิด + แรงม้า)
function drawChart(cv, rpm, tq, hp, limiter) {
  const dpr = Math.min(window.devicePixelRatio || 1, 2.5), w = cv.clientWidth, h = cv.clientHeight;
  if (!w) return;
  cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr);
  const g = cv.getContext('2d'); g.setTransform(dpr, 0, 0, dpr, 0, 0); g.clearRect(0, 0, w, h);
  const L = 38, R = 38, T = 12, B = 22, pw = w - L - R, ph = h - T - B;
  let xMax = Math.ceil((limiter + 500) / 1000) * 1000, xMin = 2000;
  const maxTq = Math.max(1, ...tq.filter(v => !Number.isNaN(v))), maxHp = Math.max(1, ...hp.filter(v => !Number.isNaN(v)));
  const yT = Math.ceil(maxTq * 1.15 / 5) * 5, yH = Math.ceil(maxHp * 1.15 / 5) * 5;
  const X = r => L + (r - xMin) / (xMax - xMin) * pw;
  g.font = '11px system-ui'; g.textBaseline = 'middle';
  g.strokeStyle = '#20262e'; g.lineWidth = 1; g.fillStyle = '#8b95a3';
  for (let i = 0; i <= 4; i++) {
    const y = T + ph * i / 4; g.beginPath(); g.moveTo(L, y); g.lineTo(L + pw, y); g.stroke();
    g.textAlign = 'right'; g.fillStyle = '#4aa3ff'; g.fillText((yT * (1 - i / 4)).toFixed(0), L - 4, y);
    g.textAlign = 'left'; g.fillStyle = '#ffa21a'; g.fillText((yH * (1 - i / 4)).toFixed(0), L + pw + 4, y);
  }
  g.fillStyle = '#8b95a3'; g.textAlign = 'center';
  for (let r = Math.ceil(xMin / 1000) * 1000; r <= xMax; r += 1000) { g.fillText(String(r / 1000), X(r), h - 8); g.strokeStyle = '#161b21'; g.beginPath(); g.moveTo(X(r), T); g.lineTo(X(r), T + ph); g.stroke(); }
  g.strokeStyle = '#ef4b4b'; g.setLineDash([4, 4]); g.beginPath(); g.moveTo(X(limiter), T); g.lineTo(X(limiter), T + ph); g.stroke(); g.setLineDash([]);
  const line = (arr, col, ymax) => {
    g.strokeStyle = col; g.lineWidth = 2.2; g.beginPath(); let open = false;
    arr.forEach((v, i) => { const r = rpm[i]; if (Number.isNaN(v) || r < xMin || r > xMax) { open = false; return; } const x = X(r), y = T + ph * (1 - clamp(v, 0, ymax) / ymax); if (!open) { g.moveTo(x, y); open = true; } else g.lineTo(x, y); });
    g.stroke();
  };
  line(tq, '#4aa3ff', yT); line(hp, '#ffa21a', yH);
  g.textAlign = 'left'; g.fillStyle = '#4aa3ff'; g.fillText('แรงบิด N·m', L + 6, T + 8); g.fillStyle = '#ffa21a'; g.fillText('แรงม้า HP', L + 90, T + 8);
}

// ================================================================= อู่
let gTab = 0, gDirty = false;
const advC = Adv.newConstraints();
let advGoal = 0, advKey = '', advTips = [], advList = [], advBusy = false;

function draftChanged() { preview = new EngineModel(draft); }

function assemble() {
  const changed = JSON.stringify(draft) !== JSON.stringify(design);
  if (changed) {
    if (runAny(cur)) before = cur;
    cur = newRun(); last060 = last100 = last402 = -1; peakRpm = 0; topSession = 0; dynoRes = null;
  }
  design = { ...draft };
  save.design = design; persist();
  installed = new EngineModel(design);
  node?.port.postMessage({ type: 'assemble', design });
  gearSel = 0; roadArmed = false; roadTiming = false;
  buildTest(); setView('test');
  toast(design.Auto === 1 ? 'ประกอบแล้ว กดสตาร์ทค้าง แล้วบิดคันเร่งออกตัวได้เลย (ออโต้)' : 'ประกอบแล้ว กดสตาร์ทค้าง แล้วเข้าเกียร์ด้วยปุ่มเกียร์ +');
}

function healthLines(m, d) {
  const L = [];
  const add = (t, s) => L.push(`<div class="line"><i class="dot ${s === 2 ? 'y' : s === 1 ? 'g' : ''}"></i><span class="${s === 2 ? 'yellow-t' : ''}">${t}</span></div>`);
  add(`เดินเบา ~${fmtN(m.IdleRpm)} rpm  •  โอเวอร์แลป ${m.Overlap.toFixed(0)}°${m.Overlap > 40 ? '  (สะดุด)' : ''}`, m.Overlap > 40 ? 2 : 1);
  const flowLimit = m.RpmFlow < m.PeakPsRpm;
  add(`ไอดีเริ่มอั้นที่ ${fmtN(m.RpmFlow)} rpm${flowLimit ? '  (เรือน/วาล์วเล็กไป)' : ''}`, flowLimit ? 2 : 1);
  const fb = d.Limiter >= m.FloatRpm;
  add(`วาล์วลอยที่ ${fmtN(m.FloatRpm)} rpm${fb ? '  รอบตัดสูงไป' : ''}`, fb ? 2 : 1);
  const vp = m.PistonSpeed(d.Limiter);
  add(`ลูกสูบเฉลี่ย ${vp.toFixed(1)} ม./วิ ที่รอบตัด${vp > 24 ? '  (ชักยาวเกิน กำลังตก)' : ''}`, vp > 24 ? 2 : vp > 22 ? 0 : 1);
  const kn = m.Knock(3500, m.Load(1, 3500)), margin = m.CrLimit - m.CrDyn, fuel = Fuels[clamp(d.Fuel, 0, 6)].name;
  add(kn > 0.02 ? `น็อก! CR ${d.Cr.toFixed(1)} (${m.Psi.toFixed(0)} psi) เกินพิกัด ${fuel} (${m.CrLimit.toFixed(1)})` : `CR ${d.Cr.toFixed(1)}  •  ${m.Psi.toFixed(0)} psi  •  พิกัด ${fuel} ${m.CrLimit.toFixed(1)} (เหลือ ${margin.toFixed(1)})`, kn > 0.02 || margin < 0.5 ? 2 : 1);
  if (d.Cr < 8.8) add('กำลังอัดต่ำ: เดินเบาจุดไม่ติดเป็นพัก ๆ', 2);
  if (m.Hunt > 0.25) add(`แคมองศามาก: เดินเบาหอบ (รอบเหวี่ยงขึ้น-ลง ~${(m.Hunt * 100).toFixed(0)}%)`, 2);
  const idle = m.Lope < 0.12 ? 'เดินเบานิ่ง' : m.Lope < 0.5 ? 'เดินเบาสะดุดเบา ๆ' : 'เดินเบาลูปจัด';
  const tone = m.SoundBright < -0.35 ? 'นุ่ม' : m.SoundBright > 0.55 ? 'แผดคม' : 'กลาง';
  const size = m.PitchStretch < 0.92 ? '  •  เสียงเล็กแหลม' : m.PitchStretch > 1.08 ? '  •  เสียงใหญ่ทุ้ม' : '';
  add(`เสียง: ${idle}  •  โทน${tone}${size}`, 0);
  if (d.Auto === 1) {
    const hold = Cvt.holdRpm(m), ok = hold >= m.PeakPsRpm * 0.93 && hold <= m.PeakPsRpm * 1.08;
    const v = hold < m.PeakPsRpm * 0.93 ? 'ถือรอบต่ำกว่าพีค' : hold > m.PeakPsRpm * 1.08 ? 'ถือรอบเกินพีค' : 'ใกล้รอบพีค (ดี)';
    add(`CVT ถือ ~${fmtN(hold)} rpm: ${v}  •  ท็อป ${Veh.topSpeedAuto(m).kmh.toFixed(0)} กม./ชม.`, ok ? 1 : 2);
  } else {
    const sp = [1, 2, 3, 4].map(g => d.Limiter * 2 * Math.PI / 60 * Veh.wheelRadius(d) / Veh.total(d, g) * 3.6);
    add(`ความเร็วที่รอบตัด: เกียร์ 1 ${sp[0].toFixed(0)}  •  2 ${sp[1].toFixed(0)}  •  3 ${sp[2].toFixed(0)}  •  4 ${sp[3].toFixed(0)} กม./ชม.`, 0);
    const top = Veh.topSpeed(m, 4);
    const v = top.rpm >= d.Limiter - 150 ? '  รอบตัดมาก่อน ปลายสั้น' : top.rpm < m.PeakPsRpm * 0.88 ? '  ปลายยาว ลากไม่ถึงพีค' : '';
    add(`ท็อปเกียร์ 4: ${top.kmh.toFixed(0)} กม./ชม. @ ${fmtN(top.rpm)} rpm${v}`, v ? 2 : 1);
  }
  return L.join('');
}

function perfPanel() {
  const m = preview, d = draft;
  let real = '';
  if (d.RealHp > 0 || d.RealTq > 0) {
    const calOn = Math.abs(d.CalK - 1) > 0.005;
    real = `<div class="small ${calOn ? 'green-t' : 'yellow-t'}">รถจริง: ` +
      (d.RealHp > 0 ? `${d.RealHp.toFixed(1)} HP${d.RealHpRpm > 0 ? ' @ ' + fmtN(d.RealHpRpm) : ''} (โมเดลต่าง ${((m.PeakHp / d.RealHp - 1) * 100).toFixed(0)}%)   ` : '') +
      (d.RealTq > 0 ? `${d.RealTq.toFixed(1)} N·m${d.RealTqRpm > 0 ? ' @ ' + fmtN(d.RealTqRpm) : ''} (ต่าง ${((m.PeakTq / d.RealTq - 1) * 100).toFixed(0)}%)` : '') + '</div>';
  }
  const same = JSON.stringify(draft) === JSON.stringify(design);
  return `<div class="panel"><h3>สมรรถนะ (คาดการณ์)</h3>
    <div class="big-num"><div><b class="accent">${m.PeakHp.toFixed(1)}</b> <span>HP @ ${fmtN(m.PeakPsRpm)} rpm</span></div><div><b class="blue-t">${m.PeakTq.toFixed(1)}</b> <span>N·m @ ${fmtN(m.PeakTqRpm)} rpm</span></div></div>
    <div class="small dim">${m.Cc.toFixed(1)} cc • ก้าน/ชัก ${m.RodRatio.toFixed(2)} • รถ+คน ${Veh.mass(d).toFixed(0)} กก. • ${(m.PeakHp / m.Cc * 1000).toFixed(0)} HP/ลิตร • ยาง ${tireCode(d)}</div>${real}
    <canvas class="chart" id="gChart" style="margin-top:8px"></canvas></div>
    <div class="panel"><h3>ตรวจสภาพเครื่อง</h3><div id="health">${healthLines(m, d)}</div></div>
    <div class="row" style="gap:8px;flex-wrap:wrap">${d.Auto === 1 ? '<button id="bTune" class="blue grow">จูนเม็ดอัตโนมัติ</button>' : ''}<button id="bAsm" class="green ${same ? '' : 'on'} grow" style="min-height:52px">${same ? 'ไปทดสอบ (แบบนี้ติดตั้งอยู่)' : 'ประกอบแล้วไปทดสอบ'}</button></div>`;
}
function drawGarageChart() {
  const cv = $('#gChart'); if (!cv) return;
  const m = preview, d = draft, rpm = [], tq = [], hp = [];
  for (let r = 2000; r <= d.Limiter; r += 100) { const t = m.BrakeTorqueWot(r); rpm.push(r); tq.push(Math.max(0, t)); hp.push(Math.max(0, Hp(t, r))); }
  drawChart(cv, rpm, tq, hp, d.Limiter);
}

function rowHtml(p) {
  const v = p.get(draft);
  return `<div class="prow" data-k="${p.key}"><div class="name"><span>${p.name}</span><span class="val">${paramDisplay(p, draft)}</span></div>
    <div class="ctl"><button data-n="-1">−</button><input type="range" min="${p.min}" max="${p.max}" step="${p.step}" value="${v}"><button data-n="1">+</button></div>
    <div class="hint">${p.hint}</div></div>`;
}
function partsHtml() {
  const setOpts = Presets.map((s, i) => `<option value="${i}">${s[0]}</option>`).join('');
  let h = `<div class="panel"><h3>เช็ตเครื่องสำเร็จรูป (* = ตัวอย่างสมมติ ไม่ใช่สเปกรถจริง)</h3><select id="setSel"><option value="">เลือกเช็ต…</option>${setOpts}</select></div><div class="panel"><h3>อะไหล่ (แยกเป็นชิ้น)</h3>`;
  PartCats.forEach((cat, ci) => {
    const cu = partCurrent(cat, draft);
    h += `<div class="prow" data-c="${ci}"><div class="name"><span>${cat.name}</span></div><div class="ctl"><button data-s="-1">◀</button>
      <select>${cu < 0 ? '<option value="-1" selected>ปรับเอง (ไม่ตรงชุดสำเร็จรูป)</option>' : ''}${cat.items.map((it, i) => `<option value="${i}" ${i === cu ? 'selected' : ''}>${it.name}</option>`).join('')}</select><button data-s="1">▶</button></div>
      <div class="hint">${cat.hint}</div></div>`;
  });
  return h + '</div>';
}

function renderGarage() {
  const el = $('#v-garage'), keepScroll = el.scrollTop;
  const [, keys_] = GarageTabs[gTab];
  const chips = GarageTabs.map((t, i) => `<button data-t="${i}" class="${i === gTab ? 'on' : ''}">${t[0]}</button>`).join('');
  let left;
  if (gTab === 0) left = partsHtml();
  else {
    const rows = Params.filter(p => keys_.includes(p.key) && (!p.visible || p.visible(draft))).map(rowHtml).join('');
    const calib = keys_.includes('realhp') ? `<div class="row" style="margin-top:8px;gap:8px"><button id="bCal" class="green grow">ปรับเทียบโมเดลให้ตรงรถจริง</button><button id="bCalClr" class="red">ล้างค่า</button></div>` : '';
    left = `<div class="panel"><h3>${GarageTabs[gTab][0]}</h3>${rows}${calib}</div>`;
  }
  el.innerHTML = `<div class="chips" id="gChips">${chips}</div><div class="twocol"><div>${left}</div><div id="gRight">${perfPanel()}</div></div>`;
  el.scrollTop = keepScroll;
  drawGarageChart();
  bindGarage();
}
function refreshGarageRight() {
  $('#gRight').innerHTML = perfPanel(); drawGarageChart(); bindGarageButtons();
}
function bindGarageButtons() {
  $('#bAsm')?.addEventListener('click', assemble);
  $('#bTune')?.addEventListener('click', () => {
    const { g, hold } = Cvt.suggestRoller(preview);
    draft.RollerG = g; draft.RollerG2 = g; draftChanged(); renderGarage();
    toast(`จูนเม็ดเป็น ${g} กรัม ทุกเม็ด  CVT ถือรอบ ~${fmtN(hold)} rpm (พีคกำลัง ${fmtN(preview.PeakPsRpm)})`);
  });
}
function bindGarage() {
  const el = $('#v-garage');
  $$('#gChips button', el).forEach(b => { b.onclick = () => { gTab = +b.dataset.t; renderGarage(); el.scrollTop = 0; }; });
  bindGarageButtons();
  $('#setSel')?.addEventListener('change', e => { if (e.target.value === '') return; draft = { ...Presets[+e.target.value][1] }; draftChanged(); renderGarage(); toast('เช็ตเครื่อง: ' + Presets[+e.target.value][0]); });
  $$('.prow[data-k]', el).forEach(row => {
    const p = paramByKey[row.dataset.k], rng = $('input', row);
    rng.addEventListener('input', () => {
      let v = Math.round(+rng.value / p.step) * p.step; v = clamp(+v.toFixed(6), p.min, p.max);
      p.set(draft, v); draftChanged();
      $('.val', row).textContent = paramDisplay(p, draft);
      if (!gDirty) { gDirty = true; requestAnimationFrame(() => { gDirty = false; refreshGarageRight(); }); }
    });
    rng.addEventListener('change', () => renderGarage());
    $$('button[data-n]', row).forEach(b => { b.onclick = () => { nudge(p, draft, +b.dataset.n, false); draftChanged(); renderGarage(); }; });
  });
  $$('.prow[data-c]', el).forEach(row => {
    const cat = PartCats[+row.dataset.c], sel = $('select', row);
    const apply = i => { if (i < 0 || i >= cat.items.length) return; cat.items[i].apply(draft); draftChanged(); renderGarage(); };
    sel.onchange = () => apply(+sel.value);
    $$('button[data-s]', row).forEach(b => {
      b.onclick = () => { const c = partCurrent(cat, draft), n = cat.items.length, d = +b.dataset.s; apply(c < 0 ? (d > 0 ? 0 : n - 1) : clamp(c + d, 0, n - 1)); };
    });
  });
  $('#bCal')?.addEventListener('click', () => {
    const k = calibrate(draft);
    if (k == null) return toast('ใส่แรงม้าหรือแรงบิดของรถจริงก่อน');
    draft.CalK = k; draftChanged(); renderGarage();
    toast(`ปรับเทียบแล้ว: ตัวคูณ ${k.toFixed(2)}  โมเดล ${preview.PeakHp.toFixed(1)} HP / ${preview.PeakTq.toFixed(1)} N·m`);
  });
  $('#bCalClr')?.addEventListener('click', () => { draft.RealHp = draft.RealHpRpm = draft.RealTq = draft.RealTqRpm = 0; draft.CalK = 1; draftChanged(); renderGarage(); toast('ล้างค่ารถจริงแล้ว'); });
}

// ================================================================= ที่ปรึกษาจูน
function renderAdvisor() {
  const el = $('#v-adv');
  const key = JSON.stringify([draft, advGoal, advC, design, last402, topSession, peakRpm, dynoRes?.ps, before?.b402]);
  const chip = (i, g) => `<button data-lk="${i}" class="sm yellow ${(advC.lockMask & (1 << i)) ? 'on' : ''}">${(advC.lockMask & (1 << i)) ? '[ล็อก] ' : ''}${g[0]}</button>`;
  el.innerHTML = `
  <div class="twocol"><div>
    <div class="panel"><h3>เป้าหมาย</h3><div class="btns">${Adv.GoalNames.map((n, i) => `<button data-goal="${i}" class="${i === advGoal ? 'on' : ''}">${n}</button>`).join('')}</div></div>
    <div class="panel"><h3>โจทย์ของเรา</h3>
      <div class="field"><span class="small dim">รอบที่อยากใช้</span><button class="sm" id="capM">−</button><b id="capV" class="${advC.rpmCap > 0 ? 'accent' : 'dim'}">${advC.rpmCap > 0 ? fmtN(advC.rpmCap) + ' rpm' : 'ไม่จำกัด'}</b><button class="sm" id="capP">+</button>
        <button class="sm blue" id="capUse">${advC.rpmCap > 0 ? 'ยกเลิกรอบ' : 'ใช้รอบเครื่องนี้'}</button></div>
      <div class="field"><button class="sm green ${advC.street ? 'on' : ''}" id="street">โจทย์ใช้ถนน: ${advC.street ? 'เปิด (เดินเบาต้องนิ่ง)' : 'ปิด'}</button></div>
      <div class="small dim" style="margin-top:6px">ล็อก (ใช้อะไหล่ตามที่เราตั้งในอู่ ที่ปรึกษาไม่แตะ):</div>
      <div class="btns" style="margin-top:4px">${Adv.Groups.map((g, i) => chip(i, g)).join('')}</div></div>
    <div class="panel"><h3>วิเคราะห์จากผลทดสอบ (เครื่องที่ติดตั้งอยู่)</h3><div id="advTips" class="small dim">กำลังคำนวณ…</div></div>
  </div><div>
    <div class="panel"><h3>ควรปรับอะไร  (ตามโจทย์ จัดอันดับตามเป้าหมาย) — คำนวณบนแบบในอู่</h3><div id="advList" class="small dim">กำลังคำนวณ…</div></div>
  </div></div>`;
  $$('[data-goal]', el).forEach(b => { b.onclick = () => { advGoal = +b.dataset.goal; renderAdvisor(); }; });
  $$('[data-lk]', el).forEach(b => { b.onclick = () => { advC.lockMask ^= 1 << +b.dataset.lk; renderAdvisor(); }; });
  const stepCap = d => { const c = advC.rpmCap > 0 ? advC.rpmCap : Math.round(draft.Limiter / 100) * 100; advC.rpmCap = clamp(c + d, 6000, 15000); renderAdvisor(); };
  $('#capM').onclick = () => stepCap(-100); $('#capP').onclick = () => stepCap(100);
  $('#capUse').onclick = () => { advC.rpmCap = advC.rpmCap > 0 ? 0 : Math.round(draft.Limiter / 100) * 100; renderAdvisor(); };
  $('#street').onclick = () => { advC.street = !advC.street; renderAdvisor(); };
  advBusy = true;
  setTimeout(() => {
    if (view !== 'adv') { advBusy = false; return; }
    advList = Adv.suggest(draft, advGoal, 5, advC);
    let capped = draft;
    if (advC.rpmCap > 0 && advList.length && advList[0].gain >= 999) capped = advList[0].design;
    const t = { peakRpm, top: topSession, last060, last100, last402, trap402, dyno: dynoRes ? { ps: dynoRes.ps, psRpm: dynoRes.psRpm, tq: dynoRes.peakTq, tqRpm: dynoRes.tqRpm } : null, before, cur };
    advTips = [...Adv.capTips(capped, advC), ...Adv.observe(installed, t)];
    advKey = key; advBusy = false;
    paintAdvisor();
  }, 30);
}
function paintAdvisor() {
  const tipsEl = $('#advTips'), listEl = $('#advList');
  if (!tipsEl) return;
  tipsEl.className = '';
  tipsEl.innerHTML = advTips.map(([lv, txt]) => `<div class="line"><i class="dot ${lv === 2 ? 'y' : lv === 1 ? 'g' : ''}"></i><span class="${lv === 2 ? 'yellow-t' : lv === 1 ? 'green-t' : ''}">${txt}</span></div>`).join('');
  listEl.className = '';
  if (!advList.length) { listEl.innerHTML = '<div class="dim small">ไม่พบการปรับค่าเดี่ยว ๆ ที่ดีขึ้นอย่างมีนัยสำคัญโดยไม่ทำให้น็อก/วาล์วลอย — ลองเปลี่ยนหลายค่าพร้อมกัน เช่น ท่อ+แคม+รอบตัด</div>'; return; }
  listEl.innerHTML = advList.map((s, i) => {
    const a = s.after, b = s.before;
    return `<div class="sug"><div class="t"><b>${s.label}</b><span>402 ม. ${b.t402.toFixed(2)}→${a.t402.toFixed(2)} วิ • 0–60 ${b.t060.toFixed(2)}→${a.t060.toFixed(2)} • ท็อป ${b.top.toFixed(0)}→${a.top.toFixed(0)} กม./ชม.</span>${s.note ? `<em>${s.note}</em>` : ''}</div><button class="green" data-use="${i}">ใช้ค่านี้</button></div>`;
  }).join('');
  $$('[data-use]', listEl).forEach(b => {
    b.onclick = () => { const s = advList[+b.dataset.use]; draft = { ...s.design }; draftChanged(); toast('ปรับตามคำแนะนำ: ' + s.label); renderAdvisor(); };
  });
}

// ================================================================= ตั้งค่า
function renderSettings() {
  const el = $('#v-set');
  el.innerHTML = `<div class="twocol"><div>
    <div class="panel"><h3>ระดับเสียง</h3><input type="range" id="volRange" min="0" max="1" step="0.01" value="${save.volume}" style="width:100%"></div>
    <div class="panel"><h3>ควบคุม</h3>
      <div class="field"><button id="assist" class="${save.assist ? 'on' : ''}">ช่วยกำครัตช์อัตโนมัติตอนเปลี่ยนเกียร์: ${save.assist ? 'เปิด' : 'ปิด'}</button></div>
      <div class="small dim" style="line-height:1.6">มือถือ: ลากแถบคันเร่งขึ้นลง (ปล่อยแล้วคืนศูนย์) • ปุ่ม "ค้าง" ต้องกดค้างไว้<br>
      คีย์บอร์ด: W เร่งสุด • X ค่อย ๆ เร่ง • Z ค่อย ๆ เร่งแตะรอบตัด • Space เบรก • S สตาร์ท • K ดับเครื่อง • E/Q เกียร์ • N เกียร์ว่าง • Shift ครัตช์ • D ไดโน • [ ] ความชัน • +/- เสียง</div></div>
  </div><div>
    <div class="panel"><h3>ข้อมูล</h3>
      <div class="field"><button id="clrRec" class="red">ล้างสถิติบนถนน</button><button id="clrAll" class="red">รีเซ็ตทุกอย่าง (เครื่อง+สถิติ)</button></div>
      <div class="small dim" style="line-height:1.6">ข้อมูลเก็บในเบราว์เซอร์เครื่องนี้ (localStorage) ไม่ส่งออกไปที่ไหน<br>โมเดลเป็นการประมาณเพื่อการเรียนรู้ ไม่ใช่ตัวเลขรับรองของเครื่องจริง</div></div>
  </div></div>`;
  $('#volRange').oninput = e => setVolume(+e.target.value);
  $('#assist').onclick = () => { save.assist = !save.assist; persist(); renderSettings(); };
  $('#clrRec').onclick = () => { save.best = { b060: -1, b100: -1, b402: -1, top: 0 }; cur = newRun(); before = null; last060 = last100 = last402 = -1; topSession = 0; persist(); statCache = ''; toast('ล้างสถิติแล้ว'); };
  $('#clrAll').onclick = () => { if (!confirm('รีเซ็ตเครื่องและสถิติทั้งหมด?')) return; try { localStorage.removeItem(KEY); } catch { /* ok */ } location.reload(); };
}

// ================================================================= หน้า
function setView(v) {
  view = v; releaseAll();
  $$('.view').forEach(s => { s.hidden = s.id !== 'v-' + v; });
  $$('#nav button').forEach(b => b.classList.toggle('on', b.dataset.v === v));
  if (v === 'garage') renderGarage();
  else if (v === 'adv') renderAdvisor();
  else if (v === 'set') renderSettings();
  else statCache = '';
}
$$('#nav button').forEach(b => { b.onclick = () => setView(b.dataset.v); });

// ================================================================= เริ่ม
$('#startBtn').onclick = async () => {
  const msg = $('#splashMsg'), btn = $('#startBtn');
  btn.disabled = true; msg.textContent = 'กำลังโหลดเสียงเครื่องยนต์…';
  try {
    await startAudio();
    for (let i = 0; i < 100 && !ready; i++) await new Promise(r => setTimeout(r, 50));
    $('#splash').hidden = true;
  } catch (err) {
    console.error(err);
    msg.innerHTML = 'เปิดเสียงไม่สำเร็จ: ' + (err.message || err) + '<br>ลองใช้ Chrome / Safari เวอร์ชันล่าสุด หรือเล่นต่อแบบไม่มีเสียง';
    btn.disabled = false; btn.textContent = 'เล่นต่อโดยไม่มีเสียง';
    btn.onclick = () => { $('#splash').hidden = true; };
  }
};
const hashView = location.hash.replace('#', '').split(',');
if (hashView.includes('nosound')) $('#splash').hidden = true;
const hv = hashView.find(x => ['garage', 'adv', 'set'].includes(x));

buildTest();
tick();
if (hv) setView(hv);
