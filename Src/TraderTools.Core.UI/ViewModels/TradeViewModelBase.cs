using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hallupa.Library;
using Hallupa.Library.UI;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Core.Services;
using TraderTools.Core.UI.Services;
using TraderTools.Core.UI.Views;
using TraderTools.Indicators;
using TraderTools.UI.Views;

namespace TraderTools.Core.UI.ViewModels
{
    public class AnnotationDetails
    {
        public string Y1 { get; set; }
        public string Y2 { get; set; }
        public string X1 { get; set; }
        public string X2 { get; set; }
    }

    public enum MainIndicators
    {
        EMA8_EMA25_EMA50,
        EMA20_MA50_MA200
    }

    public abstract class TradeViewModelBase : DoubleChartViewModel, INotifyPropertyChanged
    {
        [Import] public IBrokersCandlesService BrokerCandles { get; private set; }
        [Import] private BrokersService _brokers;

        protected IBroker Broker { get; set; }

        private int _selectedMainIndicatorsIndex;
        private Dispatcher _dispatcher;

        protected TradeViewModelBase()
        {
            DependencyContainer.ComposeParts(this);

            TimeFrameItems = new List<Timeframe>
            {
                Timeframe.D1,
                Timeframe.H8,
                Timeframe.H4,
                Timeframe.H2,
                Timeframe.H1,
                Timeframe.M1,
            };

            EditCommand = new DelegateCommand(o => EditTrade());
            DeleteCommand = new DelegateCommand(o => DeleteTrade());
            ViewTradeCommand = new DelegateCommand(t => ViewTrade((TradeDetails)t));
            ViewTradeSetupCommand = new DelegateCommand(t => ViewTradeSetup((TradeDetails)t));

            TradesView = (CollectionView)CollectionViewSource.GetDefaultView(Trades);
            TradesView.Filter = TradesViewFilter;

            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        public CollectionView TradesView { get; private set; }

        public List<Timeframe> TimeFrameItems { get; set; }

        public TradeListDisplayOptionsFlag TradeListDisplayOptions { get; set; } =
            TradeListDisplayOptionsFlag.PoundsPerPip | TradeListDisplayOptionsFlag.Stop
                                                     | TradeListDisplayOptionsFlag.Limit
                                                     | TradeListDisplayOptionsFlag.OrderPrice
                                                     | TradeListDisplayOptionsFlag.OrderDate
                                                     | TradeListDisplayOptionsFlag.Comments
                                                     | TradeListDisplayOptionsFlag.ResultR
                                                     | TradeListDisplayOptionsFlag.Broker
                                                     | TradeListDisplayOptionsFlag.Timeframe;

        protected TradeDetails TradeShowingOnChart { get; private set; }
        public DelegateCommand ViewTradeCommand { get; private set; }
        public DelegateCommand ViewTradeSetupCommand { get; private set; }

        public int SelectedMainIndicatorsIndex
        {
            get => _selectedMainIndicatorsIndex;
            set => _selectedMainIndicatorsIndex = value;
        }

        public ICommand EditCommand { get; }

        public DelegateCommand DeleteCommand { get; }

        public ObservableCollectionEx<TradeDetails> Trades { get; } = new ObservableCollectionEx<TradeDetails>();

        protected override void SelectedLargeChartTimeChanged()
        {
            //SelectedTradeUpdated();
        }

        private bool TradesViewFilter(object obj)
        {
            var t = (TradeDetails)obj;

            if (ShowOpenTradesOnly)
            {
                return t.EntryPrice != null && t.CloseDateTime == null;
            }

            if (ShowOrdersOnly)
            {
                return t.OrderPrice != null && t.EntryPrice == null && t.CloseDateTime == null;
            }

            return true;
        }

        [Import] public ChartingService ChartingService { get; private set; }

        public TradeDetails SelectedTrade { get; set; }

        public static readonly DependencyProperty ShowOpenTradesOnlyProperty = DependencyProperty.Register(
            "ShowOpenTradesOnly", typeof(bool), typeof(TradeViewModelBase), new PropertyMetadata(default(bool), ShowOpenTradesOnlyChanged));

        private static void ShowOpenTradesOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tvm = (TradeViewModelBase)d;

            if (tvm.ShowOpenTradesOnly)
            {
                tvm.ShowOrdersOnly = false;
            }

            tvm.TradesView.Refresh();
        }

