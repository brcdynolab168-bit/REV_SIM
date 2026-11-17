namespace RevGame;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // วาดที่ความละเอียดจริงของจอ (UI ปรับสเกลเองจากพื้นที่เสมือน 1920x1080)
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
