using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Headless;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Lessons;

// The slider of a lesson goes back and forth over the steps the learner acts in. Before one begins
// the program is marked: its case and rules, the invoices open, and what the screen shows, the page
// of every tab, the row of every list, the option checked and the text of every field. Passing a
// step forwards does what the learner would have; going back over it puts the marked program back.
public static partial class Coach
{
    static readonly Stack<Marked> marks = [];
    static Session? session;

    sealed record Marked(
        string? Case, string Rules, List<string> Invoices,
        List<(SelectingItemsControl List, int Index)> Chosen, List<string> Checked, List<(TextBox Box, string? Text)> Typed,
        List<Step> Undo);

    [JSExport]
    public static async Task Mark(string undo)
    {
        if (top is null || session is null) return;
        await Stored();
        var shown = top.GetVisualDescendants().Where(v => v.IsEffectivelyVisible).ToList();
        marks.Push(new Marked(
            session.Case is { } kase ? Json.Serialize(kase) : null,
            session.Rules is { } rules ? Json.Serialize(rules) : "",
            [.. shown.OfType<InvoiceView>().Select(v => v.Id)],
            [.. shown.OfType<SelectingItemsControl>().Select(l => (l, l.SelectedIndex))],
            [.. shown.OfType<RadioButton>().Where(r => r.IsChecked == true).Select(AutomationProperties.GetName).OfType<string>()],
            [.. shown.OfType<TextBox>().Where(b => b.FindAncestorOfType<DataGrid>() is null).Select(b => (b, b.Text))],
            Load(undo)));
    }

    // Done as the learner would have done it, then given the time the program takes to show it. A field
    // typed in reads right at once, and once left no longer shows as the learner's check expects.
    [JSExport]
    public static async Task Do(string steps, string? done)
    {
        foreach (var step in Load(steps)) await Act(step);
        await Settle();
        if (string.IsNullOrEmpty(done) || JsonSerializer.Deserialize(done, LessonJson.Default.Done) is not { Text: null } goal) return;
        await Until(() => Find(goal.At) is { } hit && Met(hit, goal), 60);
    }

    // An invoice open while its case changes back is closed first, so it stores nothing over the
    // case put back, and opened again on it.
    // A step the learner did not finish needs only the program put back, not its own `undo`.
    [JSExport]
    public static async Task Undo(bool done)
    {
        if (top is null || session is null || !marks.TryPop(out var mark)) return;
        await Stored();
        var kase = session.Case is { } now ? Json.Serialize(now) : null;
        foreach (var view in top.GetVisualDescendants().OfType<InvoiceView>().ToList())
            if (kase != mark.Case || !mark.Invoices.Contains(view.Id)) Frame.Of(view)?.Close();
        await Settle();
        if (kase != mark.Case) await Return(mark.Case);
        await Revert(mark.Rules);
        await Settle();
        foreach (var id in mark.Invoices)
            if (!top.GetVisualDescendants().OfType<InvoiceView>().Any(v => v.Id == id)) session.OpenInvoice(id);
        foreach (var step in done ? mark.Undo : []) await Act(step);
        if (mark.Chosen.Where(c => c.List is TabControl).Count(Choose) > 0) await Settle(4);
        if (mark.Chosen.Where(c => c.List is not TabControl).Count(Choose) > 0) await Settle(4);
        foreach (var radio in top.GetVisualDescendants().OfType<RadioButton>())
            if (mark.Checked.Contains(AutomationProperties.GetName(radio) ?? "")) radio.IsChecked = true;
        foreach (var (box, text) in mark.Typed)
            if (box.IsAttachedToVisualTree() && box.Text != text) box.Text = text;
        await Settle();
    }

    // An invoice stores its edits a moment after they are made, and only then are they in the case; the
    // rules store theirs after a pause in typing, so they are told to at once.
    static async Task Stored()
    {
        for (var i = 0; i < 100 && top!.GetVisualDescendants().OfType<InvoiceView>().Any(v => v.DataContext is InvoiceModel { Dirty: true }); i++)
            await Settle();
        foreach (var rules in top!.GetVisualDescendants().OfType<RulesView>().ToList()) await rules.Store();
        await Settle();
    }

    static bool Choose((SelectingItemsControl List, int Index) chosen)
    {
        var (list, index) = chosen;
        if (!list.IsAttachedToVisualTree() || list.SelectedIndex == index || index >= list.ItemCount) return false;
        list.SelectedIndex = index;
        return true;
    }

    // A case that was not there yet is deleted and left, so the list of cases no longer shows it; one
    // that was left is opened again.
    static async Task Return(string? json)
    {
        if (json is null)
        {
            if (session!.Case is not { } made) return;
            await session.Service.Cases.Delete(made.Id, CancellationToken.None);
            Click(await Reach(new Target { Name = "BackButton" }));
            return;
        }
        var kase = Json.Deserialize<Case>(json);
        if (session!.Case is null) session.Open(kase);
        await session.Restore(kase);
    }

    // The rules go back in one write: entries that changed together, as a unit and the recipe counting
    // in it, fit only together. A store that refuses says so; the lesson does not go on half rewound.
    static async Task Revert(string rules)
    {
        if (rules == "" || session!.Rules is not { } now) return;
        var back = now.Back(Json.Deserialize<RuleSet>(rules));
        if (back.Count > 0 && !await session.Restore(back))
            throw new InvalidOperationException("Die Regeln ließen sich nicht zurücksetzen: " + session.Error);
    }

