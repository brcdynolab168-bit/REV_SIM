// ผู้ช่วยจูน: จำลองวิ่ง + ลองปรับทีละค่า + วิเคราะห์ผลทดสอบจริง (พอร์ตจากเวอร์ชัน C#)
import { EngineModel, Veh, Cvt, Params, paramDisplay, clamp, PsToHp, gearAt } from './engine.js';

export const GoalNames = ['402 ม. (เร่งไว)', 'ท็อปสปีด', '0–60 (ออกตัว)'];
export const Groups = [
  ['สเตอร์/เกียร์', ['sprr', 'sprf', 'g1', 'gtop']],
  ['เฟืองท้าย/CVT', ['fdr', 'fdf', 'ramp', 'rampout', 'rollerg', 'spring']],
  ['เรือนลิ้น', ['throttle']],
  ['แคม', ['lsa', 'duration', 'durationex', 'lift', 'liftex']],
  ['ท่อ', ['pipe']],
  ['CR', ['cr']],
  ['ข้อเหวี่ยง/จานไฟ', ['crank', 'fly']],
  ['ล้อ/ยาง', ['tireasp', 'rim']],
  ['รอบตัด', ['limiter']],
];

export function predict(m) {
  const d = m.D, auto = d.Auto === 1;
  const r = Veh.wheelRadius(d), mEff = Veh.effMass(d), fmax = Veh.tractionMax(d);
  const lim = d.Limiter - 100;
  const hold = auto ? Cvt.holdRpm(m) : 0;
  const launch = auto ? Math.max(3500, hold * 0.75) : clamp(m.PeakTqRpm * 1.1, 3000, lim);
  const shiftRpm = Math.min(lim, m.PeakPsRpm * 1.07);
  const fin = auto ? Cvt.final(d) : 0;
  let gear = 1, v = 0, x = 0, t = 0, shift = 0, t060 = -1, t100 = -1, t402 = -1, trap = 0;
  const dt = 0.01;
  while (t < 40 && t402 < 0) {
    let rpm, ratio, wr = 0;
    if (auto) {
      const k = v / r * 9.5493 * fin;
      rpm = clamp(hold, k * Cvt.RHigh, k * Cvt.RLow);
      rpm = Math.min(Math.max(rpm, launch), lim);
      ratio = clamp(rpm / 9.5493 * r / Math.max(v, 0.1), Cvt.RHigh * fin, Cvt.RLow * fin);
    } else {
      ratio = Veh.total(d, gear);
      wr = v / r * ratio * 9.5493;
      rpm = Math.max(wr, gear === 1 ? launch : 2500);
      if (rpm >= shiftRpm && gear < d.Gears && shift <= 0) { gear++; shift = 0.2; }
      rpm = Math.min(rpm, lim);
    }
    const tq = Math.max(0, m.BrakeTorqueWot(rpm));
    let f = shift > 0 ? 0 : Math.min(tq * ratio * Veh.eff(d) / r, fmax);
    if (!auto && wr > lim) f = 0;
    v = Math.max(0, v + (f - Veh.resist(d, v, 0)) / mEff * dt);
    x += v * dt; t += dt; shift -= dt;
    const kmh = v * 3.6;
    if (t060 < 0 && kmh >= 60) t060 = t;
    if (t100 < 0 && x >= 100) t100 = t;
    if (x >= 402.3) { t402 = t; trap = kmh; }
  }
  const top = auto ? Veh.topSpeedAuto(m) : Veh.topSpeed(m, m.D.Gears);
  return { t060: t060 < 0 ? 99 : t060, t100: t100 < 0 ? 99 : t100, t402: t402 < 0 ? 99 : t402, trap, top: top.kmh, topRpm: top.rpm };
}

