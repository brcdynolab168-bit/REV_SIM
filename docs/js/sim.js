// เครื่องยนต์ 1 สูบ 4 จังหวะ: ฟิสิกส์รอบเครื่อง + ระบบขับเคลื่อน + เสียง (พัลส์ไอเสียจริง วางทีละรอบ)
// ใช้ทั้งใน AudioWorklet (เสียงจริง) และ Node (ทดสอบ)
import { EngineModel, Veh, Cvt, clamp, MaxRpm } from './engine.js';

const Block = 32;
const OlaMask = 8191;
const EvoDeg = 130;
const MaxStretch = 3.2, BrightK = 1.5;
const ShiftTime = 0.2, ClutchTorque = 60, SlipScale = 12;
export const DynoBins = Math.floor(MaxRpm / 100) + 1;
const DynoStartRpm = 3000, DynoRate = 2000;

/** พัลส์ไอเสียทีละรอบที่ตัดจากเสียงจริง (wav: Float32Array ที่ sampleRate ของระบบ, marks: ข้อความ .marks ที่สเกลเป็นแซมเปิลของระบบแล้ว) */
export function loadPulses(x, marksText, fs) {
  const segs = [];
  for (const line of marksText.split(/\r?\n/)) {
    if (!line.length || line[0] === '#') continue;
    if (line.startsWith('seg ')) { const p = line.split(' '); segs.push({ id: +p[1], on: p[2] === '1', marks: [] }); }
    else if (segs.length) segs[segs.length - 1].marks.push(+line);
  }
  const P = { grains: [], pre: [], rpm: [], seg: [], on: [], gain: [] };
  for (const { id, on, marks } of segs) {
    if (marks.length < 3) continue;
    let ss = 0;
    for (let i = marks[0]; i < marks[marks.length - 1]; i++) ss += x[i] * x[i];
    const g0 = 0.15 / Math.max(1e-4, Math.sqrt(ss / (marks[marks.length - 1] - marks[0])));
    for (let k = 1; k + 1 < marks.length; k++) {
      const a = marks[k - 1], c = marks[k], b = marks[k + 1], pr = c - a, po = b - c;
      const g = new Float32Array(pr + po);
      for (let j = 0; j < pr; j++) g[j] = x[a + j] * (0.5 - 0.5 * Math.cos(Math.PI * j / pr));
      for (let j = 0; j < po; j++) g[pr + j] = x[c + j] * (0.5 + 0.5 * Math.cos(Math.PI * j / po));
      P.grains.push(g); P.pre.push(pr); P.rpm.push(120 * fs / (0.5 * (pr + po))); P.seg.push(id); P.on.push(on); P.gain.push(g0);
    }
  }
  return P;
}

const IdleBoost = 1.8;      // เสริมเสียงเดินเบา (x2.8 ที่รอบเดินเบา)
export class EngineSim {
  constructor(design, pulses, sampleRate) {
    this.Fs = sampleRate;
    this.P = pulses || { grains: [], pre: [], rpm: [], seg: [], on: [], gain: [] };
    this.model = new EngineModel(design); this.pending = null;
    // อินพุต
    this.throttleInput = 0; this.starterHeld = false; this.ignition = true; this.volume = 0.8;
    this.gearInput = 0; this.clutchInput = 0; this.clutchEnabled = true; this.slopePct = 0; this.brakeInput = 0;
    this._lever = 0; this._cfLock = 0;
    this._roadReset = false; this._dynoRequest = false;
    // สถานะ
    this.rpm = 0; this.thr = 0; this.cut = false; this.softK = 0; this.speed = 0; this.dist = 0; this.gear = 0;
    this.clutchLock = 0; this.cvtS = 0; this.load = 0; this.knock = 0; this.ecuMult = 1; this.ecuAfr = 14.7; this.ecuAdv = 15; this.ecuDuty = 0; this.ecuEgt = 0; this.ecuMbt = 15; this.ecuPw = 0; this.ecuEoi = 0; this.ecuDwell = 0; this.ecuSpark = 1; this.ecuMisfire = 0; this.thrEff = 0;
    this.dynoPhase = 0; this.dynoVersion = 0; this.dynoFailed = false; this.dynoTq = new Float32Array(DynoBins).fill(NaN);
    this._dynoSum = new Float64Array(DynoBins); this._dynoCnt = new Int32Array(DynoBins); this._dynoRpm = 0;
    this._blockPos = 0; this._idleInt = 0; this._huntPh = 0; this._huntRate = 0; this._idleWobble = 0;
    this._fuelOn = false; this._starterActive = false;
    this._phase = 0; this._rpmAudio = 0; this._cycleFiring = false; this._cycleVar = 1; this._cycAmp = 0; this._cycKnock = 0;
    this._rng = 0x1234ABCD; this._wantOn = false; this._cycCut = false;
    this._ola = new Float32Array(OlaMask + 1); this._olaR = 0; this._olaLead = 3200; this._dcX = 0; this._dcY = 0;
    this._cursor = 0; this._runLeft = 0; this._recent = new Int32Array(40).fill(-100); this._recentPos = 0; this._cand = new Int32Array(1024);
    this.scope = new Float32Array(2048); this.scopeW = 0; this.slipRpm = 0; this.tracUse = 0; this.spin = false; this.driveForce = 0; this.gripMax = 0; this._gearApplied = 0; this._shiftT = 0; this._tcSmooth = 0;
  }

