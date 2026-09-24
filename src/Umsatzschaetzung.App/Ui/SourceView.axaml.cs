using Avalonia;
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
        var images = await Task.Run(() => list.Select(p => p.Image is { } data ? Images.Decode(data) : null).ToList());
        if (at != shown) return;
        for (var i = 0; i < list.Count; i++)
        {
            var page = list[i];
            if (images[i] is { } image)
            {
                Pages.Children.Add(new Border
                {
                    Theme = (ControlTheme)Application.Current!.FindResource("Panel")!,
                    Margin = new Thickness(0, 0, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new Image { Source = image, Width = BaseWidth, Stretch = Stretch.Uniform },
                });
                continue;
            }
            Pages.Children.Add(new TextBox
            {
                Text = page.Text ?? "",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = (FontFamily)Application.Current!.FindResource("MonoFont")!,
                Width = BaseWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            });
        }
        ZoomPan.SetZoom(Viewer, 1);
        Viewer.ScrollToHome();
    }

    void ZoomIn(object? sender, RoutedEventArgs e) => ZoomPan.ZoomBy(Viewer, ZoomPan.Step);

    void ZoomOut(object? sender, RoutedEventArgs e) => ZoomPan.ZoomBy(Viewer, 1 / ZoomPan.Step);

    void ZoomReset(object? sender, RoutedEventArgs e) => ZoomPan.FitWidth(Viewer);
}
