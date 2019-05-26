using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hallupa.Library.UI.TokenEditor
{
    /// <summary>
    /// Interaction logic for TokenContainerControl.xaml
    /// </summary>
    public partial class TokenContainerControl : UserControl
    {
        public TokenContainerControl()
        {
            InitializeComponent();

            DeleteTokenCommand = new DelegateCommand(DeleteToken);
        }

        private void DeleteToken(object obj)
        {
            var items = SelectedItems as IList;
            items?.Remove(obj);
        }

        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            "ItemsSource", typeof(IEnumerable), typeof(TokenContainerControl), new PropertyMetadata(default(IEnumerable)));

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        public static readonly DependencyProperty DeleteTokenCommandProperty = DependencyProperty.Register(
            "DeleteTokenCommand", typeof(ICommand), typeof(TokenContainerControl), new PropertyMetadata(default(ICommand)));

        public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
            "SelectedItems", typeof(IEnumerable), typeof(TokenContainerControl), new PropertyMetadata(default(IEnumerable)));

        public IEnumerable SelectedItems
        {
            get { return (IEnumerable)GetValue(SelectedItemsProperty); }
            set { SetValue(SelectedItemsProperty, value); }
        }

        public ICommand DeleteTokenCommand
        {
            get { return (ICommand)GetValue(DeleteTokenCommandProperty); }
            set { SetValue(DeleteTokenCommandProperty, value); }
        }

        public static readonly DependencyProperty ComboBoxSelectedItemProperty = DependencyProperty.Register(
            "ComboBoxSelectedItem", typeof(object), typeof(TokenContainerControl), new PropertyMetadata(default(object), ComboBoxSelectedItemChanged));

        private static void ComboBoxSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var userControl = (TokenContainerControl)d;
            if (userControl.ComboBoxSelectedItem == null) return;

            AddItem(userControl, userControl.ComboBoxSelectedItem);
        }

        public static readonly DependencyProperty ComboBoxTextProperty = DependencyProperty.Register(
            "ComboBoxText", typeof(string ), typeof(TokenContainerControl), new PropertyMetadata(default(string), PropertyChangedCallback));

        private static void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
        }

        public string ComboBoxText
        {
            get { return (string ) GetValue(ComboBoxTextProperty); }
            set { SetValue(ComboBoxTextProperty, value); }
        }

        public object ComboBoxSelectedItem
        {
            get { return (object)GetValue(ComboBoxSelectedItemProperty); }
            set { SetValue(ComboBoxSelectedItemProperty, value); }
        }

        private void PART_EditableTextBox_OnGotFocus(object sender, RoutedEventArgs e)
        {
            MainComboBox.IsDropDownOpen = true;
        }

        private void PART_EditableTextBox_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MainComboBox.IsDropDownOpen = true;
        }

        private void PART_EditableTextBox_OnKeyUp(object sender, KeyEventArgs e)
        {
            var tb = (TextBox)sender;

            if (e.Key == Key.Enter)
            {
                AddItem(this, tb.Text);
                tb.Text = string.Empty;
            }

            /*
            var itemsViewOriginal = (CollectionView)CollectionViewSource.GetDefaultView(ItemsSource);

            itemsViewOriginal.Filter = ((o) =>
            {
                if (o == null)
                {
                    return true;
                }

                if (String.IsNullOrEmpty(tb.Text)) return true;
                else
                {
                    if (((string)o).Contains(tb.Text)) return true;
                    else return false;
                }
            });

            itemsViewOriginal.Refresh();*/
        }

        private void PART_EditableTextBox_OnLostFocus(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;
            AddItem(this, tb.Text);
        }

        private static void AddItem(TokenContainerControl userControl, object item)
        {
            if (item == null || (item is string str && string.IsNullOrEmpty(str)))
            {
                return;
            }

            // Add token
            if (userControl.SelectedItems is IList selectedItemsList && !selectedItemsList.Contains(item))
            {
                selectedItemsList.Add(item);
            }

            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke((Action)(() =>
            {
                userControl.ComboBoxSelectedItem = null;
                userControl.ComboBoxText = string.Empty;
            }));
        }
    }
}
