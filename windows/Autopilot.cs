namespace RevGame;

/// <summary>
/// ขับเอง: ระบบสตาร์ท เข้าเกียร์ ออกตัว เร่งสุด ไล่เกียร์ตามรอบ (กำครัตช์ให้เองตอนเปลี่ยน) วิ่งครบ 500 ม. แล้วเบรกจนหยุด พักแล้ววิ่งซ้ำ
/// ผลวัดเวลา 0-60 / 100 ม. / 402 ม. / ท็อปสปีด เข้าตารางสถิติให้เอง ใช้เทียบ ก่อน/หลัง แก้เครื่องโดยไม่ต้องขับเอง
/// </summary>
public sealed partial class MainForm
{
    private bool _autoDrive;
    private enum Ap { Start, Ready, Launch, Run, Brake, Wait }
    private Ap _apState = Ap.Start;
    private double _apT, _apClutchUntil, _apShiftAt, _apGearAt;
    private float _apDist0;
    private int _apRuns, _apGearPending = -1;

    private void ToggleAutopilot()
    {
        _autoDrive = !_autoDrive;
        _apState = Ap.Start; _apRuns = 0; _apGearPending = -1;
        Toast(_autoDrive ? "ขับเอง: เปิด (สตาร์ท ออกตัว ไล่เกียร์ เบรก วิ่งซ้ำให้เอง)  กด P อีกครั้งเพื่อหยุด" : "ขับเอง: ปิด");
    }

    private void ApShiftTo(int g, double now)
    {
        if (_eng.IsAuto || g == _gearSel) return;
        if (_eng.ClutchEnabled) { _apClutchUntil = now + 0.45; _apGearPending = g; _apGearAt = now + 0.12; }
        else _gearSel = g;
    }

    private void AutopilotStep(float dt, ref float thr, ref float brake, ref bool starter)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_apGearPending >= 0 && now >= _apGearAt) { _gearSel = _apGearPending; _apGearPending = -1; }
        thr = 0f; brake = 0f; starter = false;
        var m = _eng.Model;
        float kmh = _eng.SpeedKmh;
        switch (_apState)
        {
            case Ap.Start:
                _eng.Ignition = true;
                if (!_eng.IsAuto && _gearSel != 0) { _gearSel = 0; }
                if (!_eng.Running) starter = true;
                else { _apState = Ap.Ready; _apT = now; }
                break;
            case Ap.Ready:
                if (!_eng.Running) { _apState = Ap.Start; break; }
                if (kmh > 1f) { _apState = Ap.Brake; break; }
                if (now - _apT > 0.8)
                {
                    ApShiftTo(1, now);
                    _apDist0 = _eng.DistanceM; _apT = now; _apShiftAt = now;
                    _apState = Ap.Launch;
                }
                break;
            case Ap.Launch:
                thr = 1f;
                if (now - _apT > 0.5) _apState = Ap.Run;
                break;
            case Ap.Run:
            {
                thr = 1f;
                float shiftRpm = MathF.Min(_eng.LimiterRpm - 250f, m.PeakPsRpm * 1.05f);
                if (!_eng.IsAuto && _gearSel < 4 && _apGearPending < 0 && now - _apShiftAt > 0.7 && _eng.Rpm >= shiftRpm)
                {
                    ApShiftTo(_gearSel + 1, now); _apShiftAt = now;
                }
                if (_eng.DistanceM - _apDist0 >= 520f || now - _apT > 40.0) { _apState = Ap.Brake; _apT = now; }
                break;
            }
            case Ap.Brake:
                brake = 1f;
                if (kmh < 1f) { _apRuns++; _apState = Ap.Wait; _apT = now; if (!_eng.IsAuto) ApShiftTo(0, now); }
                break;
            case Ap.Wait:
                if (now - _apT > 2.5) { _apState = Ap.Ready; _apT = now; }
                break;
        }
    }

    private string AutopilotText() => _apState switch
    {
        Ap.Start => "กำลังสตาร์ท…",
        Ap.Ready => "เตรียมออกตัว…",
        Ap.Launch or Ap.Run => "กำลังวิ่ง…",
        Ap.Brake => "เบรกจนหยุด…",
        _ => "พักก่อนวิ่งซ้ำ…",
    } + $"  (วิ่งแล้ว {_apRuns} ครั้ง)";
}
