using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Umsatzschätzung.App.Ui;

public sealed class Dialog : Window
{
    readonly TaskCompletionSource<bool> answered = new();

    bool answer;

    Dialog(string message, string title, bool confirm)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        if (confirm)
        {
            buttons.Children.Add(Action("Ja", true, "SecondaryButton", false, false));
            buttons.Children.Add(Action("Nein", false, "PrimaryButton", true, true));
        }
        else
        {
            buttons.Children.Add(Action("OK", true, "PrimaryButton", true, true));
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
    }

    Button Action(string caption, bool value, string theme, bool preferred, bool cancel)
    {
        var button = new Button { Content = caption, MinWidth = 96, IsDefault = preferred, IsCancel = cancel };
        if (Application.Current?.TryFindResource(theme, out var found) == true && found is ControlTheme control)
            button.Theme = control;
        button.Click += (_, _) => { answer = value; Close(); };
        return button;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        answered.TrySetResult(answer);
    }

    public static Task Alert(Window? owner, string message, string title) => Ask(owner, message, title, false);

    public static Task<bool> Confirm(Window? owner, string message, string title) => Ask(owner, message, title, true);

    // The crash path may have no window yet; the caller owns and shows this one.
    public static Window Standalone(string message, string title) => new Dialog(message, title, false);

    static Task<bool> Ask(Window? owner, string message, string title, bool confirm)
    {
        var dialog = new Dialog(message, title, confirm);
        if (owner is null) dialog.Show();
        else _ = dialog.ShowDialog(owner);
        return dialog.answered.Task;
    }
}
