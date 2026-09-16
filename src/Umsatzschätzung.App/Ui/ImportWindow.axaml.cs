using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Umsatzschätzung.App.Ui;

public partial class ImportWindow : Window
{
    readonly ImportJob job;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public ImportWindow(ImportJob job)
    {
        InitializeComponent();
        this.job = job;
        DataContext = job;
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
    }

    void Tick()
    {
        if (job.Complete)
        {
            timer.Stop();
            Title = "Import · " + job.Label;
            return;
        }
        job.Sample();
        Title = "Import · " + job.Percent + " · " + job.Label;
    }

    void CancelClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        timer.Stop();
        if (!job.Complete) job.Cancel();
    }
}
