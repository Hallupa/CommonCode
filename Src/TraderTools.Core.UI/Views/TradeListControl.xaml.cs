using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TraderTools.Basics;

namespace TraderTools.Core.UI.Views
{
    [Flags]
    public enum TradeListDisplayOptionsFlag
    {
        None = 0,
        PoundsPerPip = 1,
        Quantity = 2,
        Stop = 4,
        Limit = 8,
        OrderDate = 16,
        Broker = 32,
        Comments = 64,
        ResultR = 128,
        OrderPrice = 256,
        ClosePrice = 512,
        Timeframe = 1024,
        EntryValue = 2048,
        Strategies = 4096,
        Risk = 8192,
        Status = 16384,
        Rollover = 32768
    }

    /// <summary>
    /// Interaction logic for TradeListControl.xaml
    /// </summary>
    public partial class TradeListControl : UserControl
    {
        public TradeListControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty ShowTradeEntryOnlyProperty = DependencyProperty.Register(
            "ShowTradeEntryOnly", typeof(bool), typeof(TradeListControl), new PropertyMetadata(false, ShowTradeEntryOnlyPropertyChangedCallback));

        public static readonly DependencyProperty ShowBasicDetailsOnlyProperty = DependencyProperty.Register(
            "ShowBasicDetailsOnly", typeof(bool), typeof(TradeListControl), new PropertyMetadata(default(bool), ShowBasicDetailsPropertyChangedCallback));

        public static readonly DependencyProperty AllColumnsReadOnlyProperty = DependencyProperty.Register(
            "AllColumnsReadOnly", typeof(bool), typeof(TradeListControl), new PropertyMetadata(default(bool), AllColumnsReadOnlyPropertyChanged));

        public static readonly DependencyProperty HideContextMenuProperty = DependencyProperty.Register(
            "HideContextMenu", typeof(bool), typeof(TradeListControl), new PropertyMetadata(default(bool), HideContextMenuPropertyChangedCallback));

        public static readonly DependencyProperty HideContextMenuDeleteOptionProperty = DependencyProperty.Register(
            "HideContextMenuDeleteOption", typeof(bool), typeof(TradeListControl), new PropertyMetadata(default(bool), HideContextMenuDeleteOptionChanged));

        private static void HideContextMenuDeleteOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (TradeListControl)d;
            var hide = (bool)e.NewValue;
            c.MainContextMenuDeleteMenuItem.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        }

        public bool HideContextMenuDeleteOption
        {
            get { return (bool)GetValue(HideContextMenuDeleteOptionProperty); }
            set { SetValue(HideContextMenuDeleteOptionProperty, value); }
        }

        public static readonly DependencyProperty HideContextMenuEditOptionProperty = DependencyProperty.Register(
            "HideContextMenuEditOption", typeof(bool), typeof(TradeListControl), new PropertyMetadata(default(bool), HideContextMenuEditOptionChanged));

        private static void HideContextMenuEditOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (TradeListControl)d;
            var hide = (bool)e.NewValue;
            c.MainContextMenuEditMenuItem.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        }

        public bool HideContextMenuEditOption
        {
            get { return (bool)GetValue(HideContextMenuEditOptionProperty); }
            set { SetValue(HideContextMenuEditOptionProperty, value); }
        }

        private static void HideContextMenuPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (TradeListControl)d;
            var hide = (bool)e.NewValue;
            c.MainContextMenu.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        }

        public bool HideContextMenu
        {
            get { return (bool)GetValue(HideContextMenuProperty); }
            set { SetValue(HideContextMenuProperty, value); }
        }

        private static void AllColumnsReadOnlyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (TradeListControl)d;
            foreach (var column in c.MainDataGrid.Columns)
            {
                column.IsReadOnly = true;
            }
        }

        public bool AllColumnsReadOnly
        {
            get { return (bool)GetValue(AllColumnsReadOnlyProperty); }
            set { SetValue(AllColumnsReadOnlyProperty, value); }
        }

        private static void ShowBasicDetailsPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (TradeListControl)d;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "£/pip").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "Risking").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "Risk %").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "Profit").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
        }

        public bool ShowBasicDetailsOnly
        {
            get { return (bool)GetValue(ShowBasicDetailsOnlyProperty); }
            set { SetValue(ShowBasicDetailsOnlyProperty, value); }
        }

        private static void ShowTradeEntryOnlyPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (TradeListControl)d;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "£/pip").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "Risking").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "Risk %").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "Profit").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "Result R").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            c.MainDataGrid.Columns.First(x => (string)x.Header == "Status").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            //c.MainDataGrid.Columns.First(x => (string)x.Header == "Entry date").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
            //c.MainDataGrid.Columns.First(x => (string)x.Header == "Close date").Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
        }

        public bool ShowTradeEntryOnly
        {
            get { return (bool)GetValue(ShowTradeEntryOnlyProperty); }
            set { SetValue(ShowTradeEntryOnlyProperty, value); }
        }

        public static readonly DependencyProperty DisableMouseWheelScrollProperty = DependencyProperty.Register(
            "DisableMouseWheelScroll", typeof(bool), typeof(TradeListControl), new PropertyMetadata(false));

        public bool DisableMouseWheelScroll
        {
            get { return (bool)GetValue(DisableMouseWheelScrollProperty); }
            set { SetValue(DisableMouseWheelScrollProperty, value); }
        }

        private void MainDataGrid_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (DisableMouseWheelScroll)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                var parent = ((Control)sender).Parent as UIElement;
                parent.RaiseEvent(eventArg);
            }
        }

        public static readonly DependencyProperty TradeDoubleClickCommandProperty = DependencyProperty.Register(
            "TradeDoubleClickCommand", typeof(ICommand), typeof(TradeListControl), new PropertyMetadata(default(ICommand)));

        public ICommand TradeDoubleClickCommand
        {
            get { return (ICommand) GetValue(TradeDoubleClickCommandProperty); }
            set { SetValue(TradeDoubleClickCommandProperty, value); }
        }

        private void DataGridRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = (DataGridRow)sender;
            var trade = (Trade)row.DataContext;

            e.Handled = true;
            TradeDoubleClickCommand?.Execute(trade);
        }
    }
}