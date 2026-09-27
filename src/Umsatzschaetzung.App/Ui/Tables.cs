using System.Collections;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Umsatzschaetzung.App.Ui;

// The grid builds its rows, cells and headers itself and gives them peers without names, so each
// part gets its peer with its template, before anyone asks for one. VoiceOver reads a grid's size
// from attributes Avalonia never fills, so on the Mac the grid is a group. The keyboard stays on
// the grid, so entering it and moving in it are announced.
public static class Tables
{
    static readonly AttachedProperty<object?> ReadProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, object?>("Read", typeof(Tables));

    static readonly AttachedProperty<bool> QueuedProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("Queued", typeof(Tables));

    public static void Register()
    {
        InputElement.KeyDownEvent.AddClassHandler<DataGrid>(Pressed);
        if (OperatingSystem.IsBrowser()) return;
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<DataGrid>(Watch);
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<DataGridRow>(Watch);
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<DataGridCell>((cell, _) => Accessible.Install(cell, c => new CellPeer(c)));
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<DataGridColumnHeader>((header, _) => Accessible.Install(header, h => new HeaderPeer(h)));
        InputElement.IsKeyboardFocusWithinProperty.Changed.AddClassHandler<DataGrid>(Entered);
        ToolTip.ToolTipOpeningEvent.AddClassHandler<TextBlock>(Opening);
    }

    static void Watch(DataGrid grid, TemplateAppliedEventArgs e)
    {
        Accessible.Install(grid, g => new TablePeer(g));
        Install(e, "PART_ColumnHeadersPresenter", p => new HeaderRowPeer((DataGridColumnHeadersPresenter)p));
        Install(e, "PART_RowsPresenter", p => new PartsPeer<DataGridRow>(p, r => new RowPeer(r)));
        Install(e, "PART_TopLeftCornerHeader", c => new NoneAutomationPeer(c));
        Install(e, "PART_TopRightCornerHeader", c => new NoneAutomationPeer(c));
        grid.CurrentCellChanged -= Moved;
        grid.CurrentCellChanged += Moved;
    }

    static void Watch(DataGridRow row, TemplateAppliedEventArgs e)
    {
        Accessible.Install(row, r => new RowPeer(r));
        Install(e, "PART_CellsPresenter", p => new PartsPeer<DataGridCell>(p, c => new CellPeer(c)));
        Install(e, "PART_DetailsPresenter", d => new NoneAutomationPeer(d));
    }

    static void Install(TemplateAppliedEventArgs e, string part, Func<Control, AutomationPeer> create)
    {
        if (e.NameScope.Find<Control>(part) is { } control) Accessible.Install(control, create);
    }

    static void Entered(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        grid.ClearValue(ReadProperty);
        Queue(grid);
    }

    static void Moved(object? sender, EventArgs e)
    {
        if (sender is DataGrid { IsFocused: true } grid) Queue(grid);
    }

    static void Queue(DataGrid grid)
    {
        if (grid.GetValue(QueuedProperty)) return;
        grid.SetValue(QueuedProperty, true);
        Dispatcher.UIThread.Post(() =>
        {
            grid.SetValue(QueuedProperty, false);
            Speak(grid);
        }, DispatcherPriority.Background);
    }

    static void Opening(TextBlock block, CancelRoutedEventArgs e)
    {
        if (ToolTip.GetTip(block) is string tip && tip == block.Text && !block.TextLayout.TextLines.Any(l => l.HasCollapsed) && block.FindAncestorOfType<DataGridCell>() is not null)
            e.Cancel = true;
    }

    static void Speak(DataGrid grid)
    {
        if (!grid.IsFocused || grid.SelectedItem is not { } item || Row(grid, item) is not { } row) return;
        var cell = grid.GetValue(ReadProperty) == item && grid.CurrentColumn is { } column ? Cell(row, column) : null;
        grid.SetValue(ReadProperty, item);
        Accessible.Announce(grid, cell is not null ? Spoken(cell) : Spoken(row) + ", " + Position(grid, row));
    }

