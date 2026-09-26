using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Umsatzschaetzung.App.Ui;

namespace Umsatzschaetzung.Headless;

// Drives the real interface headlessly: every step hits the same controls a
// person would. What is shown is up to the script.
public sealed class Driver(Func<bool, Shell> launch, int scale, double pad, string outDir)
{
    Shell shell = launch(false);

    // The rules open in a window of their own, not owned by the shell, and ask from there.
    readonly List<Window> opened = Track();

    static List<Window> Track()
    {
        List<Window> opened = [];
        Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        Avalonia.Controls.Window.WindowClosedEvent.AddClassHandler<Window>((w, _) => opened.Remove(w));
        return opened;
    }

    public void Run(Step step)
    {
        var window = Window(step.Window);
        switch (step)
        {
            case ShotStep s:
                Shot(window, s);
                break;
            case ClickStep s:
                Click(Find(window, s.At));
                break;
            case TypeStep s:
                Set(Editor(Find(window, s.At)), "Text", s.Text);
                break;
            case FocusStep s:
                if (s.At is null) window.FocusManager?.Focus(null);
                else (Find(window, s.At) as InputElement)?.Focus();
                break;
            case DeselectStep s:
                Set(Find(window, s.At), "SelectedItem", null);
                break;
            case SelectStep s:
                Select(Find(window, s.At));
                break;
            case TopStep s:
                Top(Find(window, s.At));
                break;
            case EditStep s:
                Edit(Find(window, s.At), s.Column, s.Text);
                break;
            case OpenStep s:
                Open(s.Number);
                break;
            case TabStep s:
                Tab(window, s.Header);
                break;
            case ImportStep s:
                Import(s.Files);
                break;
            case PickStep s:
                Pick(s.Files);
                break;
            case ChooseStep s:
                Choose((AutoCompleteBox)Find(window, s.At), s.Text, s.Item);
                break;
            case RestartStep:
                Restart();
                break;
            case WaitStep s:
                for (var i = 1; i < s.Rounds; i++) Settle();
                break;
        }
        Settle();
    }

    Window Window(string? which) => which switch
    {
        null => shell,
        "dialog" => opened.LastOrDefault(w => w != shell) ?? throw new InvalidOperationException("no window above the main window"),
        _ => throw new ArgumentException("unknown window: " + which),
    };

    static Visual Find(Visual root, Target target) => Targets.Find(root, target, Settle);

    static Visual Up(Visual inner, string type) => Targets.Up(inner, type);

