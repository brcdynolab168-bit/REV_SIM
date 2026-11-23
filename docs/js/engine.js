// Rev Lab (เว็บ): โมเดลเครื่องยนต์ + ตัวรถ + CVT + ผู้ช่วยจูน  (พอร์ตจากเวอร์ชัน C#/WinForms)
export const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
const PI = Math.PI;

// ---------------------------------------------------------------- น้ำมัน / ท่อ
export const PipeNames = ['ท่อเดิม', 'ท่อผ่าหมก', 'ท่อผ่าเปิด', 'ท่อสูตร'];
export const Fuels = [
  { name: 'เบนซิน 91', ron: 91, power: 0.99 },
  { name: 'เบนซิน 95', ron: 95, power: 1.0 },
  { name: 'แก๊สโซฮอล์ E20', ron: 98, power: 1.01 },
  { name: 'น้ำมันแข่ง RON 100', ron: 100, power: 1.015 },
  { name: 'E85 (เอทานอล 85%)', ron: 105, power: 1.045 },
  { name: 'เมทานอล', ron: 109, power: 1.08 },
  { name: 'เมทานอล + ไนโตร 20%', ron: 120, power: 1.22 },
];
const fuelOf = d => Fuels[clamp(Math.round(d.Fuel), 0, Fuels.length - 1)];

// ---------------------------------------------------------------- แบบเครื่อง
export function newDesign(o = {}) {
  const r = Object.assign({
    Bore: 52.4, Stroke: 57.9, Rod: 90, CrankKg: 2.8, FlywheelKg: 0.6, Valves: 2, Throttle: 20,
    Lsa: 114, Duration: 225, DurationEx: 225, Lift: 7.5, LiftEx: 7.5, Cr: 9.3, Seal: 1,
    RealHp: 0, RealHpRpm: 0, RealTq: 0, RealTqRpm: 0, CalK: 1,
    Limiter: 9500, ResumeGap: 80, SoftGap: 0,
    SprocketF: 14, SprocketR: 36, RiderKg: 65, Auto: 0,
    RampDeg: 13.5, RampOutDeg: 13.5, Rollers: 6, RollerG: 8.5, RollerG2: 8.5, GearF: 13, GearR: 40, FirstRed: 3.0,
    BikeKg: 107, CdA: 0.415, RimInch: 17, RimW: 1.4, TireW: 80, TireAsp: 90,
    Inj: 0, EcuAx: 0, EcuMs: null, EcuMap: null, EcuEoi: null, EcuDwell: null, EcuIgn: null,
    SpringK: 20, Primary: 3.35, Gear1: 2.5, Gear2: 1.55, Gear3: 1.15, Gear4: 0.923, Gears: 4, Gear5: 0.8, Gear6: 0.72, Pipe: 0, Fuel: 1,
  }, o);
  if (r.EcuAx !== 2) { r.EcuMs = r.EcuEoi = r.EcuIgn = r.EcuDwell = r.EcuMap = null; }      // ตารางจูนเก่าใช้แกนโหลด: ทิ้ง ใช้ค่าโรงงานแกน TPS
  return r;
}

export const gearAt = (d, g) => d['Gear' + clamp(Math.round(g), 1, clamp(d.Gears, 4, 6))];
export const EcuN = 20;
const TPS_PCT = [0, 0.5, 1, 1.5, 2, 3, 4, 5, 6.5, 8, 10, 13, 17, 22, 30, 40, 52, 66, 82, 100];
export const InjDeadMs = 0.7, DwellTau = 1.8;
const pulseDeg = (pwMs, rpm) => { const cyc = 120000 / Math.max(rpm, 200); return Math.min(pwMs, cyc) / cyc * 720; };
const sparkQ = (dwell, req) => (1 - Math.exp(-dwell / DwellTau)) / req;
const dwellF = (dwell, rpm, req) => {
  const sq = sparkQ(dwell, req), g = sq >= 1 ? 1 : Math.max(0.1, 1 - 2.4 * (1 - sq)), cyc = 120000 / Math.max(rpm, 200);
  return g * (1 - 0.25 * clamp((dwell / cyc - 0.55) / 0.4, 0, 1));
};
export const ecuLoadAt = j => (j + 1) / EcuN;
const StoichTab = [14.7, 14.7, 14.1, 14.6, 9.76, 6.45, 5.2], FuelDens = [0.74, 0.74, 0.75, 0.74, 0.78, 0.79, 0.85], BurnK = [0, 0, -0.5, 0, -2, -3, -4];
const timingF = d => Math.max(0.15, 1 - (d > 0 ? 0.0013 : 0.0009) * d * d);
const lambdaF = l => Math.max(0.2, l >= 0.89 ? 1 - 1.8 * (l - 0.89) ** 2 : 1 - 2.4 * (l - 0.89) ** 2);
export const crankPsi = cr => 14.7 * Math.pow(cr, 1.15);
export const crFromPsi = psi => Math.pow(psi / 14.7, 1 / 1.15);
export const camLoss = d => 1 - 0.002 * Math.max(0, d.Duration - 220);
export const crDynamic = d => d.Cr * camLoss(d);
export const psiOf = d => crankPsi(crDynamic(d)) * d.Seal;
export const tireCode = d => `${Math.round(d.TireW)}/${Math.round(d.TireAsp)}-${Math.round(d.RimInch)}`;

const CVT125 = { Auto: 1, RampDeg: 13.5, Rollers: 6, RollerG: 8.5, RollerG2: 8.5, GearF: 13, GearR: 40, FirstRed: 3.0, BikeKg: 113, RimInch: 14, RimW: 1.6, TireW: 90, TireAsp: 90 };
export const Presets = [
  ['Wave 125i เดิม', newDesign()],
  ['เวฟ 110 เดิม*', newDesign({ Bore: 50, Stroke: 55.6, Rod: 89, CrankKg: 2.6, FlywheelKg: 0.6, Throttle: 22, Cr: 9.0, Limiter: 9000 })],
  ['ลูก 53 แคมแต่ง*', newDesign({ Bore: 53, Stroke: 55.6, Rod: 89, CrankKg: 2.2, FlywheelKg: 0.5, Throttle: 26, Lsa: 108, Duration: 245, DurationEx: 245, Lift: 7, LiftEx: 7, Cr: 10, Limiter: 10500 })],
  ['ลูก 67 ซิ่ง*', newDesign({ Bore: 67, Stroke: 55.6, Rod: 89, CrankKg: 1.4, FlywheelKg: 0.4, Throttle: 34, Lsa: 104, Duration: 290, DurationEx: 290, Lift: 9, LiftEx: 9, Cr: 11.5, Limiter: 12800 })],
  ['ออโต้ 125 (CVT)*', newDesign(CVT125)],
  // รถจริง: ตัวเลขที่ผู้ผลิตประกาศ (ซีซี ลูก x ชัก CR กำลัง แรงบิด น้ำหนัก ยาง) ส่วน เรือน/แคม/ก้านสูบ/เม็ด/องศาชาม/สปริง ผู้ผลิตไม่เปิดเผย เป็นค่าประมาณ แล้วปรับ CalK ให้พีคตรงรถจริง
  // Yamaha XMAX 300: 292cc 70.0x75.9 CR 10.9 4V  20.6 kW (27.6 HP)/7,250  29 N·m/5,750  หนัก ~180 กก. ยางหลัง 140/70-14
  ['XMAX 300 (CVT)', newDesign({ Bore: 70, Stroke: 75.9, Rod: 118, CrankKg: 4.7, FlywheelKg: 0.8, Valves: 4, Throttle: 32, Lsa: 110, Duration: 225, DurationEx: 225, Lift: 7.5, LiftEx: 7.5, Cr: 10.9, Limiter: 8800,
    Auto: 1, RampDeg: 13.5, RampOutDeg: 13.5, Rollers: 6, RollerG: 13.5, RollerG2: 13.5, SpringK: 20, GearF: 13, GearR: 37, FirstRed: 48 / 18, BikeKg: 180, RimInch: 14, RimW: 3.5, TireW: 140, TireAsp: 70, RiderKg: 75, CdA: 0.32,
    RealHp: 27.6, RealHpRpm: 7250, RealTq: 29, RealTqRpm: 5750, CalK: 0.975 })],
  // Honda Forza 350: 330cc 77.0x70.8 CR 10.5 4V  21.5 kW (28.8 HP)/7,500  31.5 N·m/5,250  หนัก ~186 กก. ยางหลัง 140/70-14
  ['Forza 350 (CVT)', newDesign({ Bore: 77, Stroke: 70.8, Rod: 115, CrankKg: 4.9, FlywheelKg: 0.85, Valves: 4, Throttle: 34, Lsa: 110, Duration: 225, DurationEx: 225, Lift: 7.5, LiftEx: 7.5, Cr: 10.5, Limiter: 8800,
    Auto: 1, RampDeg: 13.5, RampOutDeg: 13.5, Rollers: 6, RollerG: 14, RollerG2: 14, SpringK: 20, GearF: 13, GearR: 37, FirstRed: 48 / 18, BikeKg: 186, RimInch: 14, RimW: 3.5, TireW: 140, TireAsp: 70, RiderKg: 75, CdA: 0.32,
    RealHp: 28.8, RealHpRpm: 7500, RealTq: 31.5, RealTqRpm: 5250, CalK: 0.95 })],
  // Honda Giorno+ 125: 125cc 53.5x55.5 CR 11.5 2V  8.49 kW (11.4 HP)/8,750  11.6 N·m/5,250  หนัก ~116 กก. ยาง 100/90-12 หน้า-หลัง
  ['Giorno+ 125 (CVT)', newDesign({ Bore: 53.5, Stroke: 55.5, Rod: 90, CrankKg: 2.4, FlywheelKg: 0.6, Valves: 2, Throttle: 24, Lsa: 110, Duration: 232, DurationEx: 232, Lift: 7.5, LiftEx: 7.5, Cr: 11.5, Limiter: 9800,
    Auto: 1, RampDeg: 13.5, RampOutDeg: 13.5, Rollers: 6, RollerG: 8, RollerG2: 8, SpringK: 18, GearF: 13, GearR: 40, FirstRed: 3.0, BikeKg: 116, RimInch: 12, RimW: 2.5, TireW: 100, TireAsp: 90, RiderKg: 65, CdA: 0.40,
    RealHp: 11.4, RealHpRpm: 8750, RealTq: 11.6, RealTqRpm: 5250, CalK: 0.975 })],
  // Yamaha MX King 155 (VVA): 155cc 58.0x58.7 4V เกียร์ 6 สปีด (เกมมี 4 สปีด จึงเลือกอัตราทดห่างสุดโต่ง) หนัก 119 กก. ยาง 90/80-17  ~16 PS/9,000  14.2 N·m/6,500
  // CR 11.6, แคม/ก้านสูบ/ข้อเหวี่ยง/สเตอร์/อัตราทดเป็นค่าประมาณ (ไม่ได้ประกาศ)
  ['MX King 155 เดิม', newDesign({ Bore: 58, Stroke: 58.7, Rod: 105, CrankKg: 2.5, FlywheelKg: 0.55, Valves: 4, Throttle: 28, Lsa: 110, Duration: 244, DurationEx: 244, Lift: 7.8, LiftEx: 7.8, Cr: 11.6, Limiter: 10800,
    SprocketF: 15, SprocketR: 42, Primary: 3.05, Gear1: 2.833, Gear2: 1.875, Gears: 6, Gear3: 1.364, Gear4: 1.087, Gear5: 0.92, Gear6: 0.815, BikeKg: 119, RiderKg: 65, CdA: 0.40, RimInch: 17, RimW: 1.6, TireW: 90, TireAsp: 80,
    RealHp: 15.9, RealHpRpm: 9000, RealTq: 14.2, RealTqRpm: 6500, CalK: 0.842 })],
  // สเต็ป: ลูก 65 มม. ชัก +5 มม. (58.7 -> 63.7) = 211cc  แคม/กำลังอัด/รอบตัด แต่งเพิ่ม  ชุดเดียวกับเดิม ค่า CalK เดิม
  ['MX King สเต็ป ลูก 65 ชัก+5*', newDesign({ Bore: 65, Stroke: 63.7, Rod: 110, CrankKg: 2.8, FlywheelKg: 0.45, Valves: 4, Throttle: 30, Lsa: 110, Duration: 256, DurationEx: 256, Lift: 8.8, LiftEx: 8.8, Cr: 12.0, Limiter: 11500, Fuel: 3,
    SprocketF: 15, SprocketR: 42, Primary: 3.05, Gear1: 2.833, Gear2: 1.875, Gears: 6, Gear3: 1.364, Gear4: 1.087, Gear5: 0.92, Gear6: 0.815, BikeKg: 119, RiderKg: 65, CdA: 0.40, RimInch: 17, RimW: 1.6, TireW: 90, TireAsp: 80,
    RealHp: 15.9, RealHpRpm: 9000, RealTq: 14.2, RealTqRpm: 6500, CalK: 0.842 })],
  ['สตรีท 150 ลูก 58*', newDesign({ Bore: 58, Stroke: 57.9, Rod: 90, CrankKg: 2.5, FlywheelKg: 0.5, Throttle: 28, Lsa: 110, Duration: 240, DurationEx: 240, Lift: 8, LiftEx: 8, Cr: 10.5, Limiter: 10500, Fuel: 2 })],
  ['เดี่ยว 300 ลูก 80*', newDesign({ Bore: 80, Stroke: 60, Rod: 100, CrankKg: 4.8, FlywheelKg: 0.7, Valves: 4, Throttle: 38, Lsa: 108, Duration: 255, DurationEx: 255, Lift: 9.5, LiftEx: 9.5, Cr: 11.5, Limiter: 11000, Fuel: 3, BikeKg: 120 })],
  ['เดี่ยว 600 ลูก 100*', newDesign({ Bore: 100, Stroke: 76.5, Rod: 125, CrankKg: 7.2, FlywheelKg: 1.3, Valves: 4, Throttle: 46, Lsa: 108, Duration: 250, DurationEx: 250, Lift: 10.5, LiftEx: 10.5, Cr: 12.5, Limiter: 10500, Fuel: 3, BikeKg: 140 })],
  ['ดรักเมทานอล 1000 ลูก 120*', newDesign({ Bore: 120, Stroke: 88, Rod: 150, CrankKg: 10, FlywheelKg: 2.0, Valves: 4, Throttle: 60, Lsa: 106, Duration: 270, DurationEx: 270, Lift: 13, LiftEx: 13, Cr: 14.5, Limiter: 9500, Fuel: 5, BikeKg: 160 })],
];