    static void Pressed(DataGrid grid, KeyEventArgs e)
    {
        if (e.Key != Key.S || e.Source is TextBox || grid.CurrentColumn is not { } column || !Sortable(grid, column)) return;
        if (grid.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers is not { } command || e.KeyModifiers != (command | KeyModifiers.Shift)) return;
        e.Handled = true;
        Sort(grid, column);
    }

    static void Sort(DataGrid grid, DataGridColumn column)
    {
        column.Sort();
        Dispatcher.UIThread.Post(() =>
        {
            if (Header(grid, column) is { } header) Accessible.Announce(grid, Caption(column) + ", " + (Sorting(header) ?? "unsortiert"));
        }, DispatcherPriority.Background);
    }

    static string Shortcut(Control control) =>
        control.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers == KeyModifiers.Meta ? "Befehl+Umschalt+S" : "Strg+Umschalt+S";

    static bool Sortable(DataGrid grid, DataGridColumn column) =>
        grid.CanUserSortColumns && column.CanUserSort
        && (!string.IsNullOrEmpty(column.SortMemberPath) || column.CustomSortComparer is not null || column is DataGridBoundColumn);

    static string? Sorting(DataGridColumnHeader header) =>
        header.Classes.Contains(":sortascending") ? "aufsteigend sortiert"
        : header.Classes.Contains(":sortdescending") ? "absteigend sortiert"
        : null;

    static DataGridRow? Row(DataGrid grid, object item) =>
        grid.Columns.Count == 0 ? null : grid.Columns[0].GetCellContent(item)?.FindAncestorOfType<DataGridRow>();

    static DataGridCell? Cell(DataGridRow row, DataGridColumn column) =>
        column.GetCellContent(row)?.FindAncestorOfType<DataGridCell>();

    static DataGridColumnHeader? Header(DataGrid grid, DataGridColumn column) =>
        grid.GetVisualDescendants().OfType<DataGridColumnHeader>().FirstOrDefault(h => DataGridColumn.GetColumnContainingElement(h) == column);

