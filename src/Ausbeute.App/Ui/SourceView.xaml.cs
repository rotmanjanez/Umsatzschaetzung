using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ausbeute.Service;

namespace Ausbeute.App.Ui;

public partial class SourceView : UserControl
{
    const double BaseWidth = 640;
    const double ZoomStep = 1.25;
    double zoom = 1;
    List<SourcePage> pages = [];

    public SourceView() => InitializeComponent();

    public void Show(InvoiceSourceResp? source, string note)
    {
        Note.Text = note;
        pages = source?.Pages ?? [];
        Render();
    }

    void ZoomIn(object sender, RoutedEventArgs e) => SetZoom(zoom * ZoomStep);

    void ZoomOut(object sender, RoutedEventArgs e) => SetZoom(zoom / ZoomStep);

    void ZoomReset(object sender, RoutedEventArgs e) => SetZoom(1);

    void SetZoom(double z)
    {
        zoom = Math.Clamp(z, 0.5, 4);
        ZoomText.Text = (int)(zoom * 100 + 0.5) + " %";
        Render();
    }

    void Render()
    {
        Pages.Children.Clear();
        var width = BaseWidth * zoom;
        foreach (var page in pages)
        {
            if (page.Image is { } image)
            {
                Pages.Children.Add(new Border
                {
                    Style = (Style)FindResource("Panel"),
                    Margin = new Thickness(0, 0, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new Image { Source = Images.Decode(image), Width = width, Stretch = Stretch.Uniform },
                });
                continue;
            }
            Pages.Children.Add(new TextBox
            {
                Text = page.Text ?? "",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = (FontFamily)FindResource("MonoFont"),
                Width = width,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            });
        }
    }
}