  set surface(i) { Veh.setSurface(i); }
  assemble(design) { this.pending = new EngineModel(design); this._roadReset = true; }
  startDyno() { if (this.running && this.dynoPhase === 0) this._dynoRequest = true; }
  get running() { return this.ignition && this.rpm > 700; }
  get isAuto() { return this.model.D.Auto === 1; }

  _noise() { let r = this._rng; r ^= r << 13; r ^= r >>> 17; r ^= r << 5; this._rng = r | 0; return (r | 0) / 2147483648; }
  _unit() { return this._noise() * 0.5 + 0.5; }

  _stepPhysics(dt) {
    if (this.pending) { this.model = this.pending; this.pending = null; }
    const m = this.model;
    if (this._roadReset) { this._roadReset = false; this.speed = 0; this.dist = 0; this.cvtS = 0; this._tcSmooth = 0; this.dynoPhase = 0; }
    if (this._dynoRequest) {
      this._dynoRequest = false; this._dynoSum.fill(0); this._dynoCnt.fill(0); this.dynoTq.fill(NaN); this.dynoFailed = false; this.dynoPhase = 1;
    }
    let rpm = this.rpm;
    const ign = this.ignition;
    if (this.dynoPhase !== 0 && !ign) { this.dynoPhase = 0; this.dynoFailed = true; this.dynoVersion++; }

    let target = clamp(this.throttleInput, 0, 1);
    if (this.dynoPhase === 1) target = rpm < DynoStartRpm - 400 ? 0.35 : 0.08;
    if (this.dynoPhase === 2) target = 1;
    let thr = this.thr;
    thr += (target - thr) * Math.min(1, (target > thr ? 12 : 20) * dt);
    this.thr = thr;

    let idleThr = 0;
    if (ign) {
      const wobAmp = (25 + 420 * m.Lope + 600 * m.LowCrMisfire) / (m.IdleSteady * m.IdleSteady);
      this._idleWobble += (this._noise() * wobAmp * 2.2 - this._idleWobble) * 0.0015;
      let huntOff = 0;
      if (m.Hunt > 0) {
        this._huntRate += (this._noise() * 0.5 - this._huntRate) * 0.002;
        this._huntPh += dt * 6.2832 * (1.3 + 0.4 * this._huntRate);
        huntOff = m.Hunt * 210 * Math.sin(this._huntPh) + m.Hunt * 70 * Math.sin(this._huntPh * 2.7);
      }
      const err = m.IdleRpm + this._idleWobble + huntOff - rpm;
      this._idleInt = clamp(this._idleInt + err * dt * 0.00008, 0, 0.06);
      idleThr = clamp(0.022 + err * 0.00006 + this._idleInt, 0, 0.10) * (24500 / m.AirK);
    }
    const eff = idleThr + thr * (1 - idleThr);
    this.thrEff = eff;
    this.load = m.Load(eff, rpm);
    const ecu = m.EcuAt(rpm, eff);
    this.ecuMult = ecu.Mult; this.ecuAfr = ecu.Afr; this.ecuAdv = ecu.Adv; this.ecuDuty = ecu.Duty; this.ecuEgt = ecu.Egt; this.ecuMbt = ecu.Mbt; this.ecuPw = ecu.PulseMs; this.ecuEoi = ecu.Eoi; this.ecuDwell = ecu.DwellMs; this.ecuSpark = ecu.Spark; this.ecuMisfire = ecu.Misfire;
    this.knock = m.Knock(rpm, this.load, ecu.KnockShift);

    const D = m.D;
    if (rpm > D.Limiter) this.cut = true;
    else if (rpm < D.Limiter - D.ResumeGap) this.cut = false;
    this.softK = D.SoftGap > 0 ? clamp((rpm - (D.Limiter - D.SoftGap)) / D.SoftGap, 0, 1) : 0;
    this._fuelOn = ign && !this.cut && rpm > 220;
    this._starterActive = this.starterHeld && rpm < 1000;

    let drive = 0;
    if (this._fuelOn && this._cycleFiring) drive = m.TorqueInd(rpm) * this.load * this.ecuMult * this._cycleVar * (1 - 0.5 * this._cycKnock) * (1 - 0.4 * this.softK);
    let torque = drive;
    if (this._starterActive) torque += 5.5 * Math.max(0, 1 - rpm / 650);
    let friction = m.Friction(rpm) + (1 - this.load) * (0.30 + 0.00018 * rpm) * m.FrictionScale;
    if (rpm <= 0 && torque < friction) friction = torque;

    if (this.dynoPhase === 1 && rpm >= DynoStartRpm) { this.dynoPhase = 2; this._dynoRpm = DynoStartRpm; }
    if (this.dynoPhase === 2) {
      this._dynoRpm += DynoRate * dt;
      const bin = Math.floor(this._dynoRpm / 100);
      if (bin >= 0 && bin < DynoBins) { this._dynoSum[bin] += drive - friction; this._dynoCnt[bin]++; }
      rpm = this._dynoRpm;
      if (this._dynoRpm >= D.Limiter - 120) {
        for (let i = 0; i < DynoBins; i++) this.dynoTq[i] = this._dynoCnt[i] > 0 ? this._dynoSum[i] / this._dynoCnt[i] : NaN;
        this.dynoPhase = 0; this.dynoVersion++;
      }
    } else rpm = this._stepDrivetrain(m, rpm, torque - friction, dt);
    if (rpm < 0) rpm = 0;
    this.rpm = rpm;
  }