    static IEnumerable<DataGridColumn> Shown(DataGrid grid) => grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex);

    static int Count(DataGrid grid) =>
        grid.ItemsSource is null ? 0 : grid.CollectionView is ICollection c ? c.Count : grid.ItemsSource is ICollection s ? s.Count : -1;

    static string Position(DataGrid grid, DataGridRow row) =>
        Count(grid) is >= 0 and var count ? $"Zeile {row.Index + 1} von {count}" : $"Zeile {row.Index + 1}";

    static string Size(DataGrid grid) =>
        (Count(grid) is >= 0 and var count ? $"{count} Zeilen, " : "") + $"{Shown(grid).Count()} Spalten"
        + (grid.Columns.Any(c => Sortable(grid, c)) ? $". {Shortcut(grid)} sortiert nach der gewählten Spalte" : "");

    static string Caption(DataGridColumn column) => column.Header switch
    {
        string text => text,
        Visual visual => Text(visual),
        _ => "",
    };

    static string Spoken(DataGridRow row) =>
        AutomationProperties.GetName(row) is { Length: > 0 } name ? name
        : row.FindAncestorOfType<DataGrid>() is not { } grid ? ""
        : string.Join(", ", Shown(grid)
            .Select(c => (Caption: Caption(c), Value: c.GetCellContent(row) is { } content ? Text(content) : ""))
            .Where(c => c.Value != "")
            .Select(c => c.Caption == "" ? c.Value : c.Caption + ": " + c.Value));

    static string Spoken(DataGridCell cell)
    {
        if (AutomationProperties.GetName(cell) is { Length: > 0 } name) return name;
        var value = Text(cell) is { Length: > 0 } text ? text : Actions(cell);
        var caption = DataGridColumn.GetColumnContainingElement(cell) is { } column ? Caption(column) : "";
        return caption == "" ? value : caption + ": " + (value == "" ? "leer" : value);
    }

    static string Text(Visual visual)
    {
        List<string> parts = [];
        Collect(visual, parts);
        return string.Join(" ", parts);
    }

    // What the cell shows, not the controls in it: those are read on their own.
    static void Collect(Visual visual, List<string> parts)
    {
        if (visual is Control control)
        {
            if (!control.IsVisible || AutomationProperties.GetAccessibilityView(control) == AccessibilityView.Raw) return;
            if (control is TextBox box)
            {
                if (!string.IsNullOrWhiteSpace(box.Text)) parts.Add(box.Text.Trim());
                return;
            }
            if (control.Focusable && control is not DataGridCell) return;
            if (AutomationProperties.GetName(control) is { Length: > 0 } name)
            {
                parts.Add(name);
                return;
            }
            if (control is TextBlock { Text: { } text } && !string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text.Trim());
                return;
            }
        }
        foreach (var child in visual.GetVisualChildren()) Collect(child, parts);
    }

    static string Actions(Visual cell) => string.Join(", ", cell.GetVisualDescendants().OfType<Control>()
        .Where(c => c.Focusable && c.IsEffectivelyVisible && c is not TextBox)
        .Select(c => ControlAutomationPeer.CreatePeerForElement(c).GetName())
        .Where(n => !string.IsNullOrWhiteSpace(n)));

    static string? Hint(Visual visual) => visual.GetVisualDescendants().OfType<Control>()
        .Where(c => c.IsEffectivelyVisible)
        .Select(c => AutomationProperties.GetHelpText(c) ?? Tip(c))
        .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));

    static string? Tip(Control control) =>
        ToolTip.GetTip(control) is string tip && !(control is TextBlock block && block.Text == tip) ? tip : null;

    static string? Or(string? given, string? made) => string.IsNullOrWhiteSpace(given) ? made : given;

    sealed class TablePeer(DataGrid owner) : DataGridAutomationPeer(owner), ISelectionProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() =>
            OperatingSystem.IsMacOS() ? AutomationControlType.Group : base.GetAutomationControlTypeCore();

        protected override string GetLocalizedControlTypeCore() => "Tabelle";

        protected override string? GetNameCore() =>
            Count(Owner) != 0 ? base.GetNameCore() : base.GetNameCore() is { Length: > 0 } name ? name + ", keine Einträge" : "keine Einträge";

        protected override string? GetHelpTextCore() =>
            base.GetHelpTextCore() is { Length: > 0 } help ? help + ". " + Size(Owner) : Size(Owner);

        public bool CanSelectMultiple => Owner.SelectionMode == DataGridSelectionMode.Extended;

        public bool IsSelectionRequired => false;

        public IReadOnlyList<AutomationPeer> GetSelection() => Owner.SelectedItems.Cast<object>()
            .Select(item => Row(Owner, item))
            .OfType<DataGridRow>()
            .Select(GetOrCreate)
            .ToList();
    }

    sealed class RowPeer : DataGridRowAutomationPeer, ISelectionItemProvider
    {
        public RowPeer(DataGridRow row) : base(row) => row.PropertyChanged += (_, e) =>
        {
            if (e.Property == DataGridRow.IsSelectedProperty)
                RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, e.OldValue, e.NewValue);
        };

        DataGridRow Row => (DataGridRow)Owner;

        DataGrid? Grid => Owner.FindAncestorOfType<DataGrid>();

        protected override string GetLocalizedControlTypeCore() => "Zeile";

        protected override string? GetNameCore() => Or(base.GetNameCore(), Spoken(Row));

        protected override string? GetHelpTextCore() => Or(base.GetHelpTextCore(), Grid is { } grid ? Position(grid, Row) : null);

        public bool IsSelected => Row.IsSelected;

        public ISelectionProvider? SelectionContainer => Grid is { } grid ? GetOrCreate(grid).GetProvider<ISelectionProvider>() : null;

        public void Select()
        {
            EnsureEnabled();
            if (Grid is { } grid) grid.SelectedItem = Row.DataContext;
        }

        public void AddToSelection()
        {
            EnsureEnabled();
            if (Grid is { } grid && Row.DataContext is { } item && !grid.SelectedItems.Contains(item)) grid.SelectedItems.Add(item);
        }

        public void RemoveFromSelection()
        {
            EnsureEnabled();
            Grid?.SelectedItems.Remove(Row.DataContext);
        }
    }

    sealed class CellPeer(DataGridCell cell) : DataGridCellAutomationPeer(cell), IValueProvider
    {
        DataGridCell Cell => (DataGridCell)Owner;

        DataGridColumn? Column => DataGridColumn.GetColumnContainingElement(Cell);

        DataGrid? Grid => Owner.FindAncestorOfType<DataGrid>();

        protected override bool IsControlElementCore() => Column is { } column && Grid?.Columns.Contains(column) == true;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;

        protected override string GetLocalizedControlTypeCore() => IsReadOnly ? "Zelle" : "bearbeitbare Zelle";

        protected override string? GetNameCore() => Spoken(Cell);

        protected override string? GetHelpTextCore() => Or(base.GetHelpTextCore(), Hint(Cell));

        protected override object? GetProviderCore(Type providerType) =>
            providerType == typeof(IValueProvider) && IsReadOnly ? null : base.GetProviderCore(providerType);

        public bool IsReadOnly => Grid is not { IsReadOnly: false } || Column is not { IsReadOnly: false };

        public string? Value => Text(Cell);

        public void SetValue(string? value)
        {
            EnsureEnabled();
            if (IsReadOnly || Grid is not { } grid || Column is not { } column || Cell.DataContext is not { } item)
                throw new InvalidOperationException();
            grid.SelectedItem = item;
            grid.CurrentColumn = column;
            Cells.Write(grid, column, item, value ?? "");
        }
    }

    sealed class HeaderRowPeer(DataGridColumnHeadersPresenter presenter) : DataGridColumnHeadersPresenterAutomationPeer(presenter)
    {
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
        {
            foreach (var header in Owner.Children.OfType<DataGridColumnHeader>()) Accessible.Install(header, h => new HeaderPeer(h));
            return base.GetChildrenCore();
        }

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            OperatingSystem.IsMacOS() ? AutomationControlType.Group : base.GetAutomationControlTypeCore();

        protected override string GetLocalizedControlTypeCore() => "Spaltenköpfe";
    }

    sealed class HeaderPeer(DataGridColumnHeader header) : DataGridColumnHeaderAutomationPeer(header), IInvokeProvider
    {
        DataGridColumnHeader Header => (DataGridColumnHeader)Owner;

        DataGrid? Grid => Owner.FindAncestorOfType<DataGrid>();

        DataGridColumn? Column =>
            DataGridColumn.GetColumnContainingElement(Header) is { } column && Grid?.Columns.Contains(column) == true ? column : null;

        bool CanSort => Grid is { } grid && Column is { } column && Sortable(grid, column);

        protected override bool IsControlElementCore() => Column is not null && !string.IsNullOrEmpty(GetNameCore());

        protected override string GetLocalizedControlTypeCore() => "Spaltenkopf";

        protected override string? GetNameCore()
        {
            if (AutomationProperties.GetName(Header) is { Length: > 0 } name) return name;
            var caption = Column is { } column ? Caption(column) : "";
            return Sorting(Header) is { } sorting ? caption + ", " + sorting : caption;
        }

        protected override string? GetHelpTextCore() =>
            Or(base.GetHelpTextCore(), CanSort ? $"Aktivieren oder {Shortcut(Header)} in dieser Spalte sortiert die Tabelle" : null);

        protected override object? GetProviderCore(Type providerType) =>
            providerType == typeof(IInvokeProvider) && !CanSort ? null : base.GetProviderCore(providerType);

        public void Invoke()
        {
            EnsureEnabled();
            if (Grid is { } grid && Column is { } column && Sortable(grid, column)) Sort(grid, column);
        }
    }

    // A part that is hidden when its parent is first read gets no template yet, and so no peer of ours.
    sealed class PartsPeer<T>(Control owner, Func<T, AutomationPeer> create) : NoneAutomationPeer(owner) where T : Control
    {
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
        {
            foreach (var part in Owner.GetVisualChildren().OfType<T>()) Accessible.Install(part, create);
            return base.GetChildrenCore();
        }
    }
}