// ---------------------------------------------------------------- รายการค่าที่ปรับได้ในอู่
const fmt = (v, f) => v.toFixed(f === '0' ? 0 : f.length - 2);
// P(key, name, unit, min, max, step, format, get, set, hint, {visible, text, persist})
function P(key, name, unit, min, max, step, format, get, set, hint, o = {}) {
  return { key, name, unit, min, max, step, format, get, set, hint, visible: o.visible || null, text: o.text || null, hidden: !!o.hidden };
}
const pr = (k, p) => [d => d[p], (d, v) => { d[p] = v; }];
const num = (key, name, unit, min, max, step, format, prop, hint, o) => P(key, name, unit, min, max, step, format, ...pr(key, prop), hint, o);
const manual = d => d.Auto === 0, auto = d => d.Auto === 1;
export const Params = [
  num('bore', 'ขนาดลูกสูบ', 'มม.', 47, 120, 0.5, '0.0', 'Bore', 'ลูกใหญ่ = ซีซีเยอะ แรงขึ้น แต่น็อกง่ายขึ้น'),
  num('stroke', 'ระยะชัก', 'มม.', 49.5, 100, 0.1, '0.0', 'Stroke', 'ชักยาว = แรงบิดดี แต่ลูกสูบวิ่งเร็ว ลากรอบสูงไม่ได้'),
  num('rod', 'ก้านสูบ', 'มม.', 80, 200, 0.5, '0.0', 'Rod', 'ก้านยาว = แรงเสียดทาน/แรงกระชากลูกสูบน้อยลง'),
  num('crank', 'นน.ข้อเหวี่ยง', 'กก.', 1.2, 16, 0.1, '0.0', 'CrankKg', 'เบา = รอบขึ้น-ลงไว แต่เดินเบาไม่นิ่ง'),
  num('fly', 'นน.จานไฟ', 'กก.', 0.2, 3, 0.05, '0.00', 'FlywheelKg', 'จานไฟหนัก = ความเฉื่อยสูง รอบขึ้นช้า/ออกตัวนิ่ง เดินเบาเรียบ  เบา = เร่งรอบไว แต่เดินเบาสะดุด'),
  num('valves', 'จำนวนวาล์ว', 'วาล์ว', 2, 4, 2, '0', 'Valves', '4 วาล์ว = ไอดีเข้าเยอะ วาล์วเบา ลากรอบสูงได้'),
  num('throttle', 'ขนาดเรือน', 'มม.', 20, 60, 1, '0', 'Throttle', 'เรือนใหญ่ = ปลายไหล แต่ต้องมีวาล์ว/แคมรองรับ'),
  P('pipe', 'ท่อไอเสีย', '', 0, 3, 1, '0', ...pr('pipe', 'Pipe'),
    'เดิม = เงียบ  ผ่าหมก = ปลายขึ้นนิดหน่อย เสียงดังขึ้น  ผ่าเปิด = ปลายดีแต่รอบต่ำตก เสียงแผดดังมาก  ท่อสูตร = จูนรอบเดียว พีคแรงขึ้นชัดแต่ช่วงล่างหาย', { text: v => PipeNames[clamp(Math.round(v), 0, 3)] }),
  num('lsa', 'มุมแคม (LSA)', '°', 98, 118, 1, '0', 'Lsa', 'มุมแคบ = แรงบิดพีคสูงแต่ช่วงแคบ โอเวอร์แลปมาก เดินเบาสะดุด'),
  num('duration', 'องศาแคม ไอดี (IN)', '°', 200, 320, 2, '0', 'Duration', 'องศามาก = แรงย้ายไปรอบสูง เดินเบาสั่น  ไอดีปิดช้า = กำลังอัดจริงลด'),
  num('durationex', 'องศาแคม ไอเสีย (EX)', '°', 200, 320, 2, '0', 'DurationEx', 'ไอเสียยาวกว่าไอดี = ปลายไหลดี แต่โอเวอร์แลปมาก  สั้นกว่าไอดีมาก = ไอเสียอั้น ปลายตก'),
  num('lift', 'ระยะยกแคม ไอดี (IN)', 'มม.', 5, 16, 0.1, '0.0', 'Lift', 'ยกสูง = ไอดีไหลดี แต่วาล์วลอยที่รอบต่ำลง'),
  num('liftex', 'ระยะยกแคม ไอเสีย (EX)', 'มม.', 5, 16, 0.1, '0.0', 'LiftEx', 'ยกไอเสียน้อยกว่าไอดีมาก = ไอเสียอั้น ปลายตก  ยกมากกว่า = ไหลดีขึ้นเล็กน้อย แต่วาล์วลอยง่ายขึ้น'),
  P('cr', 'อัตราส่วนกำลังอัด (CR)', ':1', 7, 20, 0.1, '0.0', ...pr('cr', 'Cr'),
    'ค่าเรขาคณิต สูง = ประสิทธิภาพเชิงความร้อนดี เสียงแผด  แยกจากแรงดันอัด (psi) ที่ปรับอีกบรรทัด', { text: v => v.toFixed(1) + ' : 1' }),
  P('psi', 'แรงดันอัด (วัดจริง)', 'psi', 100, 480, 5, '0', d => psiOf(d), (d, v) => { d.Seal = clamp(v / crankPsi(crDynamic(d)), 0.6, 1.5); },
    'ค่าที่เกจวัดตอนสตาร์ท ปรับแยกจาก CR: ซีลแน่น = แรงบิดดีขึ้นแต่น็อกง่าย  ต่ำ = รั่ว กำลังตก จุดไม่ติด (Wave ~190, แต่งถนน 200-230, แข่ง 260-300+)', { text: v => Math.round(v) + ' psi' }),
  P('fuel', 'ชนิดน้ำมัน', '', 0, 6, 1, '0', ...pr('fuel', 'Fuel'), 'ออกเทนสูง = ทนกำลังอัดสูงก่อนน็อก  E85/เมทานอล/ไนโตร ฉีดหนาขึ้นได้กำลังเพิ่มด้วย',
    { text: v => { const f = Fuels[clamp(Math.round(v), 0, 6)]; return `${f.name}  (RON ${f.ron})`; } }),
  P('inj', 'หัวฉีด (ขนาดรู)', 'cc/นาที', 0, 3000, 10, '0', d => d.Inj, (d, v) => { d.Inj = v; }, 'ความจุหัวฉีด (เบนซิน) ถ้าเครื่องต้องการน้ำมันมากกว่านี้ (รอบสูง/E85/เมทานอล ใช้น้ำมันมากกว่าเบนซิน 1.5-2.3 เท่า) หัวฉีดเต็ม 100% แล้วส่วนผสมจะบางลง  0 = ตามรถ (เหมาะกับซีซีและน้ำมันที่เลือก)', { text: v => (v <= 0 ? 'ตามรถ (อัตโนมัติ)' : `${Math.round(v)} cc/นาที`) }),
  num('seal', 'รอบตัด (hard cut)', 'rpm', 6000, 15000, 100, '0', 'Limiter', 'ถึงรอบนี้ตัดไฟทั้งหมดทันที ตั้งเกินที่เครื่องรับไหว = พัง'),
  P('resume', 'รอบเด้งกลับหลัง hard cut', 'rpm', 20, 3000, 10, '0', ...pr('resume', 'ResumeGap'),
    'ตัดแล้วรอบต้องตกลงถึงรอบนี้จึงติดใหม่  ตัดน้อย = รัวถี่  ตัดมาก = รอบตกลึก', { text: (v, d) => `${Math.round(d.Limiter - v)} rpm  (ตกลง ${Math.round(v)})` }),
  P('softcut', 'Soft cut ห่างใต้ hard cut', 'rpm', 0, 2000, 50, '0', ...pr('softcut', 'SoftGap'),
    'หน่วงไฟ/ข้ามจังหวะระเบิดตั้งแต่ใต้ hard cut ลงมา 0 = ไม่ใช้', { text: (v, d) => (v <= 0 ? 'ปิด' : `${Math.round(d.Limiter - v)} rpm  (ห่าง ${Math.round(v)})`) }),
  num('prim', 'อัตราทดขั้นต้น', ':1', 2.5, 4.5, 0.01, '0.000', 'Primary', 'ทดขั้นต้นสูง = ทุกเกียร์ทดจัดขึ้นเท่ากัน (Wave 125i = 3.350)', { visible: manual }),
  num('gears', 'จำนวนเกียร์', 'สปีด', 4, 6, 1, '0', 'Gears', 'รถเกียร์ธรรมดามี 4 / 5 / 6 สปีด ตามรุ่นรถจริง (Wave 4 สปีด, Sonic/CBR150 5 สปีด, MX King/R15 6 สปีด)  เกียร์มากขึ้น = ช่วงห่างแคบลง รอบตกน้อยตอนเปลี่ยน', { visible: manual }),
  num('g1', 'อัตราทดเกียร์ 1', ':1', 1.6, 4, 0.01, '0.000', 'Gear1', 'ตัวเลขมาก = ทดจัด ออกตัวแรง (Wave 2.500)', { visible: manual }),
  num('g2', 'อัตราทดเกียร์ 2', ':1', 1.1, 2.8, 0.01, '0.000', 'Gear2', 'ช่วงห่างเกียร์ 1-2 กว้าง = รอบตกมากตอนเปลี่ยน (Wave 1.550)', { visible: manual }),
  num('g3', 'อัตราทดเกียร์ 3', ':1', 0.8, 2, 0.01, '0.000', 'Gear3', 'เกียร์ชิดไล่ต่อเนื่อง แต่ท็อปสปีดไม่ไกล (Wave 1.150)', { visible: manual }),
  num('g4', 'อัตราทดเกียร์ 4', ':1', 0.6, 1.5, 0.01, '0.000', 'Gear4', 'ต่ำกว่า 1 = โอเวอร์ไดรฟ์ ปลายยาว (Wave 0.923)', { visible: manual }),
  num('g5', 'อัตราทดเกียร์ 5', ':1', 0.55, 1.4, 0.01, '0.000', 'Gear5', 'เกียร์ 5 ต้องทดน้อยกว่าเกียร์ 4 (ถ้าเป็นเกียร์ท้ายสุดของรถ 5 สปีด = เกียร์โอเวอร์ไดรฟ์)', { visible: d => d.Auto === 0 && d.Gears >= 5 }),
  num('g6', 'อัตราทดเกียร์ 6', ':1', 0.5, 1.3, 0.01, '0.000', 'Gear6', 'เกียร์ 6 โอเวอร์ไดรฟ์ ปลายยาวสุด ถ้ายาวเกินกำลังไม่พอลากถึงรอบ', { visible: d => d.Auto === 0 && d.Gears >= 6 }),
  P('gtop', 'อัตราทดเกียร์ท้ายสุด', ':1', 0.5, 1.5, 0.01, '0.000', d => gearAt(d, d.Gears), (d, v) => { d['Gear' + clamp(d.Gears, 4, 6)] = v; }, 'เกียร์ท้ายสุดของรถ (ใช้ที่ปรึกษาจูน)', { visible: () => false, hidden: true }),
  num('sprf', 'สเตอร์หน้า', 'ฟัน', 11, 18, 1, '0', 'SprocketF', 'เล็กลง = ออกตัวดี แต่ปลายสั้น', { visible: manual }),
  num('sprr', 'สเตอร์หลัง', 'ฟัน', 28, 46, 1, '0', 'SprocketR', 'ใหญ่ขึ้น = ทดจัด ออกตัวแรง ท็อปสปีดต่ำ  เล็กลง = ปลายยาว', { visible: manual }),
  num('rider', 'น้ำหนักคนขี่', 'กก.', 40, 110, 1, '0', 'RiderKg', 'น้ำหนักรวมมาก = ออกตัวช้า ไต่เนินช้า'),
  num('bikekg', 'น้ำหนักรถเปล่า', 'กก.', 80, 260, 1, '0', 'BikeKg', 'รถหนัก = ออกตัวและไต่ความเร็วช้า (Wave ~107, สกู๊ตเตอร์ 125 ~113, XMAX ~180)'),
  num('cda', 'พื้นที่ลมต้าน (CdA)', 'ตร.ม.', 0.25, 0.8, 0.01, '0.00', 'CdA', 'ต่ำ = ลู่ลม ท็อปสปีดสูง (สกู๊ตเตอร์ ~0.32, รถซิ่งนั่งตรง ~0.42-0.5)'),
  num('rim', 'ขนาดขอบล้อ', 'นิ้ว', 10, 19, 1, '0', 'RimInch', 'ขอบใหญ่ = รัศมีล้อโต ความเร็วต่อรอบสูงขึ้น ล้อหนักขึ้น  ขอบเล็ก = ออกตัวไว'),
  num('rimw', 'ความกว้างขอบล้อ', 'นิ้ว', 1, 4, 0.05, '0.00', 'RimW', 'ใส่ให้พอดีหน้ายาง (1.10/1.20/1.40/1.60…) ไม่พอดี = แรงเกาะตก'),
  num('tirew', 'หน้ายาง', 'มม.', 50, 190, 5, '0', 'TireW', 'ยางกว้าง = เกาะดีขึ้น แต่ต้านการกลิ้งมากขึ้นและล้อหนักขึ้น'),
  num('tireasp', 'แก้มยาง', '%', 40, 100, 5, '0', 'TireAsp', 'แก้มสูง = รัศมีล้อโต ความเร็วต่อรอบสูงขึ้น  แก้มต่ำ = ทดจัดขึ้น'),
  P('auto', 'ระบบส่งกำลัง', '', 0, 1, 1, '0', ...pr('auto', 'Auto'), 'ออโต้ = สายพาน CVT ไม่ต้องเปลี่ยนเกียร์ ปรับชามเม็ด/น้ำหนักเม็ดเพื่อจูน', { text: v => (v > 0.5 ? 'ออโต้ (CVT)' : 'เกียร์ธรรมดา') }),
  num('ramp', 'องศาชาม ใบใน', '°', 11, 16, 0.5, '0.0', 'RampDeg', 'ช่วงต้นทาง (ออกตัว): องศาน้อย = เม็ดดันชุดขับแรง ไล่รอบเร็ว ถือรอบต่ำ  องศามาก = ถือรอบสูง ออกตัวจัด', { visible: auto }),
  num('rampout', 'องศาชาม ใบนอก', '°', 11, 16, 0.5, '0.0', 'RampOutDeg', 'ช่วงปลายทาง (ไล่สายพานใกล้สุด): องศาน้อยกว่าใบใน = ปลายดันแรง ไล่ถึงปลายไว รอบปลายต่ำลง  มากกว่าใบใน = ปลายถือรอบสูงขึ้น ท็อปไกล (ชามแต่งองศา 2 ช่วง เช่น 13/11)', { visible: auto }),
  num('rollers', 'จำนวนเม็ด', 'เม็ด', 4, 6, 2, '0', 'Rollers', '6 เม็ด = มวลรวมมาก ไล่รอบเร็ว  4 เม็ด = ถือรอบสูงกว่า', { visible: auto }),
  num('rollerg', 'น้ำหนักเม็ด', 'กรัม', 4, 20, 0.5, '0.0', 'RollerG', 'เม็ดหนัก = ไล่รอบไว ถือรอบต่ำ  เม็ดเบา = ถือรอบสูง ปลายไปไกล', { visible: auto }),
  num('rollerg2', 'น้ำหนักเม็ดสลับ', 'กรัม', 4, 20, 0.5, '0.0', 'RollerG2', 'เม็ดตำแหน่งที่ 2, 4, 6 ผสมหนัก/เบาเพื่อจูนละเอียด', { visible: auto }),
  num('fdf', 'เฟืองท้าย ตัวหน้า', 'ฟัน', 10, 20, 1, '0', 'GearF', 'ฟันหน้าน้อย/ฟันหลังมาก = ทดจัด ออกตัวแรง ท็อปสปีดลด', { visible: auto }),
  num('fdr', 'เฟืองท้าย ตัวหลัง', 'ฟัน', 28, 50, 1, '0', 'GearR', 'ฟันมาก = ทดจัดขึ้น  ฟันน้อย = ปลายยาว', { visible: auto }),
  num('firstred', 'ลดขั้นแรก (ในเรือนเกียร์)', ':1', 2, 4, 0.01, '0.000', 'FirstRed', 'ตายตัวตามรถ (XMAX 300 = 48/18 = 2.667)', { visible: auto }),
  num('spring', 'สปริงทอค (ความแข็ง)', 'N/มม.', 10, 40, 1, '0', 'SpringK', 'แข็ง = CVT ไล่รอบช้า ถือรอบสูง  นิ่ม = ไล่รอบไว ถือรอบต่ำ', { visible: auto }),
  num('realhp', 'แรงม้ารถจริง (อ้างอิง)', 'HP', 0, 500, 0.5, '0.0', 'RealHp', 'ใส่แรงม้าสูงสุดจากรถจริง (0 = ไม่ระบุ) แล้วกดปรับเทียบ', { text: v => (v <= 0 ? 'ไม่ระบุ' : v.toFixed(1) + ' HP') }),
  num('realhprpm', 'รอบที่ได้แรงม้าสูงสุด', 'rpm', 0, 16000, 100, '0', 'RealHpRpm', 'รอบของแรงม้าสูงสุดรถจริง (ใช้เทียบ)', { text: v => (v <= 0 ? 'ไม่ระบุ' : Math.round(v) + ' rpm') }),
  num('realtq', 'แรงบิดรถจริง (อ้างอิง)', 'N·m', 0, 500, 0.1, '0.0', 'RealTq', 'ใส่แรงบิดสูงสุดจากรถจริง (ใช้ปรับเทียบถ้าไม่ได้ใส่แรงม้า)', { text: v => (v <= 0 ? 'ไม่ระบุ' : v.toFixed(1) + ' N·m') }),
  num('realtqrpm', 'รอบที่ได้แรงบิดสูงสุด', 'rpm', 0, 16000, 100, '0', 'RealTqRpm', 'รอบของแรงบิดสูงสุดรถจริง (ใช้เทียบ)', { text: v => (v <= 0 ? 'ไม่ระบุ' : Math.round(v) + ' rpm') }),
  num('calk', 'ตัวคูณปรับเทียบโมเดล', 'x', 0.3, 3, 0.01, '0.00', 'CalK', 'คูณแรงบิดของโมเดลทั้งช่วงรอบ ปกติกดปรับเทียบอัตโนมัติ ค่านี้คงที่เมื่อเปลี่ยนอะไหล่'),
];
export const paramByKey = Object.fromEntries(Params.map(p => [p.key, p]));
export function paramDisplay(p, d) {
  const v = p.get(d);
  if (p.text) return p.text(v, d);
  const dec = p.format === '0' ? 0 : p.format.length - 2;
  return v.toFixed(dec) + (p.unit ? ' ' + p.unit : '');
}
export function nudge(p, d, dir, coarse) {
  const step = p.step * (coarse && p.step < 2 ? 10 : 1);
  let v = p.get(d) + dir * step;
  v = Math.round(v / p.step) * p.step;
  p.set(d, clamp(+v.toFixed(6), p.min, p.max));
}

