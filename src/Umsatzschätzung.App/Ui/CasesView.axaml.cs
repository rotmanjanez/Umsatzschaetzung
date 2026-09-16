using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public sealed class CasesModel : Observable
{
    bool empty = true, creating;

    public ObservableCollection<CaseRow> Cases { get; } = [];

    public bool Empty { get => empty; private set { if (Set(ref empty, value)) Raise(nameof(ShowEmpty)); } }
    public bool Creating { get => creating; set { if (Set(ref creating, value)) Raise(nameof(ShowEmpty)); } }
    public bool ShowEmpty => empty && !creating;

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
    readonly TextBox[] fields;

    public CasesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        Search.Attach(model.Cases, r => r.Case.Label + " " + r.Period + " " + r.Case.Taxpayer.Name);
        Grid.ItemsSource = Search.View;
        Grid.AddHandler(PointerReleasedEvent, RowClicked, RoutingStrategies.Bubble);
        fields = [NewLabel, NewFrom.Box, NewTo.Box, NewName, NewTaxNumber, NewPab];
        foreach (var field in fields) field.TextChanged += (s, _) => ((TextBox)s!).Classes.Set("invalid", false);
    }

    void ShowRules(object? sender, RoutedEventArgs e) => Session.ShowRules();

    protected override void OnEnter() => _ = Reload();

    Task Reload() => Session.Run(async () =>
    {
        var resp = await Session.Service.ListCases(Ct);
        model.Set(resp.Cases);
    });

    void StartNew(object? sender, RoutedEventArgs e)
    {
        foreach (var field in fields) field.Classes.Set("invalid", false);
        model.Creating = true;
        NewLabel.Focus();
    }

    void CancelNew(object? sender, RoutedEventArgs e) => model.Creating = false;

    async void Create(object? sender, RoutedEventArgs e)
    {
        var label = (NewLabel.Text ?? "").Trim();
        var from = Input.Date(NewFrom.Text);
        var to = Input.Date(NewTo.Text);
        var taxpayer = new Taxpayer
        {
            Name = (NewName.Text ?? "").Trim(),
            TaxNumber = (NewTaxNumber.Text ?? "").Trim(),
            PabNumber = (NewPab.Text ?? "").Trim(),
        };

        var problems = new List<string>();
        TextBox? first = null;
        void Check(TextBox field, bool ok, string name)
        {
            field.Classes.Set("invalid", !ok);
            if (ok) return;
            problems.Add(name);
            first ??= field;
        }

        Check(NewLabel, label != "", "Bezeichnung");
        Check(NewFrom.Box, from is not null, Period("von", NewFrom.Text));
        Check(NewTo.Box, to is not null, Period("bis", NewTo.Text));
        if (from is { } f && to is { } t && t < f) Check(NewTo.Box, false, "Zeitraum bis (liegt vor dem Beginn)");
        Check(NewName, taxpayer.Name != "", "Name");
        Check(NewTaxNumber, taxpayer.TaxNumber != "", "Steuernummer");
        Check(NewPab, taxpayer.PabNumber != "", "PaB-Nr.");
        if (problems.Count > 0)
        {
            Session.Fail("Bitte prüfen: " + string.Join(", ", problems) + ".");
            first?.Focus();
            return;
        }

        var kase = new Case { Label = label, PeriodFrom = from!.Value, PeriodTo = to!.Value, Taxpayer = taxpayer };
        await Session.Run(async () =>
        {
            var resp = await Session.Service.PutCase(kase, Ct);
            model.Creating = false;
            NewLabel.Text = NewFrom.Text = NewTo.Text = "";
            NewName.Text = NewTaxNumber.Text = NewPab.Text = "";
            Session.Open(resp);
        });
    }

    static string Period(string end, string text) =>
        text.Trim() == "" ? "Zeitraum " + end : "Zeitraum " + end + " (Datum als TT.MM.JJJJ)";

    void RowClicked(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is not Visual source) return;
        var row = source.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        if (row is not null) Open(row.DataContext as CaseRow);
    }

    void GridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Open(Grid.SelectedItem as CaseRow);
    }

    async void Open(CaseRow? row)
    {
        if (row is null) return;
        await Session.Run(async () =>
        {
            var resp = await Session.Service.GetCase(row.Case.Id, Ct);
            Session.Open(resp);
        });
    }

    async void Delete(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CaseRow row) return;
        var answer = await Dialog.Confirm(TopLevel.GetTopLevel(this) as Window,
            "„" + row.Case.Label + "“ wird mit allen Rechnungen unwiderruflich gelöscht.",
            "Prüfung löschen");
        if (!answer) return;
        await Session.Run(async () =>
        {
            await Session.Service.DeleteCase(row.Case.Id, Ct);
            await Reload();
        });
    }

    async void Import(object? sender, RoutedEventArgs e)
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
