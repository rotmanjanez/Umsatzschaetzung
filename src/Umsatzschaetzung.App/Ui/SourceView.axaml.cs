using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public partial class SourceView : UserControl
{
    const double BaseWidth = 640;

    public SourceView() => InitializeComponent();

    int shown;

    public async void Show(InvoiceSourceResp? source, string note)
    {
        var at = ++shown;
        Note.Text = note;
        Pages.Children.Clear();
        var list = source?.Pages ?? [];
        var images = await Task.Run(() => list.Select(p => p.Image is { } raster ? Images.From(raster) : null).ToList());
        if (at != shown) return;
        for (var i = 0; i < list.Count; i++)
        {
            var page = list[i];
            var name = $"Belegseite {i + 1} von {list.Count}";
            if (images[i] is { } image)
            {
                var picture = new Image { Source = image, Width = BaseWidth, Stretch = Stretch.Uniform };
                AutomationProperties.SetName(picture, name);
                if (page.Text is { } text) AutomationProperties.SetHelpText(picture, text);
                Pages.Children.Add(new Border
                {
                    Theme = (ControlTheme)Application.Current!.FindResource("Panel")!,
                    Margin = new Thickness(0, 0, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = picture,
                });
                continue;
            }
            var box = new TextBox
            {
                Text = page.Text ?? "",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = (FontFamily)Application.Current!.FindResource("MonoFont")!,
                Width = BaseWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            };
            AutomationProperties.SetName(box, name);
            Pages.Children.Add(box);
        }
        ZoomPan.SetZoom(Viewer, 1);
        Viewer.ScrollToHome();
    }

    void ZoomIn(object? sender, RoutedEventArgs e) => ZoomPan.ZoomBy(Viewer, ZoomPan.Step);

    void ZoomOut(object? sender, RoutedEventArgs e) => ZoomPan.ZoomBy(Viewer, 1 / ZoomPan.Step);

    void ZoomReset(object? sender, RoutedEventArgs e) => ZoomPan.FitWidth(Viewer);
}
