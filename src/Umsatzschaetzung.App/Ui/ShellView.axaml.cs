using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

// The whole program on one surface: a window on the desktop, the page in a browser.
public partial class ShellView : UserControl
{
    readonly Session session;
    readonly CasesView cases;
    readonly Screen[] screens;
    readonly Dictionary<ImportJob, ImportWindow> imports = [];
    Screen? current;
    RulesWindow? rules;
    bool moving;

    public ShellView(IService service)
    {
        InitializeComponent();
        session = new Session(service);
        session.Imports.Jobs.CollectionChanged += ImportsChanged;
        cases = new CasesView(session);
        CasesHost.Content = cases;
        screens =
        [
            new CaseView(session),
            new InvoicesView(session),
            new MappingView(session),
            new ProductsView(session),
            new CalcView(session),
            new ReportView(session),
        ];
        for (var i = 0; i < screens.Length; i++) ((TabItem)Tabs.Items[i]!).Content = screens[i];
        session.CaseOpened += OpenCase;
        session.CaseChanged += RefreshContext;
        session.StatusChanged += RefreshError;
        session.RulesRequested += () => ShowRules();
        session.ProductRequested += (name, created) => ShowRules().NewProduct(name, created);
        session.ProductEditRequested += (id, recipe) => ShowRules().EditProduct(id, recipe);
        session.TabRequested += tab => Tabs.SelectedIndex = (int)tab;
        session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Session.Error)) RefreshError(); };
        session.Indicate(this, SaveBadge, SaveText);
        MenuBar.IsVisible = !OperatingSystem.IsMacOS() && !OperatingSystem.IsBrowser();
        Loaded += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this)!;
            session.Owner = top;
            Help.OnF1(top, () => Topic);
            History.Keys(top, Move);
            Show(cases);
            await session.LoadStatus(CancellationToken.None);
            await session.LoadRules(CancellationToken.None);
        };
    }

    internal Session Session => session;

    public event Action<string>? Titled;

    public string Topic => current?.Topic ?? Help.Start;

    Window? Host => TopLevel.GetTopLevel(this) as Window;

    // What is still being written; the window waits for it before it closes.
    public Task Leave()
    {
        current?.Leave();
        current = null;
        return session.Saved;
    }

    public void Closed()
    {
        session.Imports.CancelAll();
        rules?.Close();
    }

    void ShowHelp(object? sender, RoutedEventArgs e) => Help.Open(Host, Topic);

    void ShowManual(object? sender, RoutedEventArgs e) => Help.Open(Host, Help.Start);

    void ShowAbout(object? sender, RoutedEventArgs e) => App.ShowAbout(Host);

    void ImportsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (ImportJob job in e.OldItems ?? Array.Empty<ImportJob>())
            if (imports.Remove(job, out var window) && job.Summary == "") window.Close();
        foreach (ImportJob job in e.NewItems ?? Array.Empty<ImportJob>())
        {
            var window = new ImportWindow(job);
            imports[job] = window;
            if (Host is { } host) window.Show(host);
            else window.Show();
        }
    }

    void ShowRules(object? sender, RoutedEventArgs e) => ShowRules();

    RulesWindow ShowRules()
    {
        if (rules is null)
        {
            rules = new RulesWindow(session);
            rules.Closed += (_, _) => rules = null;
            rules.Show();
        }
        else rules.Activate();
        return rules;
    }

    void Show(Screen screen)
    {
        if (current == screen) return;
        session.Error = "";
        current?.Leave();
        current = screen;
        screen.Enter();
    }

    // The page hands over what is still being typed first, it is the newest change. The page the
    // step was made on then opens afresh, with the item marked and a refusal still showing.
    async Task Move(bool back)
    {
        var history = session.History;
        if (!CaseUi.IsVisible || moving || !(back ? history.CanUndo : history.CanRedo)) return;
        moving = true;
        var tab = Tabs.SelectedIndex;
        current?.Leave();
        current = null;
        var place = back ? await history.Undo() : await history.Redo();
        moving = false;
        if (!CaseUi.IsVisible) return;
        if (place is not null) tab = place.Page;
        current?.Leave();
        current = screens[tab];
        current.Enter(place?.Item);
        Tabs.SelectedIndex = tab;
    }

    void TabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs && Tabs.SelectedIndex >= 0 && CaseUi.IsVisible) Show(screens[Tabs.SelectedIndex]);
    }

    void OpenCase(Case resp)
    {
        RefreshContext();
        CasesHost.IsVisible = false;
        CaseUi.IsVisible = true;
        Tabs.SelectedIndex = 0;
        Show(screens[0]);
    }

    async void Back(object? sender, RoutedEventArgs e)
    {
        current?.Leave();
        current = null;
        CaseUi.IsEnabled = false;
        await session.Saved;
        CaseUi.IsEnabled = true;
        session.CloseCase();
        CaseUi.IsVisible = false;
        CasesHost.IsVisible = true;
        Titled?.Invoke("Umsatzschätzung");
        Show(cases);
    }

    void RefreshContext()
    {
        if (session.Case is null) return;
        CaseLabel.Text = session.Case.Label;
        CasePeriod.Text = session.Period;
        Titled?.Invoke("Umsatzschätzung: " + session.Case.Label);
    }

    void DismissError(object? sender, RoutedEventArgs e) => session.Error = "";

    void RefreshError()
    {
        var error = session.ErrorWindow is { } owner && owner != Host ? "" : session.Error;
        var text = error != "" ? error : session.Status?.Problem ?? "";
        ErrorText.Text = text;
        ErrorBanner.IsVisible = text != "";
        ErrorClose.IsVisible = error != "";
    }
}