        public bool ShowOpenTradesOnly
        {
            get { return (bool)GetValue(ShowOpenTradesOnlyProperty); }
            set { SetValue(ShowOpenTradesOnlyProperty, value); }
        }

        public static readonly DependencyProperty ShowOrdersOnlyProperty = DependencyProperty.Register(
            "ShowOrdersOnly", typeof(bool), typeof(TradeViewModelBase), new PropertyMetadata(default(bool), ShowOrdersOnlyChanged));

        private Window _parent;

        private static void ShowOrdersOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tvm = (TradeViewModelBase)d;

            if (tvm.ShowOrdersOnly)
            {
                tvm.ShowOpenTradesOnly = false;
            }

            tvm.TradesView.Refresh();
        }

        public bool ShowOrdersOnly
        {
            get { return (bool) GetValue(ShowOrdersOnlyProperty); }
            set { SetValue(ShowOrdersOnlyProperty, value); }
        }

        protected virtual void DeleteTrade()
        {
        }

        public void ViewTrade(TradeDetails tradeDetails)
        {
            if (tradeDetails == null) return;

            ShowTrade(tradeDetails, true);
        }

        public void ViewTradeSetup(TradeDetails tradeDetails)
        {
            if (tradeDetails == null) return;

            ShowTradeSetup(tradeDetails, true);
        }

        public void SetParentWindow(Window parent)
        {
            _parent = parent;
        }

        protected virtual void EditTrade()
        {
            if (SelectedTrade == null)
            {
                return;
            }

            var broker = _brokers.Brokers.First(b => b.Name == SelectedTrade.Broker);
            if (broker.Status != ConnectStatus.Connected)
            {
                MessageBox.Show($"{broker.Name} not logged in - this is needed to calculate pip sizes", "Unable to update account", MessageBoxButton.OK);
                return;
            }


            var view = new TradeDetailsView { Owner = _parent };
            var viewModel = new TradeDetailsViewModel(SelectedTrade, () => view.Close());
            view.DataContext = viewModel;
            view.Closing += ViewOnClosing;
            view.ShowDialog();
        }

        protected virtual void ViewOnClosing(object sender, CancelEventArgs e)
        {
        }

