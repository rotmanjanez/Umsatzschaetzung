using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Umsatzschaetzung.App.Ui;

public partial class ImportPane : UserControl
{
    readonly ImportJob job;
    readonly Frame frame;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool attended;

    ImportPane(ImportJob job)
    {
        InitializeComponent();
        this.job = job;
        DataContext = job;
        frame = Frame.For(this, "Import " + job.Label, width: 440);
        frame.Closed += Closed;
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
    }

    public static Frame Open(Control owner, ImportJob job)
    {
        var pane = new ImportPane(job);
        pane.frame.Show(owner);
        return pane.frame;
    }

    void Tick()
    {
        if (job.Complete)
        {
            timer.Stop();
            frame.Title = "Import " + job.Label;
            if (attended || frame is WindowFrame) Done.Focus(NavigationMethod.Tab);
            return;
        }
        attended = IsKeyboardFocusWithin;
        job.Sample();
        frame.Title = "Import " + job.Label + " (" + job.Percent + ")";
    }

    void CancelClick(object? sender, RoutedEventArgs e) => frame.Close();

    void Closed()
    {
        timer.Stop();
        if (!job.Complete) job.Cancel();
    }
}