// ---------------------------------------------------------------- แท็บอู่
export const GarageTabs = [
  ['อะไหล่', []],
  ['เครื่องยนต์', ['bore', 'stroke', 'rod', 'crank', 'fly', 'valves']],
  ['ไอดี·แคม·ท่อ', ['throttle', 'lsa', 'duration', 'durationex', 'lift', 'liftex', 'pipe']],
  ['อัด·น้ำมัน', ['cr', 'psi', 'fuel', 'inj', 'limiter', 'resume', 'softcut']],
  ['ส่งกำลัง', ['auto', 'prim', 'gears', 'g1', 'g2', 'g3', 'g4', 'g5', 'g6', 'sprf', 'sprr', 'ramp', 'rampout', 'rollers', 'rollerg', 'rollerg2', 'fdf', 'fdr', 'firstred', 'spring']],
  ['ตัวรถ', ['rider', 'bikekg', 'rim', 'rimw', 'tirew', 'tireasp', 'cda']],
  ['จูนกล่อง', []],
  ['รถจริง', ['realhp', 'realhprpm', 'realtq', 'realtqrpm', 'calk']],
];

// ---------------------------------------------------------------- อะไหล่
const near = (a, b, t) => Math.abs(a - b) <= t;
const item = (name, apply, is) => ({ name, apply, is });
export const PartCats = [
  { name: 'ลูกสูบ + กระบอกสูบ', hint: 'ขนาดลูกยิ่งโตยิ่งซีซีเยอะ แต่น็อกง่ายขึ้น',
    items: [47, 50, 52.4, 54, 57, 59, 62, 65, 67, 70, 75, 80, 90, 100, 110, 120].map(b => item(`ลูก ${b} มม.`, d => { d.Bore = b; }, d => near(d.Bore, b, 0.26))) },
  { name: 'ชุดข้อเหวี่ยง + ก้านสูบ', hint: 'ชัก / ก้าน / นน.ข้อเหวี่ยง (ไม่รวมจานไฟ)',
    items: [[49.5, 80, 2.2], [55.6, 89, 2.6], [57.9, 90, 2.8], [63, 100, 3.4], [68, 108, 4.0], [75.8, 118, 4.7], [80, 124, 5.6], [90, 140, 8], [100, 155, 11]]
      .map(([s, r, k]) => item(`ชัก ${s} / ก้าน ${r} / ${k} กก.`, d => { d.Stroke = s; d.Rod = r; d.CrankKg = k; }, d => near(d.Stroke, s, 0.15) && near(d.Rod, r, 0.6) && near(d.CrankKg, k, 0.06))) },
  { name: 'จานไฟ (โรเตอร์)', hint: 'หนัก = รอบขึ้นช้า ออกตัวนิ่ง เดินเบาเรียบ  เบา = เร่งรอบไว แต่เดินเบาสะดุด',
    items: [0.3, 0.45, 0.6, 0.8, 1.0, 1.3, 1.6, 2.0, 2.5].map(w => item(`จานไฟ ${w.toFixed(2)} กก.`, d => { d.FlywheelKg = w; }, d => near(d.FlywheelKg, w, 0.026))) },
  { name: 'ฝาสูบ (วาล์ว)', hint: '4 วาล์วหายใจดี ลากรอบสูงได้',
    items: [item('2 วาล์ว', d => { d.Valves = 2; }, d => d.Valves === 2), item('4 วาล์ว', d => { d.Valves = 4; }, d => d.Valves === 4)] },
  { name: 'แคมชาฟท์ (IN=EX)', hint: 'LSA / องศา / ยก  องศามาก = แรงย้ายไปรอบสูง เดินเบาสะดุด',
    items: [['แคมเดิมโรงงาน', 114, 225, 7.5], ['แคมแต่งเบา', 112, 235, 8], ['แคมแต่งถนน', 108, 245, 8.5], ['แคมแข่ง', 106, 270, 9.5], ['แคมซิ่ง', 104, 290, 10], ['แคมสายแข่งหนัก', 102, 310, 12]]
      .map(([n, l, du, h]) => item(`${n}  ${l}°/${du}°/${h} มม.`, d => { d.Lsa = l; d.Duration = du; d.DurationEx = du; d.Lift = h; d.LiftEx = h; },
        d => near(d.Lsa, l, 0.6) && near(d.Duration, du, 1.1) && near(d.DurationEx, du, 1.1) && near(d.Lift, h, 0.06) && near(d.LiftEx, h, 0.06))) },
  { name: 'แคมไอเสีย (EX) แยก', hint: 'องศาไอเสียแยกจากไอดี',
    items: [215, 225, 235, 245, 255, 270, 290, 310].map(x => item(`ไอเสีย ${x}°`, d => { d.DurationEx = x; }, d => near(d.DurationEx, x, 1.1))) },
  { name: 'ยกไอเสีย (EX) แยก', hint: 'ระยะยกไอเสียแยกจากไอดี',
    items: [6, 7, 7.5, 8, 8.5, 9, 9.5, 10, 11, 12, 13].map(x => item(`ยกไอเสีย ${x} มม.`, d => { d.LiftEx = x; }, d => near(d.LiftEx, x, 0.06))) },
  { name: 'เรือนลิ้นเร่ง', hint: 'เรือนใหญ่ = ปลายไหล แต่ต้องมีวาล์ว/แคมรองรับ',
    items: [20, 22, 24, 26, 28, 30, 32, 34, 38, 42, 46, 50, 54, 60].map(t => item(`เรือน ${t} มม.`, d => { d.Throttle = t; }, d => near(d.Throttle, t, 0.5))) },
  { name: 'อัตราส่วนกำลังอัด (CR)', hint: 'ค่าเรขาคณิต: สูง = ประสิทธิภาพดี แต่ต้องใช้น้ำมันออกเทนสูง',
    items: [8.5, 9.3, 10, 10.5, 11, 11.5, 12.5, 13.5, 14.5, 16].map(c => item(`CR ${c.toFixed(1)}:1`, d => { d.Cr = c; }, d => near(d.Cr, c, 0.05))) },
  { name: 'แรงดันอัด (psi)', hint: 'แยกจาก CR: ซีลแน่น = แรงบิดเพิ่มแต่น็อกง่าย  ต่ำ = รั่ว กำลังตก',
    items: [140, 160, 180, 200, 230, 260, 300, 350, 400].map(p => item(`${p} psi`, d => { d.Seal = clamp(p / crankPsi(crDynamic(d)), 0.6, 1.5); }, d => near(psiOf(d), p, 7))) },
  { name: 'ขนาดล้อ (ขอบ+ยาง)', hint: 'ขอบล้อ / กว้างขอบ / ยาง  ขอบใหญ่/แก้มสูง = ความเร็วต่อรอบสูง  ยางกว้าง = เกาะดี',
    items: [[17, 1.2, 70, 90], [17, 1.4, 80, 90], [17, 1.4, 90, 80], [17, 1.6, 100, 80], [17, 1.85, 110, 70], [17, 2.15, 120, 70], [17, 1.6, 90, 80], [14, 1.4, 80, 90], [14, 1.6, 90, 90], [14, 2.15, 110, 80], [14, 3.5, 140, 70], [12, 1.4, 100, 90], [10, 1.6, 90, 90]]
      .map(([r, w, t, a]) => item(`${t}/${a}-${r}  ขอบ ${w.toFixed(2)}`, d => { d.RimInch = r; d.RimW = w; d.TireW = t; d.TireAsp = a; },
        d => near(d.RimInch, r, 0.1) && near(d.RimW, w, 0.03) && near(d.TireW, t, 2) && near(d.TireAsp, a, 2))) },
  { name: 'ชนิดน้ำมัน', hint: 'ออกเทนสูง = ทนกำลังอัดสูงก่อนน็อก',
    items: Fuels.map((f, i) => item(`${f.name}  RON ${f.ron}`, d => { d.Fuel = i; }, d => d.Fuel === i)) },
  { name: 'ชุดเกียร์ (4 / 5 / 6 สปีด)', hint: 'เลือกชุดเกียร์ทั้งชุด รวมจำนวนสปีด  ชิด = ไล่เกียร์ต่อเนื่อง  ห่าง = ออกตัวแรงแต่รอบตกมาก (ชุด 5-6 สปีดเป็นค่าประมาณตามรถประเภทเดียวกัน)',
    items: [['4 สปีด เดิม Wave 125i', 3.35, [2.5, 1.55, 1.15, 0.923]], ['4 สปีด ชิด', 3.35, [2.4, 1.7, 1.3, 1.05]], ['4 สปีด ออกตัวจัด', 3.35, [2.9, 1.8, 1.3, 1.0]], ['4 สปีด ปลายยาว', 3.35, [2.5, 1.55, 1.15, 0.85]], ['4 สปีด ดรัก (ชิดจัด)', 3.6, [2.7, 1.9, 1.45, 1.15]],
      ['5 สปีด มาตรฐาน', 3.2, [2.833, 1.882, 1.409, 1.13, 0.958]], ['5 สปีด ชิด', 3.2, [2.6, 1.85, 1.45, 1.2, 1.02]],
      ['6 สปีด MX King/R15', 3.05, [2.833, 1.875, 1.364, 1.087, 0.92, 0.815]], ['6 สปีด ชิด', 3.05, [2.7, 1.9, 1.5, 1.25, 1.08, 0.95]], ['6 สปีด ออกตัวจัด', 3.05, [3.1, 2.0, 1.45, 1.15, 0.95, 0.82]]]
      .map(([n, p, gs]) => item(`${n}  ${gs.map(x => x.toFixed(2)).join('/')}`, d => { d.Primary = p; d.Gears = gs.length; gs.forEach((x, i) => { d['Gear' + (i + 1)] = x; }); },
        d => d.Gears === gs.length && near(d.Primary, p, 0.006) && gs.every((x, i) => near(d['Gear' + (i + 1)], x, 0.006)))) },
  { name: 'ชามเม็ด (องศาใน/นอก)', hint: 'ชามแต่งมี 2 องศา: ใบใน (ออกตัว) / ใบนอก (ปลายทาง)  ใบนอกน้อยกว่า = ปลายไล่ไว  ใบนอกมากกว่า = ปลายถือรอบสูง (ใช้กับรถออโต้)',
    items: [[13.5, 13.5], [13, 12], [14, 12], [12, 14], [15, 11], [12, 12], [14, 14], [11, 13], [16, 12]].map(([i, o]) => item(`ชาม ${i}° / ${o}°`, d => { d.RampDeg = i; d.RampOutDeg = o; }, d => near(d.RampDeg, i, 0.05) && near(d.RampOutDeg, o, 0.05))) },
  { name: 'ท่อไอเสีย', hint: 'เดิม = เงียบ / ผ่าหมก / ผ่าเปิด / ท่อสูตร = พีคแรงเฉพาะรอบ',
    items: PipeNames.map((n, i) => item(n, d => { d.Pipe = i; }, d => d.Pipe === i)) },
  { name: 'รอบตัด (hard cut)', hint: 'ตั้งสูงเกินที่วาล์ว/ลูกสูบรับไหว = กำลังตก',
    items: [8000, 9000, 9500, 10000, 10500, 11500, 12800, 14000, 15000].map(r => item(`รอบตัด ${r.toLocaleString()}`, d => { d.Limiter = r; }, d => near(d.Limiter, r, 60))) },
  { name: 'รอบเด้งกลับหลัง hard cut', hint: 'ตัดแล้วต้องตกลงมาเท่าไรจึงติดใหม่',
    items: [30, 80, 150, 200, 300, 500, 800, 1200].map(g => item(`ตกลง ${g} rpm`, d => { d.ResumeGap = g; }, d => near(d.ResumeGap, g, 5))) },
  { name: 'Soft cut (ห่างใต้ hard cut)', hint: 'หน่วงไฟก่อนถึงรอบตัดแรง',
    items: [0, 50, 100, 200, 300, 500, 800].map(g => item(g <= 0 ? 'ไม่ใช้' : `ห่าง ${g} rpm`, d => { d.SoftGap = g; }, d => near(d.SoftGap, g, 1))) },
];
export const partCurrent = (cat, d) => cat.items.findIndex(i => i.is(d));

