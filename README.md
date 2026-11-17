# Rev Lab — ห้องทดลองเครื่องยนต์ 1 สูบ 4 จังหวะ

ออกแบบเครื่อง (ลูก/ชัก/แคม IN-EX/ยก/CR/แรงดันอัด/จานไฟ/ท่อ/น้ำมัน/อัตราทด/ล้อ-ยาง) แล้วสตาร์ท วัดไดโน วิ่ง 0–60 / 100 ม. / 402 ม. ฟังเสียงเครื่อง และให้ที่ปรึกษาช่วยจูน

| โฟลเดอร์ | |
|---|---|
| `docs/` | **เวอร์ชันเว็บ/มือถือ** (HTML + JavaScript) เปิดผ่าน GitHub Pages |
| `windows/` | เวอร์ชัน Windows (C# / WinForms, .NET 8) |

## เวอร์ชันเว็บ
Settings → Pages → Deploy from a branch → `main` / `/docs` แล้วแตะ "แตะเพื่อเริ่ม" เพื่อเปิดเสียง
ลองในเครื่อง: `cd docs && npx serve .`

## เวอร์ชัน Windows
```bash
cd windows
dotnet run
```
ต้องมี .NET 8 SDK  ข้อมูลเซฟอยู่ที่ `%LocalAppData%\RevGame110\save.txt`