  _stepDrivetrain(m, rpm, netEngine, dt) {
    const d = m.D, auto = d.Auto === 1, g = this.gearInput;
    if (!auto) {
      const want = this.clutchEnabled ? clamp(this.clutchInput, 0, 1) : 0;
      this._lever += (want - this._lever) * Math.min(1, dt * (want > this._lever ? 30 : 7));
      if (g !== this._gearApplied) { this._gearApplied = g; this.gear = g; this._shiftT = ShiftTime; }
      if (this._shiftT > 0) this._shiftT -= dt;
    } else { this._lever = 0; this.gear = 0; this._shiftT = 0; }

    let v = this.speed;
    const mass = Veh.mass(d), mEff = Veh.effMass(d), r = Veh.wheelRadius(d);
    const ratio = auto ? Cvt.ratio(this.cvtS) * Cvt.final(d) : Veh.total(d, g);
    const omegaE = rpm / 9.5493;
    let tc = 0, lock01 = 0, slipRpm = 0, tracUse = 0, spin = false;
    if (ratio > 0 && this._shiftT <= 0) {
      const start = auto ? 3200 : Math.max(2600, m.IdleRpm + 900), full = auto ? 7500 : 7000;
      lock01 = clamp((rpm * rpm - start * start) / (full * full - start * start), 0, 1);
      this._cfLock = lock01;
      lock01 *= 1 - this._lever * this._lever * (3 - 2 * this._lever);
      const omegaIn = v / r * ratio, slip = omegaE - omegaIn;
      tc = clamp(slip / SlipScale, -1, 1) * ClutchTorque * lock01;
      const tcMax = Veh.tractionMax(d) * r / (ratio * Veh.DriveEff);
      slipRpm = slip * 9.5493; tracUse = tcMax > 0 ? Math.abs(tc) / tcMax : 0; spin = Math.abs(tc) > tcMax;
      tc = clamp(tc, -tcMax, tcMax);
    }
    this.clutchLock = lock01; this.slipRpm = slipRpm; this.tracUse = tracUse; this.spin = spin; this.gripMax = Veh.tractionMax(d);
    if (auto) {
      this._tcSmooth += (Math.max(tc, 0) - this._tcSmooth) * 0.003;
      const ff = Cvt.ff(d, omegaE, this.cvtS), fr = Cvt.fr(d, this._tcSmooth, this.cvtS);
      let ds = clamp((ff - fr) / Cvt.FScale, -1, 1);
      if (ds > 0) ds *= clamp(1 - Math.abs(slipRpm) / 800, 0, 1) * clamp(lock01 * 4, 0, 1);   // สายพานไม่ไล่ขึ้นตอนคลัตช์ยังลื่น/ไม่จับ (ไม่มีโหลด สายพานหลวม) กันอัตราทดแกว่งขึ้นแล้วตกตอนออกตัว
      this.cvtS = clamp(this.cvtS + ds * Cvt.Rate * dt, 0, 1);
    }
    const inertia = m.Inertia * (1 - 0.65 * lock01);
    const newOmega = omegaE + (netEngine - tc) / inertia * dt;
    rpm = newOmega * 9.5493;

    const fDrive = ratio > 0 ? tc * ratio * Veh.DriveEff / r : 0;
    this.driveForce = Math.max(0, fDrive);
    const fRes = Veh.resist(d, v, this.slopePct);
    const fBrake = clamp(this.brakeInput, 0, 1) * Veh.BrakeDecel * mass;
    if (v < 0.02) {
      const net0 = fDrive - fRes;
      if (net0 <= 0 || (fBrake > 0 && net0 < fBrake)) v = 0; else v += net0 / mEff * dt;
    } else {
      v += (fDrive - fRes - fBrake) / mEff * dt;
      if (v < 0) v = 0;
    }
    this.speed = v; this.dist += v * dt;
    return rpm;
  }