// ---------------------------------------------------------------- โมเดลเครื่อง
export const MaxRpm = 16000, RStep = 50, NB = Math.floor(MaxRpm / RStep) + 2;
const lerpT = (t, rpm) => { const x = clamp(rpm, 0, MaxRpm) / RStep; const i = Math.floor(x); return t[i] + (t[i + 1] - t[i]) * (x - i); };
export const PsToHp = 0.98632;
export const Ps = (tq, rpm) => tq * rpm / 7023;
export const Hp = (tq, rpm) => tq * rpm / 7121;

export class EngineModel {
  constructor(d0) {
    const D = this.D = { ...d0 };
    const bore = D.Bore, stroke = D.Stroke;
    this.Cc = PI / 4 * bore * bore * stroke / 1000;
    const Cc = this.Cc;
    this.RodRatio = D.Rod / stroke;
    const durAvg = 0.5 * (D.Duration + D.DurationEx);
    this.Overlap = Math.max(0, durAvg - 2 * D.Lsa);
    this.RpmCam = 6300 + (durAvg - 220) * 82 + (D.Lsa - 108) * 60;
    const lsaN = D.Lsa / 108;
    const wLow = this.RpmCam * 0.55 * Math.pow(lsaN, 1.5), wHigh = this.RpmCam * 0.42 * lsaN;
    const peakBoost = 1 + (108 - D.Lsa) * 0.006;

    const nIn = D.Valves === 4 ? 2 : 1;
    const vd = bore * (D.Valves === 4 ? 0.40 : 0.52);
    const area = nIn * PI / 4 * vd * vd;
    const curtain = nIn * PI * vd * D.Lift;
    const valveEff = 0.6 * Math.min(area, curtain);
    const tbEff = 0.85 * PI / 4 * D.Throttle * D.Throttle;
    const aEff = 1 / Math.sqrt(1 / (valveEff * valveEff) + 1 / (tbEff * tbEff));
    this.RpmFlow = 4770 * aEff / Cc;
    const exDiff = D.DurationEx - D.Duration;
    const exLift = D.LiftEx / D.Lift;
    this.RpmFlow *= exLift < 1 ? Math.max(0.6, 1 - 0.8 * (1 - exLift)) : 1 + 0.15 * Math.min(exLift - 1, 0.4);
    this.RpmFlow *= exDiff < 0 ? Math.max(0.6, 1 + 0.012 * exDiff) : 1 + 0.0025 * Math.min(exDiff, 40);

    const ld = D.Lift / vd;
    const liftF = Math.sqrt(Math.min(1, ld / 0.28));
    const camPower = clamp((durAvg - 225) / 65, 0, 1);
    const valveGain = D.Valves === 4 ? 1.10 + 0.12 * camPower : 1;
    const camGain = 1 + 0.07 * clamp((durAvg - 250) / 50, 0, 1);
    const vePeak = 0.92 * liftF * valveGain * camGain * peakBoost;
    this.FloatRpm = 14200 - 400 * (Math.max(D.Lift, D.LiftEx) - 6) + (D.Valves === 4 ? 2500 : 0) + (durAvg - 220) * 6;

    const octane = fuelOf(D).ron, fuelPower = fuelOf(D).power;
    this.CrLimit = 12.6 - 0.05 * (bore - 50) + 0.30 * (octane - 95);
    const sealPow = Math.pow(D.Seal, 1 / 1.15);
    this.CrDyn = crDynamic(D) * sealPow;
    this.Psi = psiOf(D);
    this.CrEff = D.Cr * sealPow;
    this.SealGain = D.Seal < 1 ? Math.max(0.3, 1 - 0.8 * (1 - D.Seal)) : 1 + 0.2 * (D.Seal - 1);
    this.CrAmp = clamp(0.90 + 0.035 * (this.CrEff - 9.5), 0.75, 1.35);
    this.LowCrMisfire = clamp((8.8 - this.CrEff) * 0.12, 0, 0.3);
    this.PitchStretch = clamp(Math.pow(Cc / 196, 0.25), 0.8, 1.5);
    const bright = 0.07 * D.Cr + 0.6 * (bore / stroke) + 0.0018 * durAvg + (D.Valves === 4 ? 0.12 : 0) - 0.25 * (this.RodRatio - 1.55);
    const brightRef = 0.07 * 11.5 + 0.6 * (67 / 55.6) + 0.0018 * 290 - 0.25 * (89 / 55.6 - 1.55);
    const pipeBr = [0, 0.25, 0.5, 0.7][D.Pipe] || 0;
    this.SoundBright = clamp((bright - brightRef) * 1.5 + 0.3 + pipeBr, -0.9, 1.7);
    const eff = 1 - Math.pow(D.Cr, -0.3);

    const rodF = clamp(1 + 0.25 * (1.65 - this.RodRatio), 0.85, 1.3);
    this.FrictionScale = Math.pow(Cc / 150, 0.6) * (D.Valves === 4 ? 1.08 : 1) * rodF;
    // ความเฉื่อยหมุน (kg·m²): อ้างอิงเวฟ (ข้อ 2.8 กก. + จานไฟ 0.6 กก.) = 0.0145  ข้อ/จานไฟหนักขึ้นเร่งรอบช้าลงชัดเจน เบาลงเร่งไวแต่เดินเบาไม่นิ่ง
    this.Inertia = Math.max(0.006, 0.0145 + (D.CrankKg - 2.8) * 0.0026 + (D.FlywheelKg - 0.6) * 0.0065);
    this.IdleSteady = clamp(0.0144 / this.Inertia, 0.55, 1.8);
    this.IdleRpm = 1400 + Math.max(0, this.Overlap - 10) * 10;
    this.AirK = 40000 * (D.Throttle / 28) ** 2 * (Cc / 110);
    this.Lope = clamp((this.Overlap - 20) / 70 + (D.Pipe === 3 ? 0.12 : 0), 0, 1);
    this.Hunt = clamp((durAvg - 245) / 45, 0, 1);
    this.RpmPipe = this.RpmCam * 1.04;
    const pipeLoud = [1, 1.25, 1.55, 1.7][D.Pipe] || 1;
    this.Loudness = Math.min(2.0, Math.pow(Cc / 196, 0.45) * (0.85 + 0.15 * D.Throttle / 34) * pipeLoud);

    this._veA = new Float64Array(NB);
    this._veN = new Float64Array(NB); this._tInd = new Float64Array(NB); this._fric = new Float64Array(NB);
    const dispL = Cc / 1000;
    let peakPs = 0, peakPsRpm = 0, peakTq = 0, peakTqRpm = 0;
    for (let i = 0; i < NB; i++) {
      const rpm = i * RStep;
      const dw = (rpm - this.RpmCam) / (rpm < this.RpmCam ? wLow : wHigh);
      let ve = vePeak * (0.55 + 0.45 * Math.exp(-dw * dw));
      ve /= Math.pow(1 + Math.pow(rpm / this.RpmFlow, 4), 0.25);
      if (rpm > this.FloatRpm) { const f = (rpm - this.FloatRpm) / 900; ve *= Math.exp(-f * f); }
      ve *= this._pipeGain(rpm);
      this._veN[i] = ve; this._veA[i] = ve;
      this._tInd[i] = ve * dispL * 245 * eff * fuelPower * this.SealGain;
      const vp = 2 * stroke * rpm / 60000;
      const vpPen = vp > 24 ? 0.35 * (vp - 24) ** 2 * this.FrictionScale : 0;
      this._fric[i] = (0.35 + 0.00018 * rpm + 1.0e-8 * rpm * rpm) * this.FrictionScale + vpPen;
    }
    let veMax = 1e-6;
    for (let i = 0; i < NB; i++) veMax = Math.max(veMax, this._veN[i]);
    for (let i = 0; i < NB; i++) this._veN[i] /= veMax;
    let maxFuelGps = 0;       // หัวฉีดตามรถ
    for (let i = 0; i < NB; i++) {
      const rpm = i * RStep; if (rpm < 1500 || rpm > D.Limiter) continue;
      maxFuelGps = Math.max(maxFuelGps, this._veA[i] * dispL * 1.18 * rpm / 120 * this.Load(1, rpm) / (this.Stoich * 0.88));
    }
    this._injAuto = Math.max(10, Math.ceil(maxFuelGps / FuelDens[this._fi()] * 60 / 0.78 / 5) * 5);
    for (let i = 0; i < NB; i++) {
      const rpm = i * RStep;
      if (rpm >= 2000 && rpm <= D.Limiter) {
        const tq = this.BrakeTorqueWot(rpm), ps = tq * rpm / 7023;
        if (tq > peakTq) { peakTq = tq; peakTqRpm = rpm; }
        if (ps > peakPs) { peakPs = ps; peakPsRpm = rpm; }
      }
    }
    this.PeakPs = peakPs; this.PeakPsRpm = peakPsRpm; this.PeakTq = peakTq; this.PeakTqRpm = peakTqRpm;
    this.PeakHp = peakPs * PsToHp;
  }
  _pipeGain(rpm) {
    switch (this.D.Pipe) {
      case 1: return 0.99 + 0.04 * clamp((rpm - 3500) / 5000, 0, 1);
      case 2: return 0.96 + 0.09 * clamp((rpm - 4000) / 5000, 0, 1);
      case 3: { const dw = (rpm - this.RpmPipe) / (0.28 * this.RpmPipe); return 0.89 + 0.21 * Math.exp(-dw * dw); }
      default: return 1;
    }
  }
  TorqueInd(rpm) { return lerpT(this._tInd, rpm) * this.D.CalK; }
  Friction(rpm) { return lerpT(this._fric, rpm); }
  /** แรงฝืดจากการอัด (เบรกเครื่อง) : แรงดันอัดสูง/CR สูง ต้านตอนปิดคันเร่งมาก รอบตกเร็วขึ้น  อ้างอิง ~190 psi = 1.0 */
  get CompDrag() { return Math.pow(clamp(this.Psi / 190, 0.5, 2.2), 1.4); }
  PumpLoss(rpm, load) { return (1 - load) * (0.30 + 0.00018 * rpm) * this.FrictionScale * this.CompDrag; }
  /** เวลา (วิ) ที่เครื่องเปล่าเร่งเปิดคันเร่งสุดจากรอบ a ไป b */
  RevUpTime(a, b) { let t = 0; for (let r = a; r < b; r += 100) { const tq = this.BrakeTorqueWot(r + 50); if (tq < 0.05) return Infinity; t += this.Inertia * (100 * 2 * Math.PI / 60) / tq; } return t; }
  /** เวลา (วิ) ที่รอบตกจาก a ลง b ตอนปล่อยคันเร่ง */
  CoastTime(a, b) { let t = 0; for (let r = a; r > b; r -= 100) { const mid = r - 50, tq = this.Friction(mid) + this.PumpLoss(mid, 0.05); t += this.Inertia * (100 * 2 * Math.PI / 60) / Math.max(tq, 0.05); } return t; }
  NaturalLoad(thr, rpm) { return 1 - Math.exp(-thr * this.AirK / Math.max(rpm, 400)); }
  /** ปริมาณอากาศต่อรอบ (0..1)  ถ้าแก้ตาราง MAP แล้ว = ตาม MAP เป้าหมายของช่อง (TPS x รอบ) แต่ไม่เกินที่ลิ้นเปิดสุดจะผ่านได้ */
  Load(thr, rpm) {
    const nat = this.NaturalLoad(thr, rpm), t = this.D.EcuMap;
    if (!t) return nat;
    const map = this._samp(t, (rpm - 1000) / (this.EcuRpmMax - 1000) * (EcuN - 1), EngineModel.TpsToIndex(thr));
    return Math.min(clamp((map / 101.3 - 0.28) / 0.72, 0, 1), this.NaturalLoad(1, rpm));
  }
  VeNorm(rpm) { return lerpT(this._veN, rpm); }
  Knock(rpm, load, shift = 0) {
    const k = (this.CrDyn - this.CrLimit - shift) / 1.5;
    if (k <= 0) return 0;
    return clamp(k, 0, 1) * load * load * clamp(1.4 - rpm / 8000, 0.3, 1);
  }
  BrakeTorqueWot(rpm) {
    const load = this.Load(1, rpm), e = this.EcuAt(rpm, 1), kn = this.Knock(rpm, load, e.KnockShift);
    return this.TorqueInd(rpm) * load * e.Mult * (1 - 0.5 * kn) - this.Friction(rpm);
  }
  KnockWot(rpm) { const load = this.Load(1, rpm); return this.Knock(rpm, load, this.EcuAt(rpm, 1).KnockShift); }