    static void Click(Visual visual)
    {
        var button = visual as Button ?? visual.GetVisualAncestors().OfType<Button>().FirstOrDefault()
            ?? visual.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible)
            ?? throw new InvalidOperationException("not a button: " + visual.GetType().Name);
        if (button is RadioButton radio) radio.IsChecked = true;
        else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    // A field, or the first text box inside what `at` found (the price in a row).
    static Visual Editor(Visual visual) =>
        AvaloniaPropertyRegistry.Instance.GetRegistered((AvaloniaObject)visual).Any(p => p.Name == "Text") ? visual
        : visual.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.IsEffectivelyVisible)
          ?? throw new InvalidOperationException("no text field in " + visual.GetType().Name);

    static void Set(Visual visual, string name, object? value)
    {
        var target = (AvaloniaObject)visual;
        var property = AvaloniaPropertyRegistry.Instance.GetRegistered(target).FirstOrDefault(p => p.Name == name)
            ?? throw new InvalidOperationException(visual.GetType().Name + " has no " + name);
        target.SetValue(property, value);
    }

    static void Select(Visual row)
    {
        var list = row.GetVisualAncestors().FirstOrDefault(v => v is DataGrid or SelectingItemsControl)
            ?? throw new InvalidOperationException("no list above " + row.GetType().Name);
        Set(list, "SelectedItem", (row as StyledElement)?.DataContext);
    }

    // A list that scrolled away from its first rows shows them again; rows a list has scrolled
    // out of view are not there to be found.
    static void Top(Visual visual)
    {
        var grid = visual as DataGrid ?? (DataGrid)Up(visual, "DataGrid");
        if (grid.ItemsSource?.Cast<object>().FirstOrDefault() is { } first) grid.ScrollIntoView(first, null);
    }

    // Puts the cursor on a cell of the row `at` sits in; with a text the cell is edited and
    // committed as if typed.
    static void Edit(Visual cell, string column, string? text)
    {
        var grid = (DataGrid)Up(cell, "DataGrid");
        grid.SelectedItem = (cell as StyledElement)?.DataContext;
        grid.CurrentColumn = grid.Columns.FirstOrDefault(c => c.Header as string == column)
            ?? throw new InvalidOperationException("no column " + column);
        if (text is null) return;
        grid.BeginEdit();
        Settle();
        var box = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.IsFocused)
            ?? throw new InvalidOperationException("no editor in column " + column);
        box.Text = text;
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    void Open(string number)
    {
        var invoice = shell.Session.Case?.Invoices.Find(i => i.Number == number)
            ?? throw new InvalidOperationException("no invoice " + number);
        shell.Session.OpenInvoice(invoice.Id);
    }

    static void Tab(Visual root, string header)
    {
        var item = Find(root, new Target { Text = header, Type = "TabItem" });
        ((TabControl)Up(item, "TabControl")).SelectedItem = item;
    }

    void Import(List<string> paths)
    {
        var kase = shell.Session.Case ?? throw new InvalidOperationException("import without an open case");
        shell.Session.Imports.Add(kase.Id, kase.Label, [.. paths.SelectMany(Files).Select(Path.GetFullPath)]);
        var job = shell.Session.Imports.Jobs.Single(j => j.CaseId == kase.Id);
        while (shell.Session.Imports.Jobs.Count > 0) Settle();
        if (job.Failed.Count > 0) throw new InvalidOperationException("import failed: " + string.Join("; ", job.Failed));
    }

    static IEnumerable<string> Files(string path) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path).Order() : [path];

    // The app is quit with the case open, the rule store deleted, and the case opened again.
    void Restart()
    {
        var id = shell.Session.Case?.Id;
        Close();
        shell = launch(true);
        if (id is null) return;
        var kase = shell.Session.Service.GetCase(id, CancellationToken.None);
        while (!kase.IsCompleted) Settle();
        shell.Session.Open(kase.Result);
    }

    public void Close()
    {
        shell.Close();
        Settle();
        (shell.Session.Service as IDisposable)?.Dispose();
    }

    void Pick(List<string> paths)
    {
        var files = paths.Select(Path.GetFullPath).ToList();
        shell.Session.Picked = () =>
        {
            shell.Session.Picked = null;
            return files;
        };
    }

    // Clicks into a search box, types and clicks the entry of its drop-down that reads `item`,
    // with the pointer as a person would; the box has to have taken it, or taken and cleared it.
    static void Choose(AutoCompleteBox box, string text, string item)
    {
        Press(box);
        Settle();
        if (text != "") box.GetVisualDescendants().OfType<TextBox>().First().Text = text;
        Settle();
        var popup = box.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child
            ?? throw new InvalidOperationException("no drop-down below the search box");
        var entry = popup.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == item)
            ?? throw new InvalidOperationException("not offered: " + item);
        Press(entry);
        Settle();
        if (box.IsDropDownOpen || box.SelectedItem is { } taken && taken != entry.DataContext)
            throw new InvalidOperationException("the search box did not take " + item);
    }

    static void Press(Visual visual)
    {
        var top = TopLevel.GetTopLevel(visual) ?? throw new InvalidOperationException("not shown: " + visual.GetType().Name);
        var at = visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), top)
            ?? throw new InvalidOperationException("not placed: " + visual.GetType().Name);
        top.MouseDown(at, MouseButton.Left);
        top.MouseUp(at, MouseButton.Left);
    }

    void Shot(Window window, ShotStep step)
    {
        var dpi = new Vector(96 * scale, 96 * scale);
        using var full = new RenderTargetBitmap(Pixels(window.Bounds.Size, scale), dpi);
        full.Render(window);

        var path = Path.Combine(outDir, step.Name + ".png");
        if (Focus(window, step) is not { } focus) full.Save(path, new PngBitmapEncoderOptions());
        else
        {
            var area = focus.Inflate(pad).Intersect(new Rect(window.Bounds.Size));
            using var crop = new RenderTargetBitmap(Pixels(area.Size, scale), dpi);
            using (var ctx = crop.CreateDrawingContext())
                ctx.DrawImage(full, area * scale, new Rect(area.Size));
            crop.Save(path, new PngBitmapEncoderOptions());
        }
        Console.WriteLine(path);
    }

    // A crop shows what the step is about; the padding is added while cutting.
    static Rect? Focus(Window window, ShotStep step)
    {
        if (step.At is null) return null;
        var visual = Find(window, step.At);
        var box = Box(window, visual);
        if (step.Clip is { } type)
        {
            var bottom = visual.GetVisualDescendants().Where(v => v.GetType().Name == type)
                .Select(v => Box(window, v).Bottom)
                .DefaultIfEmpty(box.Bottom)
                .Max();
            box = box.WithHeight(bottom - box.Y);
        }
        if (step.Trim is { } trim) box = box.Deflate(new Thickness(trim.Left, trim.Top, trim.Right, trim.Bottom));
        return box;
    }

    static Rect Box(Window window, Visual visual) => Targets.Box(window, visual);

    static PixelSize Pixels(Size size, int scale) =>
        new((int)(size.Width * scale), (int)(size.Height * scale));

    public static void Settle()
    {
        for (var i = 0; i < 25; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
