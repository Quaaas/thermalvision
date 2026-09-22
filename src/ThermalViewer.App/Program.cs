using Avalonia;

namespace ThermalViewer.App;

internal static class Program
{
    // Avalonia needs a synchronous Main method with no [STAThread] requirements beyond
    // this, so it starts unmodified on both Linux and Windows.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