  // ================================================================ ตารางจูนกล่อง (ECU): น้ำมัน / องศาจุดระเบิด / AFR
  get Stoich() { return StoichTab[this._fi()]; }
  _fi() { return clamp(this.D.Fuel | 0, 0, StoichTab.length - 1); }
  /** หัวฉีดตามรถ: เปิดราว 78% ที่อากาศไหลสูงสุด (หลักการเลือกหัวฉีดจริง เผื่อไว้ ~20%) */
  get InjCapCc() { return this.D.Inj > 0 ? this.D.Inj : this._injAuto; }
  get InjFlowGpMs() { return this.InjCapCc * FuelDens[this._fi()] / 60000; }
  get FuelDensity() { return FuelDens[this._fi()]; }
  VeAbs(rpm) { return lerpT(this._veA, rpm); }
  AirPerCycleG(rpm, load) { return lerpT(this._veA, rpm) * (this.Cc / 1000) * 1.18 * load; }
  /** เวลาฉีดโรงงาน (ms) = dead time + น้ำมันตามแมพโรงงาน / อัตราไหลหัวฉีด */
  FactoryMs(rpm, load) { return InjDeadMs + this.AirPerCycleG(rpm, load) / (this.Stoich * this.LambdaBase(rpm, load)) / this.InjFlowGpMs; }
  MsForAfr(rpm, load, afr) { return InjDeadMs + this.AirPerCycleG(rpm, load) / Math.max(afr, 1) / this.InjFlowGpMs; }
  get EcuRpmMax() { return Math.max(6000, Math.ceil(this.D.Limiter / 500) * 500); }
  EcuRpmAt(i) { return 1000 + (this.EcuRpmMax - 1000) * i / (EcuN - 1); }
  LambdaBase(rpm, load) {
    const hi = clamp((load - 0.55) / 0.45, 0, 1);
    return 1 - 0.12 * hi - 0.02 * hi * clamp((rpm / this.D.Limiter - 0.7) / 0.3, 0, 1);
  }
  Mbt(rpm, load, lam) {
    let m = 10 + 2.1 * Math.min(rpm, 11000) / 1000;
    m += 14 * Math.pow(1 - clamp(load, 0, 1), 1.3);
    m += 0.25 * (this.D.Bore - 55) - 1.0 * (this.CrEff - 10) - 6 * (this.RodRatio - 1.7) + 0.04 * this.Overlap + BurnK[this._fi()];
    m += clamp(12 * (lam - 0.9), -1.5, 6);
    return clamp(m, 5, 45);
  }
  StockAdv(rpm, load) { return Math.max(2, this.Mbt(rpm, load, this.LambdaBase(rpm, load)) - 4); }
  _samp(t, x, y) {
    x = clamp(x, 0, EcuN - 1); y = clamp(y, 0, EcuN - 1);
    const x0 = Math.floor(x), y0 = Math.floor(y), x1 = Math.min(x0 + 1, EcuN - 1), y1 = Math.min(y0 + 1, EcuN - 1), fx = x - x0, fy = y - y0;
    const a = t[y0 * EcuN + x0] + (t[y0 * EcuN + x1] - t[y0 * EcuN + x0]) * fx, b = t[y1 * EcuN + x0] + (t[y1 * EcuN + x1] - t[y1 * EcuN + x0]) * fx;
    return a + (b - a) * fy;
  }
  /** โหลดอากาศจากตำแหน่งลิ้นเร่ง (TPS 0..1) ที่รอบนั้น */
  _loadOf(tps, rpm) { return clamp(this.Load(tps, rpm), 0.02, 1); }
  TableMs(rpm, tps) { const t = this.D.EcuMs; return t ? this._samp(t, (rpm - 1000) / (this.EcuRpmMax - 1000) * (EcuN - 1), EngineModel.TpsToIndex(tps)) : this.FactoryMs(rpm, this._loadOf(tps, rpm)); }
  // ---------------- MAP ----------------
  /** แรงดันท่อไอดี (kPa สัมบูรณ์) จากโหลดอากาศ 0..1 */
  MapKpa(load) { return 101.3 * (0.28 + 0.72 * clamp(load, 0, 1)); }
  MapAtTps(rpm, tps) { return this.MapKpa(this.Load(tps, rpm)); }
  /** แกนคันเร่ง TPS ของตาราง MAP: เริ่มที่ 0% (ลิ้นปิด) ถึง 100% เรียงเท่า ๆ กัน 20 ช่อง */
  static TpsAxis(j) { return TPS_PCT[clamp(j, 0, EcuN - 1)] / 100; }
  /** ตำแหน่งในแกน TPS (ทศนิยม) ของค่า TPS 0..1 : แกนถี่ที่ปลายล่างเพราะเครื่องใหญ่ ลิ้นเปิดนิดเดียวอากาศก็แทบเต็ม */
  static TpsToIndex(tps) {
    const v = clamp(tps, 0, 1) * 100; let i = 0;
    while (i < EcuN - 2 && v > TPS_PCT[i + 1]) i++;
    return i + clamp((v - TPS_PCT[i]) / (TPS_PCT[i + 1] - TPS_PCT[i]), 0, 1);
  }
  // ---------------- องศาจบฉีด (EOI) ----------------
  get IvoBtdc() { return this.D.Duration * 0.5 - this.D.Lsa + 12; }
  FactoryEoi(rpm, load) {
    let e = Math.max(this.IvoBtdc + 25, 330 - 15 * rpm / 1000);
    e = Math.min(e, 700 - pulseDeg(this.FactoryMs(rpm, load), rpm));
    return clamp(Math.round(e / 5) * 5, 60, 540);
  }
  _injF(rpm, eoi, pd) {
    const open = clamp((this.IvoBtdc + 10 - eoi) / Math.max(pd, 20), 0, 1), hi = clamp((rpm - 6000) / 4000, 0, 1);
    return 1 - 0.06 * open * (1 - 0.6 * hi) - 0.04 * clamp((eoi + pd - 720) / 90, 0, 1) - 0.012 * clamp((eoi - 360) / 180, 0, 1);
  }
  TableEoi(rpm, tps) { const t = this.D.EcuEoi; return t ? this._samp(t, (rpm - 1000) / (this.EcuRpmMax - 1000) * (EcuN - 1), EngineModel.TpsToIndex(tps)) : this.FactoryEoi(rpm, this._loadOf(tps, rpm)); }
  // ---------------- ไทม์คอยล์ (dwell) ----------------
  CoilReq(load, lam) { return clamp(0.52 + 0.26 * load + 0.03 * (this.CrEff - 10) + 0.35 * clamp(lam - 1, 0, 0.4), 0.4, 1); }
  FactoryDwell(rpm, load) { const req = this.CoilReq(load, this.LambdaBase(rpm, load)); return Math.round(-DwellTau * Math.log(1 - Math.min(0.96, req * 1.08)) * 20) / 20; }
  TableDwell(rpm, tps) { const t = this.D.EcuDwell; return t ? this._samp(t, (rpm - 1000) / (this.EcuRpmMax - 1000) * (EcuN - 1), EngineModel.TpsToIndex(tps)) : this.FactoryDwell(rpm, this._loadOf(tps, rpm)); }
  CellEoi(c, r) { const t = this.D.EcuEoi; return t ? t[r * EcuN + c] : this.FactoryEoi(this.EcuRpmAt(c), this._loadOf(EngineModel.TpsAxis(r), this.EcuRpmAt(c))); }
  CellDwell(c, r) { const t = this.D.EcuDwell; return t ? t[r * EcuN + c] : this.FactoryDwell(this.EcuRpmAt(c), this._loadOf(EngineModel.TpsAxis(r), this.EcuRpmAt(c))); }
  FactoryEoiTable() { const t = new Array(EcuN * EcuN); for (let r = 0; r < EcuN; r++) for (let c = 0; c < EcuN; c++) t[r * EcuN + c] = this.FactoryEoi(this.EcuRpmAt(c), this._loadOf(EngineModel.TpsAxis(r), this.EcuRpmAt(c))); return t; }
  FactoryDwellTable() { const t = new Array(EcuN * EcuN); for (let r = 0; r < EcuN; r++) for (let c = 0; c < EcuN; c++) t[r * EcuN + c] = this.FactoryDwell(this.EcuRpmAt(c), this._loadOf(EngineModel.TpsAxis(r), this.EcuRpmAt(c))); return t; }
  OpenValveFrac(c, r) { return clamp((this.IvoBtdc + 10 - this.CellEoi(c, r)) / Math.max(pulseDeg(this.CellMs(c, r), this.EcuRpmAt(c)), 20), 0, 1); }
  CoilDuty(c, r) { return this.CellDwell(c, r) / (120000 / Math.max(this.EcuRpmAt(c), 200)); }
  TableAdv(rpm, tps) { const t = this.D.EcuIgn; return t ? this._samp(t, (rpm - 1000) / (this.EcuRpmMax - 1000) * (EcuN - 1), EngineModel.TpsToIndex(tps)) : this.StockAdv(rpm, this._loadOf(tps, rpm)); }
  /** ผลของตารางจูนกล่อง ณ รอบ/โหลดนั้น (คืนออบเจ็กต์ที่ใช้ซ้ำ) */
  EcuAt(rpm, tps, out = this._ecuTmp || (this._ecuTmp = {})) {
    tps = clamp(tps, 0, 1); const load = this._loadOf(tps, rpm);
    const fi = this._fi(), st = StoichTab[fi], lamB = this.LambdaBase(rpm, load), pw = this.TableMs(rpm, tps);
    const air = this.AirPerCycleG(rpm, load), cycleMs = 120000 / Math.max(rpm, 200), duty = pw / cycleMs;
    const fuel = Math.max(0, Math.min(pw, cycleMs) - InjDeadMs) * this.InjFlowGpMs;
    const afr = fuel > 1e-6 ? air / fuel : 99, lam = afr / st;
    const adv = this.TableAdv(rpm, tps), mbt = this.Mbt(rpm, load, lam), dlt = adv - mbt;
    let mult = timingF(dlt) / timingF(-4) * lambdaF(lam) / lambdaF(lamB);
    // ส่วนผสม: หนา (lam<0.8) สะดุด/ติดไม่ครบ  บาง (lam>1.08) แห้ง ๆ กระตุก ติดไม่ครบ  ยิ่งโหลดต่ำยิ่งสังเกตง่าย
    const lowLd = 1 + 0.35 * (1 - load), leanR = clamp((lam - 1.06) / 0.30, 0, 1), richR = clamp((0.80 - lam) / 0.28, 0, 1);
    const mixMf = Math.max(clamp((lam - 1.12) / 0.33, 0, 1), clamp((0.74 - lam) / 0.26, 0, 1)) * 0.95 * Math.min(1, lowLd);
    // บาง/หนาเกิน/ประกายอ่อน/ตัดน้ำมัน: จุดไม่ติดเป็นจังหวะ ๆ (สุ่มรายจังหวะ) ทำให้รอบสะดุด/เดินหอบ แทนการลดแรงบิดเฉย ๆ
    const eoi = this.TableEoi(rpm, tps), fInj = this._injF(rpm, eoi, pulseDeg(pw, rpm)) / this._injF(rpm, this.FactoryEoi(rpm, load), pulseDeg(this.FactoryMs(rpm, load), rpm));
    const dwell = this.TableDwell(rpm, tps), reqA = this.CoilReq(load, lam), reqB = this.CoilReq(load, lamB), spark = sparkQ(dwell, reqA);
    mult *= fInj * dwellF(dwell, rpm, reqA) / dwellF(this.FactoryDwell(rpm, load), rpm, reqB);
    if (fuel <= 1e-6) mult = 0;      // เวลาฉีด 0 (หรือไม่เกิน dead time) = ตัดน้ำมัน ไม่มีการเผาไหม้
    out.Mult = mult; out.KnockShift = 1.8 * (lamB - lam) - 0.11 * (adv - this.StockAdv(rpm, load));
    out.Afr = afr; out.Lambda = lam; out.Adv = adv; out.Mbt = mbt; out.Duty = duty; out.PulseMs = pw; out.Eoi = eoi; out.DwellMs = dwell; out.Spark = spark;
    out.Misfire = fuel <= 1e-6 ? 1 : Math.max(mixMf, clamp((0.8 - spark) / 0.3, 0, 1));
    out.Lean = Math.min(1, leanR * lowLd); out.Rich = Math.min(1, richR * lowLd);      // ความรุนแรงของอาการบาง/หนา 0..1
    out.Egt = 330 + 380 * load + 12 * rpm / 1000 + 300 * clamp((lam - 0.8) / 0.25, 0, 1) - 150 * clamp((lam - 1.1) / 0.3, 0, 1) + 9 * Math.max(0, mbt - adv) + 200 * Math.max(0, 1 - spark);
    return out;
  }
  CellMs(c, r) { const t = this.D.EcuMs; return t ? t[r * EcuN + c] : this.FactoryMs(this.EcuRpmAt(c), this._loadOf(EngineModel.TpsAxis(r), this.EcuRpmAt(c))); }
  FactoryMsTable() { const t = new Array(EcuN * EcuN); for (let r = 0; r < EcuN; r++) for (let c = 0; c < EcuN; c++) t[r * EcuN + c] = Math.round(this.FactoryMs(this.EcuRpmAt(c), this._loadOf(EngineModel.TpsAxis(r), this.EcuRpmAt(c))) * 100) / 100; return t; }
  CellMap(c, r) { const t = this.D.EcuMap; return t ? t[r * EcuN + c] : Math.round(this.MapKpa(this.NaturalLoad(EngineModel.TpsAxis(r), this.EcuRpmAt(c)))); }
  FactoryMapTable() { const t = new Array(EcuN * EcuN); for (let r = 0; r < EcuN; r++) for (let c = 0; c < EcuN; c++) t[r * EcuN + c] = Math.round(this.MapKpa(this.NaturalLoad(EngineModel.TpsAxis(r), this.EcuRpmAt(c)))); return t; }
  CellAdv(c, r) { const t = this.D.EcuIgn; return t ? t[r * EcuN + c] : this.StockAdv(this.EcuRpmAt(c), this._loadOf(EngineModel.TpsAxis(r), this.EcuRpmAt(c))); }
  FactoryIgnTable() { const t = new Array(EcuN * EcuN); for (let r = 0; r < EcuN; r++) for (let c = 0; c < EcuN; c++) t[r * EcuN + c] = Math.round(this.StockAdv(this.EcuRpmAt(c), this._loadOf(EngineModel.TpsAxis(r), this.EcuRpmAt(c))) * 2) / 2; return t; }
  PistonSpeed(rpm) { return 2 * this.D.Stroke * rpm / 60000; }
}

