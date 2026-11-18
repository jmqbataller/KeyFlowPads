namespace KeyFlowPads.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var artwork = new Theme();
        using var player = new PlayerWindow(artwork);
        Application.Run(player);
    }
}
