using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public partial class SourceView : UserControl
{
    const double BaseWidth = 640;

    public SourceView() => InitializeComponent();

    public void Show(InvoiceSourceResp? source, string note)
    {
        Note.Text = note;
        Pages.Children.Clear();
        foreach (var page in source?.Pages ?? [])
        {
            if (page.Image is { } image)
            {
                Pages.Children.Add(new Border
                {
                    Style = (Style)FindResource("Panel"),
                    Margin = new Thickness(0, 0, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new Image { Source = Images.Decode(image), Width = BaseWidth, Stretch = Stretch.Uniform },
                });
                continue;
            }
            Pages.Children.Add(new TextBox
            {
                Text = page.Text ?? "",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = (FontFamily)FindResource("MonoFont"),
                Width = BaseWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            });
        }
        ZoomPan.SetZoom(Viewer, 1);
        Viewer.ScrollToHome();
    }

    void ZoomIn(object sender, RoutedEventArgs e) => ZoomPan.ZoomBy(Viewer, ZoomPan.Step);

    void ZoomOut(object sender, RoutedEventArgs e) => ZoomPan.ZoomBy(Viewer, 1 / ZoomPan.Step);

    void ZoomReset(object sender, RoutedEventArgs e) => ZoomPan.FitWidth(Viewer);
}