/** หาตัวคูณปรับเทียบให้พีคตรงรถจริง (แรงม้าก่อน ไม่มีก็แรงบิด) null = ไม่ได้ใส่ค่ารถจริง */
export function calibrate(d) {
  if (d.RealHp <= 0 && d.RealTq <= 0) return null;
  const t = { ...d, CalK: 1 };
  for (let i = 0; i < 8; i++) {
    const m = new EngineModel(t);
    const have = d.RealHp > 0 ? m.PeakHp : m.PeakTq, want = d.RealHp > 0 ? d.RealHp : d.RealTq;
    if (have < 0.01) return null;
    t.CalK = clamp(t.CalK * want / have, 0.3, 3);
  }
  return t.CalK;
}

// ---------------------------------------------------------------- ตัวรถ
/** พื้นถนน: ตัวคูณแรงเกาะ (μ) และแรงต้านการกลิ้ง */
export const Surfaces = [
  { name: 'ถนนทั่วไป', muK: 1.0, crrK: 1.0, note: 'ลาดยางทั่วไป แห้ง ค่าอ้างอิง' },
  { name: 'ราดยาง', muK: 0.9, crrK: 1.25, note: 'ผิวหยาบ/หินเกล็ดราดยาง เกาะน้อยลง ต้านการกลิ้งมาก' },
  { name: 'คอนกรีต', muK: 1.05, crrK: 0.92, note: 'ผิวแข็งเรียบ เกาะดี ต้านการกลิ้งน้อย' },
  { name: 'แทร็ก', muK: 1.25, crrK: 0.85, note: 'ผิวแข่ง/สนามดรักเรียบเหนียว เกาะสูงสุด ต้านต่ำสุด' },
];
export const Veh = {
  surface: 0,
  setSurface(i) { Veh.surface = clamp(Math.round(i), 0, Surfaces.length - 1); },
  DriveEff: 0.93, Crr: 0.012, AirRho: 1.18, Mu: 0.95, CgRear: 0.5, CgHeight: 0.4, G: 9.81, BrakeDecel: 7.0, WheelInertia: 0.9,
  mass: d => d.BikeKg + d.RiderKg,
  wheelRadius: d => (d.RimInch * 12.7 + d.TireW * d.TireAsp / 100) * 0.992 / 1000,
  idealRim: tw => 0.000125 * tw * tw + 0.0075 * tw,
  rimFit(d) { const ideal = Veh.idealRim(d.TireW); const m = Math.max(0, Math.abs(d.RimW - ideal) - 0.2) / ideal; return clamp(1 - 0.5 * m, 0.7, 1); },
  muFor(d) { return Veh.Mu * Surfaces[Veh.surface].muK * Math.pow(d.TireW / 90, 0.3) * Veh.rimFit(d); },
  crrFor(d) { return Veh.Crr * Surfaces[Veh.surface].crrK * Math.pow(d.TireW / 90, 0.25) / (0.5 + 0.5 * Veh.rimFit(d)); },
  wheelInertiaFor(d) { const r = Veh.wheelRadius(d) / 0.285; return Veh.WheelInertia * r * r * (0.7 + 0.3 * d.TireW / 80) * (0.85 + 0.15 * d.RimW / 1.4); },
  effMass(d) { const r = Veh.wheelRadius(d); return Veh.mass(d) + Veh.wheelInertiaFor(d) / (r * r); },
  tractionMax(d) { const mu = Veh.muFor(d); return mu * Veh.mass(d) * Veh.G * Veh.CgRear / (1 - mu * Veh.CgHeight); },
  final: d => d.SprocketR / d.SprocketF,
  gearOf: (d, g) => gearAt(d, g),
  total: (d, g) => (g <= 0 ? 0 : d.Primary * Veh.gearOf(d, g) * Veh.final(d)),
  resist(d, v, slopePct) {
    const m = Veh.mass(d), s = slopePct / 100, sin = s / Math.sqrt(1 + s * s), cos = 1 / Math.sqrt(1 + s * s);
    return Veh.crrFor(d) * m * Veh.G * cos + 0.5 * Veh.AirRho * d.CdA * v * Math.abs(v) + m * Veh.G * sin;
  },
  topSpeed(m, gear, slope = 0) {
    const d = m.D, ratio = Veh.total(d, gear), r = Veh.wheelRadius(d);
    let best = 0, bestRpm = 0;
    for (let v = 1; v < 80; v += 0.1) {
      const rpm = v / r * ratio * 9.5493;
      if (rpm > d.Limiter) break;
      if (rpm < 2500) continue;
      const fav = Math.max(0, m.BrakeTorqueWot(rpm)) * ratio * Veh.DriveEff / r;
      if (fav >= Veh.resist(d, v, slope)) { best = v; bestRpm = rpm; }
    }
    return { kmh: best * 3.6, rpm: bestRpm };
  },
  topSpeedAuto(m, slope = 0) {
    const d = m.D, r = Veh.wheelRadius(d), hold = Cvt.holdRpm(m);
    let best = 0, bestRpm = 0;
    for (let v = 1; v < 80; v += 0.1) {
      const rpmHi = v / r * Cvt.ratio(1) * Cvt.final(d) * 9.5493;
      const rpm = Math.max(hold, rpmHi);
      if (rpm > d.Limiter) break;
      const ratio = rpm / 9.5493 * r / v;
      const fav = Math.max(0, m.BrakeTorqueWot(rpm)) * ratio * Veh.DriveEff / r;
      if (fav >= Veh.resist(d, v, slope)) { best = v; bestRpm = rpm; }
    }
    return { kmh: best * 3.6, rpm: bestRpm };
  },
};

