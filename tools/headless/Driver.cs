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

// Fährt die echte Oberfläche kopflos: jeder Schritt trifft dieselben Steuerelemente,
// die auch ein Mensch trifft. Was gezeigt wird, steht im Skript.
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
        "dialog" => shell.OwnedWindows.LastOrDefault() ?? throw new InvalidOperationException("kein Fenster über dem Hauptfenster"),
        _ => throw new ArgumentException("unbekanntes Fenster: " + which),
    };

    static Visual Find(Visual root, Target target)
    {
        var hits = root.GetVisualDescendants();
        if (target.Name is { } name) hits = hits.Where(v => (v as StyledElement)?.Name == name);
        if (target.Text is { } text) hits = hits.Where(v => Label(v) == text);
        if (target.Type is { } type) hits = hits.Where(v => v.GetType().Name == type);
        var hit = hits.FirstOrDefault() ?? throw new InvalidOperationException("nicht gefunden: " + target);
        return target.Up is { } up ? Up(hit, up) : hit;
    }

    static Visual Up(Visual inner, string type) =>
        inner.GetVisualAncestors().FirstOrDefault(v => v.GetType().Name == type)
        ?? throw new InvalidOperationException("kein " + type + " über " + inner.GetType().Name);

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
            ?? throw new InvalidOperationException("kein Knopf: " + visual.GetType().Name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    static void Set(Visual visual, string name, object? value)
    {
        var target = (AvaloniaObject)visual;
        var property = AvaloniaPropertyRegistry.Instance.GetRegistered(target).FirstOrDefault(p => p.Name == name)
            ?? throw new InvalidOperationException(visual.GetType().Name + " hat kein " + name);
        target.SetValue(property, value);
    }

    static void Tab(Visual root, string header)
    {
        var item = Find(root, new Target { Text = header, Type = "TabItem" });
        ((TabControl)Up(item, "TabControl")).SelectedItem = item;
    }

    void Import(List<string> paths)
    {
        var kase = shell.Session.Case ?? throw new InvalidOperationException("Import ohne offene Prüfung");
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

    // Ein Ausschnitt zeigt, worum es im Schritt geht; der Rand kommt beim Zuschneiden dazu.
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
