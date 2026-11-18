namespace RevGame;

public sealed partial class MainForm
{
    // ====================================================================
    //  ภาพรวม: ใบสเปกเครื่องที่ติดตั้ง + สถิติ
    // ====================================================================

    private void DrawMenu(Graphics g)
    {
        DrawTopBar(g, "ภาพรวม", "Enter ไปทดสอบ  •  G อู่ออกแบบ  •  F11 เต็มจอ/หน้าต่าง  •  + / − ระดับเสียง");

        var m = _eng.Model;
        var d = m.D;

        // ใบสเปก
        var sheet = new RectangleF(252, 124, 1000, 800);
        PanelBox(g, sheet, "ใบสเปกเครื่อง (ที่ติดตั้งอยู่)");
        float y = 176;
        void Sec(string name)
        {
            y += 8;
            Txt(g, name, _fCap, Palette.Accent, sheet.X + 28, y);
            g.DrawLine(Pn(Palette.Border, 1f), sheet.X + 28 + 250, y + 11, sheet.Right - 28, y + 11);
            y += 28;
        }
        void Row(string k, string v)
        {
            Txt(g, k, _fSm, Palette.Dim, sheet.X + 36, y);
            TxtR(g, v, _fNumS, Palette.Text, sheet.Right - 36, y);
            y += 31;
        }

        string fuel = EngineDesign.Fuels[Math.Clamp(d.Fuel, 0, EngineDesign.Fuels.Length - 1)].Name;
        Sec("BLOCK");
        Row("ความจุ", $"{m.Cc:0.0} cc");
        Row("ลูก × ชัก", $"{d.Bore:0.0} × {d.Stroke:0.0} มม.   (ก้าน {d.Rod:0} มม.)");
        Row("ล้อ / ยาง", $"{d.TireCode}  ขอบ {d.RimW:0.00}");
        Row("ข้อเหวี่ยง / จานไฟ", $"{d.CrankKg:0.0} / {d.FlywheelKg:0.00} กก.");
        Sec("HEAD / CAM");
        Row("วาล์ว / เรือน", $"{d.Valves} วาล์ว / {d.Throttle:0} มม.");
        Row("แคม LSA / องศา IN,EX / ยก IN,EX", $"{d.Lsa:0}° / {d.Duration:0}°,{d.DurationEx:0}° / {d.Lift:0.0},{d.LiftEx:0.0} มม.");
        Sec("COMPRESSION / FUEL");
        Row("อัตราส่วนกำลังอัด", $"{d.Cr:0.0} : 1");
        Row("แรงดันอัด (วัดจริง)", $"{m.Psi:0} psi");
        Row("น้ำมัน", $"{fuel}  (RON {d.Octane:0})");
        Row("รอบตัด", $"{d.Limiter:N0} rpm");
        Sec("OUTPUT");
        Row("กำลังสูงสุด", $"{m.PeakHp:0.0} HP @ {m.PeakPsRpm:N0} rpm");
        Row("แรงบิดสูงสุด", $"{m.PeakTq:0.0} N·m @ {m.PeakTqRpm:N0} rpm");
        Sec("DRIVETRAIN");
        if (d.Auto == 1)
        {
            Row("ระบบ", "ออโต้ CVT");
            Row("ชาม ใน/นอก / เม็ด / สปริง", $"{d.RampDeg:0.0}°/{d.RampOutDeg:0.0}° / {d.Rollers} × {d.RollerG:0.#} g / {d.SpringK:0} N/มม.");
            Row("เฟืองท้าย", $"{d.GearF}/{d.GearR}  (ทดรวม {Cvt.Final(d):0.00})");
        }
        else
        {
            Row("ระบบ", "เกียร์ธรรมดา 4 สปีด");
            Row("สเตอร์ หน้า/หลัง", $"{d.SprocketF:0}/{d.SprocketR:0}  (ทดสุดท้าย {Vehicle.Final(d):0.000})");
        }

        // สถิติ
        var rec = new RectangleF(1280, 124, 600, 400);
        PanelBox(g, rec, "สถิติบนถนน");
        string R(float v) => v < 0f ? "-" : v.ToString("0.00") + " วิ";
        float ry = 184;
        void Rec(string k, string v)
        {
            Txt(g, k, _fSm, Palette.Dim, rec.X + 28, ry + 4);
            TxtR(g, v, _fNum, Palette.Accent, rec.Right - 28, ry);
            ry += 54;
        }
        Rec("0–60 กม./ชม.", R(_save.Best060));
        Rec("100 เมตร", R(_save.Best100));
        Rec("402 เมตร", R(_save.Best402));
        Rec("ท็อปสปีด", _save.BestTop <= 0f ? "-" : $"{_save.BestTop:0} กม./ชม.");

        Btn(g, new RectangleF(1280, 556, 600, 88), "ไปห้องทดสอบ", false, Palette.Green, () => GoTo(Page.Drive), _fLg);
        Btn(g, new RectangleF(1280, 660, 600, 88), "อู่ออกแบบเครื่อง", false, Palette.Accent, () => GoTo(Page.Garage), _fLg);
        Btn(g, new RectangleF(1280, 764, 600, 70), "ตั้งค่า", false, Palette.Blue, () => GoTo(Page.Settings), _fMd);
    }

    // ====================================================================
    //  ตั้งค่า
    // ====================================================================

    private void DrawSettings(Graphics g)
    {
        DrawTopBar(g, "ตั้งค่า", "Esc กลับ  •  F11 เต็มจอ/หน้าต่าง");

        var box = new RectangleF(252, 124, 1100, 560);
        PanelBox(g, box, "ระบบ");

        float x = box.X + 40, w = box.Width - 80, y = box.Y + 80;

        Txt(g, "ระดับเสียง", _fMd, Palette.Text, x, y);
        TxtR(g, $"{_eng.Volume * 100f:0}%", _fNum, Palette.Accent, x + w, y);
        Slider(g, new RectangleF(x, y + 48, w, 36), _eng.Volume, Palette.Accent, v => _eng.Volume = v);

        y += 150;
        Txt(g, "การแสดงผล", _fMd, Palette.Text, x, y);
        Btn(g, new RectangleF(x + w - 520, y - 10, 520, 60), _fullscreen ? "เต็มจอ (กดเพื่อเป็นหน้าต่าง)" : "หน้าต่าง (กดเพื่อเต็มจอ)",
            false, Palette.Blue, () => ApplyWindowMode(!_fullscreen), _fSm);

        y += 90;
        Txt(g, "แสดงเฟรมเรต (FPS)", _fMd, Palette.Text, x, y);
        Btn(g, new RectangleF(x + w - 260, y - 10, 260, 60), _showFps ? "เปิด" : "ปิด", _showFps, Palette.Green, () => _showFps = !_showFps, _fSm);

        y += 90;
        Txt(g, "ล้างสถิติบนถนน", _fMd, Palette.Text, x, y);
        Btn(g, new RectangleF(x + w - 260, y - 10, 260, 60), "ล้างสถิติ", false, Palette.Red, () =>
        {
            _save.Best060 = _save.Best100 = _save.Best402 = -1f;
            _save.BestTop = 0f;
            _last060 = _last100 = _last402 = -1f;
            _topSession = 0f;
            _cur = new RunStats();
            _before = null;
            _save.Save();
            Toast("ล้างสถิติแล้ว");
        }, _fSm);
    }
}