export const Cvt = {
  RLow: 2.5, RHigh: 0.77, R0: 0.038, R1: 0.044, FBase: 1000, SpringPreloadMm: 25, SpringTravelMm: 35, KCam: 250, FScale: 1500, Rate: 1.5,
  ratio: s => Cvt.RLow * Math.pow(Cvt.RHigh / Cvt.RLow, clamp(s, 0, 1)),
  mass(d) { const n = d.Rollers, a = Math.floor((n + 1) / 2), b = Math.floor(n / 2); return (a * d.RollerG + b * d.RollerG2) / 1000; },
  ff(d, omega, s) { const ang = d.RampDeg + (d.RampOutDeg - d.RampDeg) * clamp(s, 0, 1), tanA = Math.tan(ang * PI / 180); return Cvt.mass(d) * omega * omega * (Cvt.R0 + (Cvt.R1 - Cvt.R0) * s) / tanA; },
  final: d => d.FirstRed * d.GearR / d.GearF,
  fr: (d, torque, s) => Cvt.FBase + d.SpringK * (Cvt.SpringPreloadMm + Cvt.SpringTravelMm * s) + Cvt.KCam * Math.max(0, torque),
  holdRpm(m, d = m.D) {
    for (let rpm = 2500; rpm < MaxRpm; rpm += 50) {
      const omega = rpm / 9.5493, t = Math.max(0, m.BrakeTorqueWot(rpm));
      if (Cvt.ff(d, omega, 0.5) >= Cvt.fr(d, t, 0.5)) return rpm;
    }
    return MaxRpm;
  },
  suggestRoller(m) {
    const target = m.PeakPsRpm * 0.97, d = { ...m.D };
    let bestG = m.D.RollerG, bestHold = Cvt.holdRpm(m), bestErr = Infinity;
    for (let g = 4; g <= 20; g += 0.1) {
      d.RollerG = g; d.RollerG2 = g;
      const hold = Cvt.holdRpm(m, d), err = Math.abs(hold - target);
      if (err < bestErr) { bestErr = err; bestG = g; bestHold = hold; }
    }
    return { g: Math.round(bestG * 2) / 2, hold: bestHold };
  },
};
