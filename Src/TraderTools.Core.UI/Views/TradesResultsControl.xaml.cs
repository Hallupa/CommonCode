using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TraderTools.Core.UI.Views
{
    /// <summary>
    /// Interaction logic for SimulationResultsControl.xaml
    /// </summary>
    public partial class TradesResultsControl : UserControl
    {
        public TradesResultsControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty DisableMouseWheelScrollProperty = DependencyProperty.Register(
            "DisableMouseWheelScroll", typeof(bool), typeof(TradesResultsControl), new PropertyMetadata(false));

        public bool DisableMouseWheelScroll
        {
            get { return (bool)GetValue(DisableMouseWheelScrollProperty); }
            set { SetValue(DisableMouseWheelScrollProperty, value); }
        }

        private void RowDoubleClick(object sender, RoutedEventArgs e)
        {
            var row = (DataGridRow)sender;
            row.DetailsVisibility = row.DetailsVisibility == Visibility.Collapsed ?
                Visibility.Visible : Visibility.Collapsed;
        }

        private void UIElement_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
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
    }
}