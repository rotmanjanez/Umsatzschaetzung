using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Umsatzschaetzung.App.Ui;

public static class Input
{
    public static long? Cents(string s) => Scaled(s, 2);
    public static long? Bp(string s) => Scaled(s, 2);
    public static long? Milli(string s) => Scaled(s, 3);
    public static long? Micro(string s) => Scaled(s, 6);

    public static long? Int(string s) =>
        Ungrouped(s.Trim()) is { } digits && long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;

    // A dot only groups thousands, as "12.500": "0.5" is no number here, the decimal mark is the comma.
    static string? Ungrouped(string s)
    {
        if (!s.Contains('.')) return s;
        var groups = s.TrimStart('-').Split('.');
        return groups[0].Length is >= 1 and <= 3 && groups.Skip(1).All(g => g.Length == 3) ? s.Replace(".", "") : null;
    }

    public static DateOnly? Date(string s) =>
        DateOnly.TryParseExact(s.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string Edit(string display) => display.TrimEnd().TrimEnd('€', '%').TrimEnd();

    public static string BpText(long v)
    {
        var digits = Math.Abs(v).ToString(CultureInfo.InvariantCulture).PadLeft(3, '0');
        var text = (digits[..^2] + "," + digits[^2..]).TrimEnd('0').TrimEnd(',');
        return v < 0 ? "-" + text : text;
    }

    static long? Scaled(string s, int decimals)
    {
        s = Edit(s).Replace(" ", "");
        if (s == "") return null;
        var neg = s.StartsWith('-');
        if (neg) s = s[1..];
        var comma = s.IndexOf(',');
        if (Ungrouped(comma < 0 ? s : s[..comma]) is not { } whole) return null;
        var frac = comma < 0 ? "" : s[(comma + 1)..];
        if (whole == "" && frac == "" || frac.Length > decimals) return null;
        frac = frac.PadRight(decimals, '0');
        if (!long.TryParse(whole + frac, NumberStyles.None, CultureInfo.InvariantCulture, out var v)) return null;
        return neg ? -v : v;
    }
}

public static class Accessible
{
    static readonly AttachedProperty<TextBlock?> SpeakerProperty =
        AvaloniaProperty.RegisterAttached<Visual, TextBlock?>("Speaker", typeof(Accessible));

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_automationPeer")]
    static extern ref AutomationPeer? Peer(Control control);

    public static void Install<T>(T control, Func<T, AutomationPeer> create) where T : Control
    {
        ref var peer = ref Peer(control);
        peer ??= create(control);
    }

    // What a view names on the host is read where the focus lands: in the box inside it.
    public static void Forward(Control host, Control target, bool id = false)
    {
        target.Bind(AutomationProperties.NameProperty, host.GetObservable(AutomationProperties.NameProperty));
        target.Bind(AutomationProperties.HelpTextProperty, host.GetObservable(AutomationProperties.HelpTextProperty));
        target.Bind(AutomationProperties.LabeledByProperty, host.GetObservable(AutomationProperties.LabeledByProperty));
        target.Bind(AutomationProperties.IsRequiredForFormProperty, host.GetObservable(AutomationProperties.IsRequiredForFormProperty));
        if (id) target.Bind(AutomationProperties.AutomationIdProperty, host.GetObservable(AutomationProperties.AutomationIdProperty));
    }

    // Read out by a screen reader from an unseen line in the adorner layer of the window.
    public static void Announce(Visual scope, string text)
    {
        if (AdornerLayer.GetAdornerLayer(scope) is not { } layer) return;
        if (layer.GetValue(SpeakerProperty) is not { } speaker)
        {
            speaker = new TextBlock { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false };
            AutomationProperties.SetLiveSetting(speaker, AutomationLiveSetting.Polite);
            layer.Children.Add(speaker);
            layer.SetValue(SpeakerProperty, speaker);
        }
        speaker.Text = speaker.Text == text ? text + "\u200B" : text;
    }
}