const knocks = m => m.Knock(3500, m.Load(1, 3500)) > 0.02;
const TWEAKS = [
  ['sprr', [-2, -1, 1, 2], 0, 1], ['sprf', [-1, 1], 0, 1], ['gtop', [-0.06, 0.06], 0, 1], ['g1', [-0.2, 0.2], 0, 1],
  ['fdr', [-2, -1, 1, 2], 1, 0], ['fdf', [-1, 1], 1, 0], ['ramp', [-1, -0.5, 0.5, 1], 1, 0], ['rampout', [-2, -1, 1, 2], 1, 0], ['rollerg', [-2, -1, 1, 2], 1, 0], ['spring', [-4, -2, 2, 4], 1, 0],
  ['throttle', [2, 4, -2], 1, 1], ['lsa', [-4, -2, 2, 4], 1, 1], ['duration', [-10, -6, 6, 10], 1, 1], ['durationex', [-10, -6, 6, 10], 1, 1],
  ['lift', [-0.5, 0.5, 1], 1, 1], ['liftex', [-0.5, 0.5, 1], 1, 1], ['cr', [-0.5, 0.5, 1], 1, 1], ['pipe', [1, 2, 3, -1], 1, 1],
  ['limiter', [-500, 500, 1000], 1, 1], ['crank', [-0.4, -0.8, 0.4], 1, 1], ['fly', [-0.2, -0.4, 0.2], 1, 1],
  ['tireasp', [-5, 5, 10], 1, 1], ['rim', [-1, 1], 1, 1],
];
const metric = (p, goal) => (goal === 1 ? -p.top : goal === 2 ? p.t060 : p.t402);
export const newConstraints = () => ({ rpmCap: 0, lockMask: 0, street: false });
const locked = (c, i) => (c.lockMask & (1 << i)) !== 0;

export function suggest(d0, goal, max = 5, c = newConstraints()) {
  const d = { ...d0 };
  const best = [];
  const limIdx = Groups.findIndex(g => g[0] === 'รอบตัด');
  if (c.rpmCap > 0 && !locked(c, limIdx)) {
    const lim = Params.find(p => p.key === 'limiter');
    const cap = clamp(Math.round(c.rpmCap / lim.step) * lim.step, lim.min, lim.max);
    if (Math.abs(d.Limiter - cap) > 0.5) {
      const dd = { ...d, Limiter: cap };
      best.push({ label: `ตั้งรอบตัดตามเป้าหมาย:  ${paramDisplay(lim, d0)}  →  ${paramDisplay(lim, dd)}`, design: dd,
        before: predict(new EngineModel(d0)), after: predict(new EngineModel(dd)), gain: 999, note: 'ตั้งตามโจทย์ (ฐานของข้อแนะนำด้านล่าง)' });
      Object.assign(d, dd);
    }
  }
  const m0 = new EngineModel(d), p0 = predict(m0);
  const knock0 = knocks(m0), float0 = d.Limiter >= m0.FloatRpm, auto = d.Auto === 1;
  for (const [key, deltas, forAuto, forManual] of TWEAKS) {
    if (auto ? !forAuto : !forManual) continue;
    const gi = Groups.findIndex(g => g[1].includes(key));
    if (gi >= 0 && locked(c, gi)) continue;
    if (key === 'limiter' && c.rpmCap > 0) continue;
    const def = Params.find(p => p.key === key);
    let top = null;
    for (const delta of deltas) {
      const d1 = { ...d };
      const cur = def.get(d);
      let nv = Math.round((cur + delta) / def.step) * def.step;
      nv = clamp(nv, def.min, def.max);
      if (Math.abs(nv - cur) < def.step * 0.5) continue;
      def.set(d1, nv);
      if (key === 'rollerg' && Math.abs(d.RollerG - d.RollerG2) < 0.01) d1.RollerG2 = d1.RollerG;
      const m1 = new EngineModel(d1);
      if (!knock0 && knocks(m1)) continue;
      if (!float0 && d1.Limiter >= m1.FloatRpm) continue;
      if (c.street && ((m1.Overlap > 40 && m0.Overlap <= 40) || (m1.Hunt > 0.25 && m0.Hunt <= 0.25))) continue;
      const p1 = predict(m1);
      const gain = metric(p0, goal) - metric(p1, goal);
      if (gain < (goal === 1 ? 0.3 : 0.01)) continue;
      if (top && gain <= top.gain) continue;
      const notes = [];
      if (m1.Overlap > 40 && m0.Overlap <= 40) notes.push('เดินเบาสะดุดขึ้น');
      if (m1.Hunt > 0.25 && m0.Hunt <= 0.25) notes.push('เดินหอบ');
      if (m1.Psi > m0.Psi + 15) notes.push(`แรงดันอัดเพิ่มเป็น ${m1.Psi.toFixed(0)} psi`);
      if (m1.PeakPs < m0.PeakPs * 0.97) notes.push(`แรงม้าสูงสุดลด ${m0.PeakHp.toFixed(1)}→${m1.PeakHp.toFixed(1)} HP`);
      if (m1.PeakPs > m0.PeakPs * 1.02) notes.push(`แรงม้าสูงสุด ${m0.PeakHp.toFixed(1)}→${m1.PeakHp.toFixed(1)} HP`);
      top = { label: `${def.name}:  ${paramDisplay(def, d)}  →  ${paramDisplay(def, d1)}`, design: d1, before: p0, after: p1, gain, note: notes.join('  •  ') };
    }
    if (top) best.push(top);
  }
  best.sort((a, b) => b.gain - a.gain);
  return best.slice(0, max);
}

