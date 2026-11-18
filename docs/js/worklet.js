// AudioWorklet: รันฟิสิกส์เครื่องยนต์ + สร้างเสียงในเธรดเสียง ส่งสถานะกลับ UI ~30 ครั้ง/วินาที
import { EngineSim, loadPulses } from './sim.js';

class RevProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.sim = null;
    this.acc = 0;
    this.lastVer = 0;
    this.port.onmessage = e => {
      const m = e.data;
      if (m.type === 'init') {
        const pulses = loadPulses(m.wav, m.marks, sampleRate);
        this.sim = new EngineSim(m.design, pulses, sampleRate);
        this.sim.volume = m.volume ?? 0.8;
        this.port.postMessage({ type: 'ready', grains: pulses.grains.length });
      } else if (!this.sim) return;
      else if (m.type === 'set') this.sim[m.key] = m.value;
      else if (m.type === 'assemble') this.sim.assemble(m.design);
      else if (m.type === 'dyno') this.sim.startDyno();
    };
  }

  process(inputs, outputs) {
    const out = outputs[0][0];
    if (!this.sim) { out.fill(0); return true; }
    const sim = this.sim;
    sim.render(out, out.length);
    for (let c = 1; c < outputs[0].length; c++) outputs[0][c].set(out);
    this.acc += out.length;
    if (this.acc >= sampleRate / 30) {
      this.acc = 0;
      const msg = {
        type: 'state', rpm: sim.rpm, thr: sim.thr, cut: sim.cut, softK: sim.softK, speed: sim.speed, dist: sim.dist, gear: sim.gear,
        clutchLock: sim.clutchLock, cfLock: sim._cfLock, lever: sim._lever, cvtS: sim.cvtS, running: sim.running, ignition: sim.ignition,
        dynoPhase: sim.dynoPhase, dynoVersion: sim.dynoVersion, dynoFailed: sim.dynoFailed,
        limiter: sim.model.D.Limiter, isAuto: sim.isAuto, scope: sim.scope.slice(), scopeW: sim.scopeW,
        knock: sim.knock, ecuAfr: sim.ecuAfr, ecuAdv: sim.ecuAdv, ecuMbt: sim.ecuMbt, ecuDuty: sim.ecuDuty, ecuEgt: sim.ecuEgt, slipRpm: sim.slipRpm, tracUse: sim.tracUse, spin: sim.spin, driveForce: sim.driveForce, gripMax: sim.gripMax,
      };
      if (sim.dynoVersion !== this.lastVer) { this.lastVer = sim.dynoVersion; msg.dynoTq = Array.from(sim.dynoTq); }
      this.port.postMessage(msg);
    }
    return true;
  }
}
registerProcessor('rev-engine', RevProcessor);