  // ---------------------------------------------------------------- เสียง
  _newCycle() {
    const m = this.model, rpm = this.rpm, load = this.load;
    let fire = this._fuelOn;
    const low = clamp(1 - (rpm - m.IdleRpm) / 2500, 0, 1) * (1 - load * 0.6);
    if (fire && this._unit() < m.Lope * 0.30 * low) fire = false;
    if (fire && this._unit() < m.LowCrMisfire * (0.4 + low)) fire = false;
    if (fire && this._unit() < (this.ecuMisfire || 0)) fire = false;      // ตารางจูนกล่อง: บาง/หนาเกิน ประกายอ่อน ตัดน้ำมัน -> จุดไม่ติดเป็นจังหวะ ๆ
    if (fire && this.softK > 0 && this._unit() < this.softK * 0.6) fire = false;
    if (fire && rpm > m.FloatRpm && this._unit() < 0.45) fire = false;
    this._cycleFiring = fire;
    const gg = this._noise() + this._noise() + this._noise();
    const jitter = 0.03 + 0.12 * (1 - load) + 0.22 * m.Lope * low + 0.30 * this.knock;
    this._cycleVar = clamp(1 + jitter * gg, 0.5, 1.5);
    this._cycKnock = fire ? this.knock * (0.5 + this._unit()) : 0;
    let cond = m.CrAmp * (0.55 + 0.45 * m.VeNorm(rpm));
    if (m.Hunt > 0) cond *= 1 - 0.20 * m.Hunt * Math.max(0, Math.sin(this._huntPh)) * clamp(1 - (rpm - m.IdleRpm) / 1500, 0, 1);
    this._cycAmp = fire ? Math.pow(0.12 + 0.88 * load, 0.8) * this._cycleVar * cond : 0;
    this._wantOn = fire && load > 0.3;
    this._cycCut = this.cut;
  }

  _usedRecently(idx) { for (let i = 0; i < this._recent.length; i++) if (Math.abs(this._recent[i] - idx) < 8) return true; return false; }
  _collect(rpm, on, tol, skip) {
    const P = this.P; let n = 0;
    for (let k = 0; k < P.grains.length && n < this._cand.length; k++) {
      if (P.on[k] !== on) continue;
      if (Math.abs(P.rpm[k] - rpm) > tol * rpm) continue;
      if (skip && this._usedRecently(k)) continue;
      this._cand[n++] = k;
    }
    return n;
  }
  _nextPulse(rpm, on) {
    const P = this.P, cnt = P.grains.length;
    if (cnt === 0) return 0;
    rpm = Math.max(rpm, 300);
    const nx = this._cursor + 1;
    if (this._runLeft > 0 && nx < cnt && P.seg[nx] === P.seg[this._cursor] && P.on[nx] === on && Math.abs(P.rpm[nx] - P.rpm[this._cursor]) < 0.12 * P.rpm[this._cursor]) {
      this._cursor = nx; this._runLeft--;
    } else {
      let n = this._collect(rpm, on, 0.08, true);
      if (n < 10) n = this._collect(rpm, on, 0.16, true);
      if (n < 10) n = this._collect(rpm, on, 0.30, true);
      if (n < 10) n = this._collect(rpm, on, 0.70, true);
      if (n < 10) n = this._collect(rpm, on, 3.00, true);
      if (n < 3) n = this._collect(rpm, on, 3.00, false);
      if (n === 0) {
        let bestD = Infinity, bi = 0;
        for (let k = 0; k < cnt; k++) { const dd = Math.abs(P.rpm[k] - rpm) + (P.on[k] === on ? 0 : 3000); if (dd < bestD && !this._usedRecently(k)) { bestD = dd; bi = k; } }
        this._cand[0] = bi; n = 1;
      }
      this._cursor = this._cand[Math.floor(this._unit() * n) % n];
      this._runLeft = 3 + Math.floor(this._unit() * 8);
    }
    this._recent[this._recentPos] = this._cursor;
    this._recentPos = (this._recentPos + 1) % this._recent.length;
    return this._cursor;
  }

