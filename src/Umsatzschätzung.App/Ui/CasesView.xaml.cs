using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public sealed class CasesModel : Observable
{
    bool empty = true;

    public ObservableCollection<CaseRow> Cases { get; } = [];

    public bool Empty { get => empty; private set => Set(ref empty, value); }

    public void Set(List<CaseRow> rows)
    {
        Cases.Clear();
        foreach (var r in rows) Cases.Add(r);
        Empty = rows.Count == 0;
    }
}

public partial class CasesView : Screen
{
    readonly CasesModel model = new();

    public CasesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
    }

    protected override void OnEnter() => _ = Reload();

    Task Reload() => Session.Run(async () =>
    {
        var resp = await Session.Service.ListCases(Ct);
        model.Set(resp.Cases);
    });

    void StartNew(object sender, RoutedEventArgs e)
    {
        NewPanel.Visibility = Visibility.Visible;
        NewLabel.Focus();
    }

    void CancelNew(object sender, RoutedEventArgs e) => NewPanel.Visibility = Visibility.Collapsed;

    async void Create(object sender, RoutedEventArgs e)
    {
        var label = NewLabel.Text.Trim();
        var from = Input.Date(NewFrom.Text);
        var to = Input.Date(NewTo.Text);
        if (label == "" || from is null || to is null)
        {
            Session.Fail("Bitte Bezeichnung und Zeitraum (TT.MM.JJJJ) angeben.");
            return;
        }
        var kase = new Case { Label = label, PeriodFrom = from.Value, PeriodTo = to.Value };
        await Session.Run(async () =>
        {
            var resp = await Session.Service.PutCase(kase, Ct);
            NewPanel.Visibility = Visibility.Collapsed;
            NewLabel.Text = NewFrom.Text = NewTo.Text = "";
            Session.Open(resp);
        });
    }

    void OpenSelected(object sender, MouseButtonEventArgs e) => Open(Grid.SelectedItem as CaseRow);

    void GridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Open(Grid.SelectedItem as CaseRow);
    }

    void OpenRow(object sender, RoutedEventArgs e) => Open(((FrameworkElement)sender).DataContext as CaseRow);

    async void Open(CaseRow? row)
    {
        if (row is null) return;
        await Session.Run(async () =>
        {
            var resp = await Session.Service.GetCase(row.Case.Id, Ct);
            Session.Open(resp);
        });
    }

    async void Delete(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not CaseRow row) return;
        var answer = MessageBox.Show(
            "„" + row.Case.Label + "“ wird mit allen Rechnungen unwiderruflich gelöscht.",
            "Prüfung löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        await Session.Run(async () =>
        {
            await Session.Service.DeleteCase(row.Case.Id, Ct);
            await Reload();
        });
    }

    async void Import(object sender, RoutedEventArgs e)
    {
        var files = await Session.PickFiles(Session.CaseFilter, false);
        if (files.Count == 0) return;
        await Session.Run(async () =>
        {
            var resp = await Session.Service.ImportCase(files[0].Name, files[0].Data, Ct);
            Session.Open(resp);
        });
    }
}
