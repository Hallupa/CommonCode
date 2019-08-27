using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using Abt.Controls.SciChart.Visuals.Annotations;
using Hallupa.Library;
using Hallupa.Library.UI;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Core.Services;
using TraderTools.Core.UI.ChartModifiers;
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
        protected Dispatcher _dispatcher;

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
            ViewTradeCommand = new DelegateCommand(t => ViewTrade((Trade)t, true));
            RemoveSelectedLineCommand = new DelegateCommand(t => RemoveSelectedLine());
            ViewTradeSetupCommand = new DelegateCommand(t => ViewTradeSetup((Trade)t));

            TradesView = (CollectionView)CollectionViewSource.GetDefaultView(Trades);
            TradesView.Filter = TradesViewFilter;

            _dispatcher = Dispatcher.CurrentDispatcher;

            LargeChartTimeframe = Timeframe.H2;
        }

        private void RemoveSelectedLine()
        {
            if (ChartViewModel != null && ChartViewModel.ChartPaneViewModels.Count > 0 && ChartViewModel.ChartPaneViewModels[0].TradeAnnotations != null)
            {
                var toRemoveList = ChartViewModel.ChartPaneViewModels[0].TradeAnnotations.OfType<LineAnnotation>().Where(x => x.Tag is string s && s.StartsWith("Added") && x.IsSelected).ToList();
                foreach (var toRemove in toRemoveList)
                {
                    ChartViewModel.ChartPaneViewModels[0].TradeAnnotations.Remove(toRemove);

                    var linked = ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations.OfType<LineAnnotation>().FirstOrDefault(x => x.Tag is string s && s.Equals((string)toRemove.Tag));
                    if (linked != null)
                    {
                        ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations.Remove(linked);
                    }
                }
            }

            if (ChartViewModelSmaller1 != null && ChartViewModelSmaller1.ChartPaneViewModels.Count > 0 && ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations != null)
            {
                var toRemoveList = ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations.OfType<LineAnnotation>().Where(x => x.Tag is string s && s.StartsWith("Added") && x.IsSelected).ToList();
                foreach (var toRemove in toRemoveList)
                {
                    ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations.Remove(toRemove);

                    if (ChartViewModel != null)
                    {
                        var linked = ChartViewModel.ChartPaneViewModels[0].TradeAnnotations.OfType<LineAnnotation>()
                            .FirstOrDefault(x => x.Tag is string s && s.Equals((string)toRemove.Tag));
                        if (linked != null)
                        {
                            ChartViewModel.ChartPaneViewModels[0].TradeAnnotations.Remove(linked);
                        }
                    }
                }
            }
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
                                                     | TradeListDisplayOptionsFlag.Timeframe
                                                     | TradeListDisplayOptionsFlag.Strategies
                                                     | TradeListDisplayOptionsFlag.Risk
                                                     | TradeListDisplayOptionsFlag.Status
                                                     | TradeListDisplayOptionsFlag.ClosePrice;

        protected Trade TradeShowingOnChart { get; private set; }
        public DelegateCommand ViewTradeCommand { get; protected set; }
        public DelegateCommand ViewTradeSetupCommand { get; protected set; }

        public DelegateCommand RemoveSelectedLineCommand { get; private set; }

        public int SelectedMainIndicatorsIndex
        {
            get => _selectedMainIndicatorsIndex;
            set => _selectedMainIndicatorsIndex = value;
        }

        public ICommand EditCommand { get; }

        public DelegateCommand DeleteCommand { get; }

        public ObservableCollectionEx<Trade> Trades { get; } = new ObservableCollectionEx<Trade>();

        private bool TradesViewFilter(object obj)
        {
            var t = (Trade)obj;
            return ((ShowOpenTrades && t.EntryPrice != null && t.CloseDateTime == null)
                   || (ShowOrders && t.OrderPrice != null && t.EntryPrice == null && t.CloseDateTime == null)
                   || (ShowClosedTrades && t.CloseDateTime != null));// && t.Id == "34728772";
        }

        [Import] public ChartingService ChartingService { get; private set; }

        public Trade SelectedTrade
        {
            get => _selectedTrade;
            set
            {
                _selectedTrade = value;
                OnPropertyChanged();
            }
        }

        public static readonly DependencyProperty ShowOpenTradesProperty = DependencyProperty.Register("ShowOpenTrades", typeof(bool), typeof(TradeViewModelBase), new PropertyMetadata(true, ShowTradesChanged));
        public static readonly DependencyProperty ShowClosedTradesProperty = DependencyProperty.Register("ShowClosedTrades", typeof(bool), typeof(TradeViewModelBase), new PropertyMetadata(false, ShowTradesChanged));
        public static readonly DependencyProperty ShowOrdersProperty = DependencyProperty.Register("ShowOrders", typeof(bool), typeof(TradeViewModelBase), new PropertyMetadata(true, ShowTradesChanged));

        private static void ShowTradesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tvm = (TradeViewModelBase)d;
            tvm.TradesView.Refresh();
        }

        public bool ShowOpenTrades
        {
            get { return (bool)GetValue(ShowOpenTradesProperty); }
            set { SetValue(ShowOpenTradesProperty, value); }
        }

        public bool ShowClosedTrades
        {
            get { return (bool)GetValue(ShowClosedTradesProperty); }
            set { SetValue(ShowClosedTradesProperty, value); }
        }

        public bool ShowOrders
        {
            get { return (bool)GetValue(ShowOrdersProperty); }
            set { SetValue(ShowOrdersProperty, value); }
        }

        private Window _parent;
        private Trade _selectedTrade;

        protected virtual void DeleteTrade()
        {
        }

        public void ViewTrade(Trade tradeDetails, bool updatePrices)
        {
            if (tradeDetails == null) return;

            ShowTrade(tradeDetails, updatePrices);
        }

        protected override void LargeChartTimeframeChanged()
        {
            if (TradeShowingOnChart != null)
            {
                ViewTrade(TradeShowingOnChart, true);
            }
        }

        public void ViewTradeSetup(Trade tradeDetails, bool updatePrices = true)
        {
            if (tradeDetails == null) return;

            ShowTradeSetup(tradeDetails, updatePrices);
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

        protected void ShowTrade(Trade trade, Timeframe smallChartTimeframe, List<Candle> smallChartCandles, Timeframe largeChartTimeframe, List<Candle> largeChartCandles)
        {
            _dispatcher.BeginInvoke((Action)(() =>
            {
                TradeShowingOnChart = trade;

                if (trade == null)
                {
                    return;
                }

                ViewCandles(trade.Market, smallChartTimeframe, smallChartCandles, largeChartTimeframe, largeChartCandles);
                
                ChartHelper.SetChartViewModelVisibleRange(trade, ChartViewModel, largeChartCandles,
                    largeChartTimeframe);

                ChartHelper.SetChartViewModelVisibleRange(trade, ChartViewModelSmaller1, smallChartCandles,
                    smallChartTimeframe);

                ChartHelper.CreateTradeAnnotations(ChartViewModel.ChartPaneViewModels[0].TradeAnnotations,
                    ChartViewModel, TradeAnnotationsToShow.All, largeChartTimeframe, largeChartCandles, trade);
                
                ChartHelper.CreateTradeAnnotations(ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations,
                    ChartViewModelSmaller1, TradeAnnotationsToShow.All, smallChartTimeframe, smallChartCandles,
                    trade);

                AddTradeLines(trade);

                TradeShown?.Invoke(trade);
            }));
        }

        protected void ViewCandles(string market, Timeframe smallChartTimeframe, List<Candle> smallChartCandles,
            Timeframe largeChartTimeframe, List<Candle> largeChartCandles)
        {
            ChartHelper.SetChartViewModelPriceData(largeChartCandles, ChartViewModel, largeChartTimeframe);

            if (SelectedMainIndicatorsIndex == (int) MainIndicators.EMA8_EMA25_EMA50)
            {
                ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(8), Colors.DarkBlue, largeChartTimeframe, largeChartCandles);
                ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(25), Colors.Blue, largeChartTimeframe, largeChartCandles);
                ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(50), Colors.LightBlue, largeChartTimeframe, largeChartCandles);
            }
            else if (SelectedMainIndicatorsIndex == (int) MainIndicators.EMA20_MA50_MA200)
            {
                ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(8), Colors.DarkBlue, largeChartTimeframe, largeChartCandles);
                ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], market,
                    new SimpleMovingAverage(50), Colors.Blue, largeChartTimeframe, largeChartCandles);
                ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], market,
                    new SimpleMovingAverage(200), Colors.LightBlue, largeChartTimeframe, largeChartCandles);
            }



            ChartHelper.SetChartViewModelPriceData(smallChartCandles, ChartViewModelSmaller1,
                smallChartTimeframe);

            if (SelectedMainIndicatorsIndex == (int) MainIndicators.EMA8_EMA25_EMA50)
            {
                ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(8), Colors.DarkBlue, smallChartTimeframe, smallChartCandles);
                ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(25), Colors.Blue, smallChartTimeframe, smallChartCandles);
                ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(50), Colors.LightBlue, smallChartTimeframe, smallChartCandles);
            }
            else if (SelectedMainIndicatorsIndex == (int) MainIndicators.EMA20_MA50_MA200)
            {
                ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(20), Colors.DarkBlue, smallChartTimeframe, smallChartCandles);
                ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], market,
                    new SimpleMovingAverage(50), Colors.Blue, smallChartTimeframe, smallChartCandles);
                ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], market,
                    new SimpleMovingAverage(200), Colors.LightBlue, smallChartTimeframe, smallChartCandles);
            }

            if (ChartViewModel.ChartPaneViewModels[0].TradeAnnotations == null)
            {
                ChartViewModel.ChartPaneViewModels[0].TradeAnnotations = new AnnotationCollection();
            }
            else
            {
                ChartViewModel.ChartPaneViewModels[0].TradeAnnotations.Clear();
            }

            if (ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations == null)
            {
                ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations = new AnnotationCollection();
            }
            else
            {
                ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations.Clear();
            }
        }

        private void AddTradeLines(Trade t)
        {
            if (t.ChartLines != null)
            {
                var id = 1;
                foreach (var line in t.ChartLines)
                {
                    var addedLine = new LineAnnotation
                    {
                        Tag = "Added_" + id,
                        StrokeThickness = AddLinesModifier.StrokeThickness,
                        Opacity = AddLinesModifier.Opacity,
                        Stroke = AddLinesModifier.Stroke,
                        X1 = line.DateTimeUTC1,
                        Y1 = line.Price1,
                        X2 = line.DateTimeUTC2,
                        Y2 = line.Price2,
                        IsEditable = true
                    };
                    ChartViewModel.ChartPaneViewModels[0].TradeAnnotations.Add(addedLine);

                    addedLine = new LineAnnotation
                    {
                        Tag = "Added_" + id,
                        StrokeThickness = AddLinesModifier.StrokeThickness,
                        Opacity = AddLinesModifier.Opacity,
                        Stroke = AddLinesModifier.Stroke,
                        X1 = line.DateTimeUTC1,
                        Y1 = line.Price1,
                        X2 = line.DateTimeUTC2,
                        Y2 = line.Price2,
                        IsEditable = true
                    };
                    ChartViewModelSmaller1.ChartPaneViewModels[0].TradeAnnotations.Add(addedLine);

                    id++;
                }
            }
        }

        protected event Action<Trade> TradeShown;

        protected void ShowTrade(Trade trade, bool updateCandles = false)
        {
            DateTime? start = null, end = null;

            if (LargeChartTimeframe == Timeframe.M1)
            {
                start = trade.StartDateTime.Value.AddMinutes(-20);
                end = trade.CloseDateTime != null
                    ? trade.CloseDateTime.Value.AddMinutes(20)
                    : trade.StartDateTime.Value.AddMinutes(240);
            }

            var largeChartCandles = BrokerCandles.GetCandles(Broker, trade.Market, LargeChartTimeframe, updateCandles, cacheData: false, minOpenTimeUtc: start, maxCloseTimeUtc: end);
            var smallChartCandles = BrokerCandles.GetCandles(Broker, trade.Market, SmallChartTimeframe, updateCandles);

            ShowTrade(trade, SmallChartTimeframe, smallChartCandles, LargeChartTimeframe, largeChartCandles);
        }

        protected void ShowTradeSetup(Trade trade, bool updateCandles = false)
        {
            if (trade.StartDateTime == null) return;

            // Setup main chart
            var smallChartTimeframe = Timeframe.D1;

            DateTime? start = null;
            if (LargeChartTimeframe == Timeframe.M1)
            {
                start = trade.StartDateTime.Value.AddMinutes(-20);
            }

            var candlesTimeframe = LargeChartTimeframe;

            switch (LargeChartTimeframe)
            {
                case Timeframe.H1:
                    candlesTimeframe = Timeframe.M15;
                    break;
                case Timeframe.H2:
                    candlesTimeframe = Timeframe.H1;
                    break;
                case Timeframe.M15:
                    candlesTimeframe = Timeframe.M5;
                    break;
                case Timeframe.D1:
                    candlesTimeframe = Timeframe.H2;
                    break;
            }

            var smallChartCandles = BrokerCandles.GetCandlesUptoSpecificTime(Broker, trade.Market, smallChartTimeframe, updateCandles, start, trade.StartDateTime.Value, candlesTimeframe);
            var largeChartCandles = BrokerCandles.GetCandlesUptoSpecificTime(Broker, trade.Market, LargeChartTimeframe, updateCandles, start, trade.StartDateTime.Value, candlesTimeframe);

            ShowTrade(trade, smallChartTimeframe, smallChartCandles, LargeChartTimeframe, largeChartCandles);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}