export function capTips(d, c) {
  const tips = [];
  if (c.rpmCap <= 0) return tips;
  const m = new EngineModel(d), p = predict(m), cap = d.Limiter;
  if (m.PeakPsRpm > cap * 1.02) tips.push([2, `โจทย์รอบ ${cap.toLocaleString()}: เครื่องนี้พีคกำลังที่ ${m.PeakPsRpm.toLocaleString()} สูงกว่ารอบที่ใช้ ใช้กำลังไม่เต็ม  → ลดองศา/ยกแคม หรือใช้ท่อเดิม/ผ่าหมก ให้พีคลงมาใกล้ ${cap.toLocaleString()}`]);
  else if (m.PeakPsRpm < cap - 1500) tips.push([2, `โจทย์รอบ ${cap.toLocaleString()}: กำลังพีคที่ ${m.PeakPsRpm.toLocaleString()} ต่ำกว่ารอบที่ใช้มาก  → เพิ่มองศา/ยกแคม เรือนใหญ่ขึ้น หรือท่อสูตรให้พีคขยับขึ้นไปหา ${cap.toLocaleString()}`]);
  else tips.push([1, `โจทย์รอบ ${cap.toLocaleString()}: พีคกำลัง ${m.PeakHp.toFixed(1)} HP @ ${m.PeakPsRpm.toLocaleString()} สัมพันธ์กับรอบที่ใช้ดี`]);
  if (p.topRpm >= cap - 150) tips.push([2, `ท็อปสปีด ${p.top.toFixed(0)} กม./ชม. ชนรอบ ${cap.toLocaleString()} ก่อนความเร็วจบ  → ทดท้ายให้ยาวขึ้นเล็กน้อยจะได้ท็อปมากกว่านี้`]);
  else tips.push([0, `ท็อปสปีดประมาณ ${p.top.toFixed(0)} กม./ชม. @ ${p.topRpm.toFixed(0)} rpm (ใช้รอบไม่ถึง ${cap.toLocaleString()})`]);
  return tips;
}

