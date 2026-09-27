using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Umsatzschaetzung.App.Platform;

namespace Umsatzschaetzung.App.Ui;

public static class Help
{
    public const string Start = "";
    public const string Cases = "pruefungen/";
    public const string Case = "pruefung/";
    public const string Invoices = "rechnungen/";
    public const string Invoice = "rechnungen/#durchsicht";
    public const string Mapping = "zuordnung/";
    public const string Products = "kalkulation/";
    public const string Calc = "kalkulation/";
    public const string Report = "bericht/";
    public const string Rules = "regeln/";

    public static async void Open(Control origin, string topic)
    {
        var url = $"https://docs.umsatzschaetzung.amtstools.de/{Release.Docs}/{topic}";
        if (TopLevel.GetTopLevel(origin)?.Launcher is { } launcher && await launcher.LaunchUriAsync(new Uri(url))) return;
        _ = Dialog.Alert(origin, $"Die Dokumentation ließ sich nicht öffnen. Sie steht unter:\n\n{url}", "Hilfe");
    }

    public static void OnF1(Control scope, Func<string> topic)
    {
        void Pressed(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.F1) return;
            Open(scope, topic());
            e.Handled = true;
        }
        scope.AddHandler(InputElement.KeyDownEvent, Pressed, RoutingStrategies.Tunnel);
    }
}
