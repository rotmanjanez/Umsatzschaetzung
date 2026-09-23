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
public sealed class Driver(Shell shell, int scale, double pad, string outDir)
{
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
                Set(Find(window, s.At), "Text", s.Text);
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
            case WaitStep s:
                for (var i = 1; i < s.Rounds; i++) Settle();
                break;
        }
        Settle();
    }

    Window Window(string? which) => which switch
    {
        null => shell,
        "dialog" => shell.OwnedWindows.LastOrDefault() ?? throw new InvalidOperationException("no window above the main window"),
        _ => throw new ArgumentException("unknown window: " + which),
    };

    static Visual Find(Visual root, Target target)
    {
        var hits = root.GetVisualDescendants().Where(v => v.IsEffectivelyVisible);
        if (target.Name is { } name) hits = hits.Where(v => (v as StyledElement)?.Name == name);
        if (target.Text is { } text) hits = hits.Where(v => Label(v) == text);
        if (target.Type is { } type) hits = hits.Where(v => v.GetType().Name == type);
        var hit = hits.FirstOrDefault() ?? throw new InvalidOperationException("not found: " + target);
        return target.Up is { } up ? Up(hit, up) : hit;
    }

    static Visual Up(Visual inner, string type) =>
        inner.GetVisualAncestors().FirstOrDefault(v => v.GetType().Name == type)
        ?? throw new InvalidOperationException("no " + type + " above " + inner.GetType().Name);

    static string? Label(Visual visual) => visual switch
    {
        TextBlock t => t.Text,
        HeaderedContentControl h => h.Header as string,
        ContentControl c => c.Content as string,
        _ => null,
    };

    static void Click(Visual visual)
    {
        var button = visual as Button ?? visual.GetVisualAncestors().OfType<Button>().FirstOrDefault()
            ?? visual.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible)
            ?? throw new InvalidOperationException("not a button: " + visual.GetType().Name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

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
        var picked = paths
            .Select(p => new PickedFile(Path.GetFileName(p), File.ReadAllBytes(p)))
            .ToList();
        shell.Session.Imports.Add(kase.Id, kase.Label, picked);
        while (shell.Session.Imports.Jobs.Count > 0) Settle();
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

    static Rect Box(Window window, Visual visual) =>
        new(visual.TranslatePoint(default, window) ?? default, visual.Bounds.Size);

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