    static List<Step> Load(string json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize(json, LessonJson.Default.ListStep) ?? [];

    // The actions of the headless driver, on the one view the browser has.
    static async Task Act(Step step)
    {
        var at = step is IAimed { At: { } target } ? await Reach(target) : null;
        switch (step)
        {
            case ClickStep:
                Click(at!);
                break;
            case TypeStep s:
                Type(at!, s);
                break;
            case FocusStep:
                if (at is InputElement input) input.Focus();
                else top!.FocusManager?.Focus(null);
                break;
            case DeselectStep:
                Select(at!, null);
                break;
            case SelectStep:
                Select(at!, (at as StyledElement)?.DataContext);
                break;
            case TopStep:
                var grid = at as DataGrid ?? (DataGrid)Targets.Up(at!, "DataGrid");
                if (grid.ItemsSource?.Cast<object>().FirstOrDefault() is { } first) grid.ScrollIntoView(first, null);
                break;
            case EditStep s:
                await Edit(at!, s.Column, s.Text);
                break;
            case OpenStep s:
                if (session!.Case?.Invoices.Find(i => i.Number == s.Number) is { } invoice) session.OpenInvoice(invoice.Id);
                break;
            case TabStep s:
                var header = new Target { Text = s.Header, Type = "TabItem" };
                var item = Topmost() is { } open && Targets.Seek(open, header) is { } inside ? inside : await Reach(header);
                ((TabControl)Targets.Up(item, "TabControl")).SelectedItem = item;
                break;
            case ChooseStep s:
                await Pick((AutoCompleteBox)at!, s.Text, s.Item);
                break;
            case WaitStep s:
                await Settle(s.Rounds);
                break;
            case PressStep:
                var row = (at as StyledElement)?.DataContext as CaseRow ?? throw new NotSupportedException("not a case: " + at!.GetType().Name);
                session!.Open(await session.Service.Cases.Get(row.Case.Id, CancellationToken.None));
                break;
            case CloseStep:
                Click(Targets.Seek(Topmost() ?? throw new InvalidOperationException("no sheet to close"), new Target { Tip = "Schließen (Esc)" })!);
                break;
            default:
                throw new NotSupportedException("not in the browser: " + step.GetType().Name);
        }
        await Settle();
    }

    // The sheet on top, where one is open: what the program in a window would call the dialog.
    static Border? Topmost() =>
        top!.GetVisualDescendants().OfType<Sheets>().FirstOrDefault()?.Children.OfType<Border>().MaxBy(c => c.ZIndex);

    static async Task<Visual> Reach(Target target)
    {
        Visual? hit = null;
        bool Found()
        {
            hit = Targets.Seek(top!, target);
            if (hit is null) Targets.Reveal(top!, target);
            return hit is not null;
        }
        return await Until(Found, 400) ? hit! : throw new InvalidOperationException("not found: " + target);
    }

    // Rounds in which the services work, as while the matcher's cache loads over a slow line, count
    // a twentieth: the program is not late yet, but a call that never answers ends it too.
    static async Task<bool> Until(Func<bool> met, int rounds)
    {
        for (var waited = 0; waited < 20 * rounds; waited += transport.Running > 0 ? 1 : 20)
        {
            if (met()) return true;
            await Settle();
        }
        return met();
    }

    static void Click(Visual visual)
    {
        var button = visual as Button ?? visual.GetVisualAncestors().OfType<Button>().FirstOrDefault()
            ?? visual.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible)
            ?? throw new InvalidOperationException("not a button: " + visual.GetType().Name);
        if (button is RadioButton radio) radio.IsChecked = true;
        else
        {
            if (button is ToggleButton toggle) toggle.IsChecked = toggle.IsChecked != true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    // A field, or the first text box inside what `at` found (the price in a row).
    static void Type(Visual visual, TypeStep step)
    {
        if (AvaloniaPropertyRegistry.Instance.GetRegistered(visual).FirstOrDefault(p => p.Name == "Text") is { } property)
            visual.SetValue(property, step.Into(visual.GetValue(property) as string));
        else if (visual.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.IsEffectivelyVisible) is { } box)
            box.Text = step.Into(box.Text);
        else
            throw new InvalidOperationException("no text field in " + visual.GetType().Name);
    }

    static void Select(Visual row, object? item)
    {
        switch (row.GetSelfAndVisualAncestors().FirstOrDefault(v => v is DataGrid or SelectingItemsControl))
        {
            case DataGrid grid: grid.SelectedItem = item; break;
            case SelectingItemsControl list: list.SelectedItem = item; break;
            default: throw new InvalidOperationException("no list above " + row.GetType().Name);
        }
    }

    static async Task Edit(Visual cell, string column, string? text)
    {
        var grid = (DataGrid)Targets.Up(cell, "DataGrid");
        grid.SelectedItem = (cell as StyledElement)?.DataContext;
        grid.CurrentColumn = grid.Columns.FirstOrDefault(c => c.Header as string == column)
            ?? throw new InvalidOperationException("no column " + column);
        if (text is null) return;
        grid.ScrollIntoView(grid.SelectedItem, grid.CurrentColumn);
        await Settle();
        grid.BeginEdit();
        await Settle();
        var box = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.IsFocused)
            ?? throw new InvalidOperationException("no editor in column " + column);
        box.Text = text;
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    // Typed into the search box, and the entry that reads `item` taken from its drop-down.
    static async Task Pick(AutoCompleteBox box, string text, string item)
    {
        box.Focus();
        await Settle();
        box.GetVisualDescendants().OfType<TextBox>().First().Text = text;
        TextBlock? entry = null;
        bool Offered() => (entry = box.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child?
            .GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == item)) is not null;
        if (!await Until(Offered, 60)) throw new InvalidOperationException("not offered: " + item);
        box.SelectedItem = entry!.DataContext;
    }

    static async Task Settle(int rounds = 1)
    {
        for (var i = 0; i < rounds; i++) await Task.Delay(50);
        if (session is not null) await session.Saved;
    }
}
