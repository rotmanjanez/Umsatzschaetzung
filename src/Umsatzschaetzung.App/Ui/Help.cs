using System.ComponentModel;
using System.Diagnostics;
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

    public static void Open(Window? owner, string topic)
    {
        var url = $"https://docs.umsatzschaetzung.amtstools.de/{Release.Docs}/{topic}";
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or IOException or PlatformNotSupportedException)
        {
            _ = Dialog.Alert(owner, $"Die Dokumentation ließ sich nicht öffnen. Sie steht unter:\n\n{url}", "Hilfe");
        }
    }

    public static void OnF1(TopLevel window, Func<string> topic)
    {
        void Pressed(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.F1) return;
            Open(window as Window, topic());
            e.Handled = true;
        }
        window.AddHandler(InputElement.KeyDownEvent, Pressed, RoutingStrategies.Tunnel);
    }
}