  _emitGrain(index, amp, rpm) {
    const P = this.P;
    if (!P.grains.length) return;
    const p = P.grains[index];
    amp *= P.gain[index];
    const s = clamp(P.rpm[index] / Math.max(rpm, 400) * this.model.PitchStretch, 0.6, MaxStretch);
    const outLen = Math.floor(p.length * s);
    const start = this._olaR + this._olaLead - Math.floor(P.pre[index] * s);
    const inv = 1 / s;
    const boost = this._cycCut ? 0 : clamp(BrightK * (s - 1) + this.model.SoundBright * (0.35 + 0.65 * this.load) + 1.4 * this._cycKnock, -0.85, 2.8);
    const hpA = 1 - Math.exp(-2 * Math.PI * 800 / this.Fs);
    let z = 0;
    for (let j = 0; j < outLen; j++) {
      const x = j * inv, i0 = Math.floor(x);
      if (i0 >= p.length - 1) break;
      const f = x - i0;
      let v = p[i0] + (p[i0 + 1] - p[i0]) * f;
      if (boost !== 0) { z += hpA * (v - z); v += boost * (v - z); }
      this._ola[(start + j) & OlaMask] += v * amp;
    }
  }

  /** สร้างเสียง count แซมเปิล ลง out (Float32Array) */
  render(out, count) {
    const vol = clamp(this.volume, 0, 1), Fs = this.Fs, blockDt = Block / Fs;
    for (let i = 0; i < count; i++) {
      if (this._blockPos === 0) this._stepPhysics(blockDt);
      this._blockPos++; if (this._blockPos >= Block) this._blockPos = 0;
      this._rpmAudio += (this.rpm - this._rpmAudio) * 0.02;
      const prev = this._phase;
      let ph = prev + this._rpmAudio * (6 / Fs);
      if (prev < EvoDeg && ph >= EvoDeg && this._rpmAudio > 150) {
        let amp;
        if (this._cycCut) amp = 0.55;
        else if (this._wantOn) amp = this._cycAmp;
        else amp = 0.75 * (0.35 + 0.65 * clamp((this._rpmAudio - 2500) / 5000, 0, 1)) * this._cycleVar;
        // เสียงเดินเบาเบาเกินไป (พัลส์โหลดต่ำ) : เสริมระดับช่วงรอบเดินเบาให้ได้ยินชัด ค่อย ๆ ลดจนไม่มีผลที่ ~รอบเดินเบา+2,500
        if (!this._cycCut) amp *= 1 + IdleBoost * clamp(1 - (this._rpmAudio - this.model.IdleRpm) / 2500, 0, 1);
        this._emitGrain(this._nextPulse(this._rpmAudio, this._wantOn), amp, this._rpmAudio);
      }
      if (ph >= 720) { ph -= 720; this._newCycle(); }
      this._phase = ph;
      const e = this._ola[this._olaR]; this._ola[this._olaR] = 0; this._olaR = (this._olaR + 1) & OlaMask;
      const o = e * this.model.Loudness * 0.95 * vol;
      const dc = o - this._dcX + 0.9975 * this._dcY;
      this._dcX = o; this._dcY = dc;
      const os = Math.tanh(dc * 1.2);
      out[i] = os;
      this.scope[this.scopeW] = os; this.scopeW = (this.scopeW + 1) & 2047;
    }
  }
}