/** t: {peakRpm, top, last060, last100, last402, dyno:{ps,psRpm,tq,tqRpm}|null, before, cur} */
export function observe(m, t) {
  const d = m.D, tips = [], pred = predict(m), lim = d.Limiter;
  const any = t.peakRpm > 0 || t.top > 0 || t.last402 > 0 || t.dyno;
  if (!any) tips.push([0, 'ยังไม่มีผลทดสอบ: ลองวิ่ง 0–60, 100 ม., 402 ม. และวัดไดโนที่หน้าทดสอบ แล้วกลับมาดูคำแนะนำจากผลจริง']);
  if (t.last402 > 0) {
    if (t.last402 - pred.t402 > pred.t402 * 0.06) tips.push([2, `402 ม. วัดได้ ${t.last402.toFixed(2)} วิ ช้ากว่าที่เครื่องนี้ทำได้ ~${pred.t402.toFixed(2)} วิ  → ออกตัว/เปลี่ยนเกียร์ยังไม่ดี (ปล่อยคลัตช์ที่รอบแรงบิดสูงสุด ~${m.PeakTqRpm.toLocaleString()}, ไล่เกียร์ที่ ~${Math.round(m.PeakPsRpm * 1.05).toLocaleString()})`]);
    else tips.push([1, `402 ม. วัดได้ ${t.last402.toFixed(2)} วิ ใกล้ค่าที่เครื่องนี้ทำได้ ~${pred.t402.toFixed(2)} วิ แล้ว ต่อไปต้องแก้ที่เครื่อง/อัตราทด`]);
  }
  if (t.top > 0 && t.top - pred.top < -pred.top * 0.05) tips.push([2, `ท็อปสปีดที่ทำได้ ${t.top.toFixed(0)} กม./ชม. ต่ำกว่าศักยภาพ ~${pred.top.toFixed(0)} (@ ${pred.topRpm.toFixed(0)} rpm)  → ลองลากให้นานขึ้น/ทางราบ หรือรอบถึงรอบตัดก่อน`]);
  if (t.peakRpm >= lim - 100) tips.push([2, `รอบชนรอบตัด (${Math.round(t.peakRpm).toLocaleString()}): ปลายสั้น/ล็อกรอบก่อนความเร็วสุด  → ทดท้ายให้ยาวขึ้น หรือเพิ่มรอบตัดถ้าวาล์วไม่ลอย (${Math.round(m.FloatRpm).toLocaleString()})`]);
  else if (t.peakRpm > 0 && t.top > 0 && t.peakRpm < m.PeakPsRpm * 0.9) tips.push([2, `รอบสูงสุดที่ใช้ ${Math.round(t.peakRpm).toLocaleString()} ยังไม่ถึงพีคกำลัง (${m.PeakPsRpm.toLocaleString()})  → ทดจัดขึ้นเพื่อให้ได้รอบพีคก่อนจบ`]);
  if (t.dyno) {
    const dy = t.dyno;
    if (dy.psRpm >= lim - 400) tips.push([2, `ไดโน: กำลังยังขึ้นไปจนถึงรอบตัด (${(dy.ps * PsToHp).toFixed(1)} HP @ ${dy.psRpm.toLocaleString()})  → รอบตัดต่ำไปสำหรับเครื่องนี้ เพิ่มรอบตัดหรือทดท้ายยาวขึ้น`]);
    else if (dy.psRpm < lim - 2500) tips.push([2, `ไดโน: กำลังตกหลัง ${dy.psRpm.toLocaleString()} ทั้งที่รอบตัด ${lim.toLocaleString()}  → ลดรอบตัด/จูนปลาย (เรือน, แคม EX, ยก, ท่อ)`]);
    else tips.push([1, `ไดโน: พีค ${(dy.ps * PsToHp).toFixed(1)} HP @ ${dy.psRpm.toLocaleString()} สัมพันธ์กับรอบตัด ${lim.toLocaleString()} พอดี`]);
    if (dy.tqRpm > dy.psRpm * 0.85) tips.push([0, `แรงบิดพีค ${dy.tq.toFixed(1)} N·m @ ${dy.tqRpm.toLocaleString()} ใกล้พีคกำลัง: ช่วงกำลังแคบ ต้องไล่เกียร์/CVT ให้อยู่รอบสูงตลอด`]);
  }
  if (d.Auto === 0) {
    const sr = Math.min(lim - 100, m.PeakPsRpm * 1.07), g = Array.from({ length: d.Gears }, (_, i) => gearAt(d, i + 1));
    for (let i = 0; i < d.Gears - 1; i++) {
      const after = sr * g[i + 1] / g[i];
      if (after < m.PeakTqRpm * 0.85) tips.push([2, `เปลี่ยนเกียร์ ${i + 1}→${i + 2} รอบตกเหลือ ~${Math.round(after).toLocaleString()} ต่ำกว่าพีคแรงบิด (${m.PeakTqRpm.toLocaleString()}) ห่างเกียร์ไป  → ปรับเกียร์ ${i + 2} ให้ทดจัดขึ้น`]);
    }
  } else {
    const hold = Cvt.holdRpm(m);
    if (hold < m.PeakPsRpm * 0.93) tips.push([2, `CVT ถือรอบ ${hold.toLocaleString()} ต่ำกว่าพีคกำลัง (${m.PeakPsRpm.toLocaleString()})  → เม็ดเบาลง/สปริงแข็งขึ้น/องศาชามมากขึ้น (ปุ่ม "จูนเม็ด" ในอู่)`]);
    else if (hold > m.PeakPsRpm * 1.08) tips.push([2, `CVT ถือรอบ ${hold.toLocaleString()} เกินพีคกำลัง (${m.PeakPsRpm.toLocaleString()})  → เม็ดหนักขึ้น/สปริงนิ่มลง`]);
    else tips.push([1, `CVT ถือรอบ ${hold.toLocaleString()} ใกล้พีคกำลัง (${m.PeakPsRpm.toLocaleString()}) ดีแล้ว`]);
  }
  const launchRpm = clamp(m.PeakTqRpm * 1.1, 3000, lim);
  const ratio1 = d.Auto === 1 ? Cvt.RLow * Cvt.final(d) : Veh.total(d, 1);
  const fL = Math.max(0, m.BrakeTorqueWot(launchRpm)) * ratio1 * Veh.eff(d) / Veh.wheelRadius(d);
  if (fL > Veh.tractionMax(d) * 1.05) tips.push([2, `ออกตัวล้อฟรี: แรงขับ ${fL.toFixed(0)} N เกินที่ยางเกาะได้ ${Veh.tractionMax(d).toFixed(0)} N  → หน้ายางกว้างขึ้น/ขอบให้พอดี หรือทดเกียร์ 1 ให้ยาวขึ้น`]);
  if (knocks(m)) tips.push([2, `น็อก! แรงดันอัด ${m.Psi.toFixed(0)} psi เกินพิกัดน้ำมัน (${m.CrLimit.toFixed(1)}) ลด CR/แรงดันอัด หรือเปลี่ยนน้ำมันออกเทนสูง`]);
  if (lim >= m.FloatRpm) tips.push([2, `รอบตัด ${lim.toLocaleString()} สูงเกินรอบวาล์วลอย (${Math.round(m.FloatRpm).toLocaleString()}) ลดรอบตัด/ลดยก/เพิ่ม 4 วาล์ว`]);
  if (m.RpmFlow < m.PeakPsRpm) tips.push([2, `ไอดีเริ่มอั้นที่ ${Math.round(m.RpmFlow).toLocaleString()} ต่ำกว่ารอบพีค: เพิ่มขนาดเรือน/ระยะยก/จำนวนวาล์ว`]);
  if (t.before && t.cur) {
    const b = t.before, c = t.cur, parts = [];
    if (b.b402 > 0 && c.b402 > 0) parts.push(`402 ม. ${b.b402.toFixed(2)}→${c.b402.toFixed(2)} วิ`);
    if (b.b060 > 0 && c.b060 > 0) parts.push(`0–60 ${b.b060.toFixed(2)}→${c.b060.toFixed(2)} วิ`);
    if (b.top > 0 && c.top > 0) parts.push(`ท็อป ${b.top.toFixed(0)}→${c.top.toFixed(0)} กม./ชม.`);
    if (b.hp > 0 && c.hp > 0) parts.push(`${b.hp.toFixed(1)}→${c.hp.toFixed(1)} HP`);
    if (parts.length) tips.push([0, 'ผลเทียบก่อน→หลังแก้: ' + parts.join('  •  ')]);
  }
  return tips;
}
