using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Umsatzschaetzung.App.Ui;

public sealed class Dialog : UserControl
{
    readonly TaskCompletionSource<bool> answered = new();
    readonly Frame frame;

    bool answer;
    Button? first;

    Dialog(bool standalone, string message, string title, bool confirm, string yes = "Ja", string no = "Nein")
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        if (confirm)
        {
            buttons.Children.Add(Action(yes, true, "SecondaryButton", false, false, message));
            buttons.Children.Add(Action(no, false, "PrimaryButton", true, true, message));
        }
        else
        {
            buttons.Children.Add(Action("OK", true, "PrimaryButton", true, true, message));
        }

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            MaxWidth = 460,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttons,
            },
        };
        frame = standalone ? new WindowFrame(this, title) : Frame.For(this, title);
        frame.Closed += () => answered.TrySetResult(answer);
        frame.Opening += () => first?.Focus();
    }

    // The message is read with the button that has the focus: a screen reader reads nothing else of it.
    Button Action(string caption, bool value, string theme, bool preferred, bool cancel, string message)
    {
        var button = new Button { Content = caption, MinWidth = 96, IsDefault = preferred, IsCancel = cancel };
        AutomationProperties.SetHelpText(button, message);
        AutomationProperties.SetAutomationId(button, !value ? "DialogNo" : preferred ? "DialogOk" : "DialogYes");
        if (preferred) first = button;
        if (Application.Current?.TryFindResource(theme, out var found) == true && found is ControlTheme control)
            button.Theme = control;
        button.Click += (_, _) => { answer = value; frame.Close(); };
        return button;
    }

    public static Task Alert(Control? owner, string message, string title) => Ask(owner, message, title, false);

    public static Task<bool> Confirm(Control? owner, string message, string title, string yes = "Ja", string no = "Nein") =>
        Ask(owner, message, title, true, yes, no);

    // The crash path may have no window yet; the caller owns and shows this one.
    public static Window Standalone(string message, string title) => ((WindowFrame)new Dialog(true, message, title, false).frame).Window;

    // Likewise before the shell exists, but the answer decides whether it ever does.
    public static (Window Window, Task<bool> Answer) StandaloneConfirm(string message, string title)
    {
        var dialog = new Dialog(true, message, title, true);
        return (((WindowFrame)dialog.frame).Window, dialog.answered.Task);
    }

    static Task<bool> Ask(Control? owner, string message, string title, bool confirm, string yes = "Ja", string no = "Nein")
    {
        var dialog = new Dialog(false, message, title, confirm, yes, no);
        dialog.frame.Show(owner, modal: true);
        return dialog.answered.Task;
    }
}
