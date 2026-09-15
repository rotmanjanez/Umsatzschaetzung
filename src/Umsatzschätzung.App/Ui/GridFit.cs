using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Umsatzschätzung.App.Ui;

public static class GridFit
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(GridFit), new PropertyMetadata(false, EnabledChanged));

    static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(GridFit));

    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DataGrid grid) => (bool)grid.GetValue(EnabledProperty);

    static void EnabledChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not DataGrid grid) return;
        if (grid.GetValue(StateProperty) is State old) old.Detach();
        grid.SetValue(StateProperty, (bool)e.NewValue ? new State(grid) : null);
    }

    sealed class State
    {
        readonly DataGrid grid;
        bool fitted;
        bool pending;

        public State(DataGrid grid)
        {
            this.grid = grid;
            grid.Loaded += Loaded;
            ((INotifyCollectionChanged)grid.Items).CollectionChanged += Items;
            if (grid.IsLoaded) Schedule();
        }

        public void Detach()
        {
            grid.Loaded -= Loaded;
            ((INotifyCollectionChanged)grid.Items).CollectionChanged -= Items;
        }

        void Loaded(object? sender, RoutedEventArgs e) => Schedule();

        void Items(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) fitted = false;
            Schedule();
        }

        void Schedule()
        {
            if (pending || fitted) return;
            pending = true;
            grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Fit);
        }

        void Fit()
        {
            pending = false;
            if (fitted || !grid.IsLoaded || grid.Items.Count == 0) return;
            fitted = true;
            foreach (var column in grid.Columns)
                if (!column.Width.IsStar) column.Width = DataGridLength.Auto;
            grid.UpdateLayout();
            foreach (var column in grid.Columns)
                if (!column.Width.IsStar) column.Width = new DataGridLength(column.ActualWidth);
        }
    }
}
