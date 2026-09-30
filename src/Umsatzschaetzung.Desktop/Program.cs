using Avalonia;

namespace Umsatzschaetzung.Desktop;

public static class Program
{
    [STAThread]
    static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // The Bericht is previewed and printed in the platform's own web view.
    public static AppBuilder BuildAvaloniaApp()
    {
        App.App.Preview = () => new HtmlView();
        App.App.Printer = () => new WebViewPdfPrinter();
        return AppBuilder.Configure<App.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