        protected void ShowTrade(TradeDetails trade, Timeframe smallChartTimeframe, List<ICandle> smallChartCandles, Timeframe largeChartTimeframe, List<ICandle> largeChartCandles)
        {
            _dispatcher.BeginInvoke((Action)(() =>
            {
                ChartViewModel.ChartPaneViewModels.Clear();
                ChartViewModelSmaller1.ChartPaneViewModels.Clear();
                TradeShowingOnChart = trade;

                if (trade == null)
                {
                    return;
                }

                ChartHelper.SetChartViewModelPriceData(largeChartCandles, ChartViewModel, largeChartTimeframe);

                if (SelectedMainIndicatorsIndex == (int)MainIndicators.EMA8_EMA25_EMA50)
                {
                    ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], trade.Market,
                        new ExponentialMovingAverage(8), Colors.DarkBlue, largeChartTimeframe, largeChartCandles);
                    ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], trade.Market,
                        new ExponentialMovingAverage(25), Colors.Blue, largeChartTimeframe, largeChartCandles);
                    ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], trade.Market,
                        new ExponentialMovingAverage(50), Colors.LightBlue, largeChartTimeframe, largeChartCandles);
                }
                else if (SelectedMainIndicatorsIndex == (int)MainIndicators.EMA20_MA50_MA200)
                {
                    ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], trade.Market,
                        new ExponentialMovingAverage(8), Colors.DarkBlue, largeChartTimeframe, largeChartCandles);
                    ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], trade.Market,
                        new SimpleMovingAverage(50), Colors.Blue, largeChartTimeframe, largeChartCandles);
                    ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], trade.Market,
                        new SimpleMovingAverage(200), Colors.LightBlue, largeChartTimeframe, largeChartCandles);
                }

                ChartHelper.SetChartViewModelVisibleRange(trade, ChartViewModel, largeChartCandles,
                    largeChartTimeframe);


                ChartHelper.SetChartViewModelPriceData(smallChartCandles, ChartViewModelSmaller1, smallChartTimeframe);

                if (SelectedMainIndicatorsIndex == (int)MainIndicators.EMA8_EMA25_EMA50)
                {
                    ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], trade.Market,
                        new ExponentialMovingAverage(8), Colors.DarkBlue, smallChartTimeframe, smallChartCandles);
                    ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], trade.Market,
                        new ExponentialMovingAverage(25), Colors.Blue, smallChartTimeframe, smallChartCandles);
                    ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], trade.Market,
                        new ExponentialMovingAverage(50), Colors.LightBlue, smallChartTimeframe, smallChartCandles);
                }
                else if (SelectedMainIndicatorsIndex == (int)MainIndicators.EMA20_MA50_MA200)
                {
                    ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], trade.Market,
                        new ExponentialMovingAverage(20), Colors.DarkBlue, smallChartTimeframe, smallChartCandles);
                    ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], trade.Market,
                        new SimpleMovingAverage(50), Colors.Blue, smallChartTimeframe, smallChartCandles);
                    ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], trade.Market,
                        new SimpleMovingAverage(200), Colors.LightBlue, smallChartTimeframe, smallChartCandles);
                }

                ChartHelper.SetChartViewModelVisibleRange(trade, ChartViewModelSmaller1, smallChartCandles,
                    smallChartTimeframe);


                ChartViewModel.ChartPaneViewModels[0].TradeAnnotations =
                    ChartHelper.CreateTradeAnnotations(ChartViewModel, TradeAnnotationsToShow.All, largeChartTimeframe, largeChartCandles,
                        trade);
                ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations =
                    ChartHelper.CreateTradeAnnotations(ChartViewModelSmaller1, TradeAnnotationsToShow.All, smallChartTimeframe,
                        smallChartCandles, trade);

                TradeShown?.Invoke(trade);
            }));
        }

        protected event Action<TradeDetails> TradeShown;

        protected void ShowTrade(TradeDetails trade, bool updateCandles = false)
        {
            // Setup main chart
            var largeChartTimeframe = GetSelectedTimeframe(trade);
            var smallChartTimeframe = Timeframe.D1;

            DateTime? start = null, end = null;

            if (largeChartTimeframe == Timeframe.M1)
            {
                start = trade.StartDateTime.Value.AddMinutes(-20);
                end = trade.CloseDateTime != null
                    ? trade.CloseDateTime.Value.AddMinutes(20)
                    : trade.StartDateTime.Value.AddMinutes(240);
            }
            else
            {
                end = trade.CloseDateTime?.AddDays(20);
            }

            var largeChartCandles = BrokerCandles.GetCandles(Broker, trade.Market, largeChartTimeframe, updateCandles, cacheData: false, minOpenTimeUtc: start, maxCloseTimeUtc: end);
            var smallChartCandles = BrokerCandles.GetCandles(Broker, trade.Market, smallChartTimeframe, updateCandles, maxCloseTimeUtc: trade.CloseDateTime?.AddDays(30));

            ShowTrade(trade, smallChartTimeframe, smallChartCandles, largeChartTimeframe, largeChartCandles);
        }

        protected void ShowTradeSetup(TradeDetails trade, bool updateCandles = false)
        {
            if (trade.StartDateTime == null) return;

            // Setup main chart
            var largeChartTimeframe = trade.Timeframe ?? Timeframe.H2;
            var smallChartTimeframe = Timeframe.D1;

            DateTime? start = null;
            if (largeChartTimeframe == Timeframe.M1)
            {
                start = trade.StartDateTime.Value.AddMinutes(-20);
            }

            var smallChartCandles = BrokerCandles.GetCandlesUptoSpecificTime(Broker, trade.Market, smallChartTimeframe, updateCandles, start, trade.StartDateTime.Value, Timeframe.M15);
            var largeChartCandles = BrokerCandles.GetCandlesUptoSpecificTime(Broker, trade.Market, largeChartTimeframe, updateCandles, start, trade.StartDateTime.Value, Timeframe.M15);

            ShowTrade(trade, smallChartTimeframe, smallChartCandles, largeChartTimeframe, largeChartCandles);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}