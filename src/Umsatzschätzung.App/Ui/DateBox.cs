using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Calendar = Avalonia.Controls.Calendar;

namespace Umsatzschätzung.App.Ui;

public sealed class DateBox : Grid
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<DateBox, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);

    readonly TextBox box = new() { PlaceholderText = "TT.MM.JJJJ" };
    readonly Calendar calendar = new();
    readonly Flyout flyout;
    bool picking;

    public DateBox()
    {
        var icon = new PathIcon { Width = 14, Height = 14 };
        icon[!PathIcon.DataProperty] = new DynamicResourceExtension("CalendarIcon");

        flyout = new Flyout { Content = calendar, Placement = PlacementMode.BottomEdgeAlignedRight };
        var pick = new Button
        {
            Content = icon,
            Flyout = flyout,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(8, 4),
            Focusable = false,
        };
        pick[!ThemeProperty] = new DynamicResourceExtension("IconButton");
        ToolTip.SetTip(pick, "Datum wählen");

        box.InnerRightContent = pick;
        Children.Add(box);

        box.TextChanged += (_, _) => Text = box.Text ?? "";
        flyout.Opened += (_, _) => ShowCurrent();
        calendar.SelectedDatesChanged += (_, _) => Picked();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public TextBox Box => box;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TextProperty) box.Text = Text;
    }

    void ShowCurrent()
    {
        picking = true;
        calendar.DisplayMode = CalendarMode.Year;
        if (Input.Date(Text) is { } d)
        {
            calendar.DisplayDate = d.ToDateTime(TimeOnly.MinValue);
            calendar.SelectedDate = calendar.DisplayDate;
        }
        else
        {
            calendar.SelectedDate = null;
        }
        picking = false;
    }

    void Picked()
    {
        if (picking || calendar.SelectedDate is not { } d) return;
        Text = d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        flyout.Hide();
    }
}
