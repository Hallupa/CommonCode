using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Abt.Controls.SciChart;
using Abt.Controls.SciChart.Visuals.Annotations;
using Hallupa.Library;
using TraderTools.Basics;
using TraderTools.Core.UI.Services;
using TraderTools.Indicators;

namespace TraderTools.Core.UI.ViewModels
{
    public abstract class DoubleChartViewModel : DependencyObject
    {
        private Timeframe _largeChartTimeframe = Timeframe.H2;
        private int _selectedMainIndicatorsIndex;
        private Dispatcher _dispatcher;

        public DoubleChartViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            DependencyContainer.ComposeParts(this);

            ChartViewModel.XVisibleRange = new IndexRange();
            ChartViewModelSmaller1.XVisibleRange = new IndexRange();

            LargeChartTimeframeOptions.Add(Timeframe.D1);
            LargeChartTimeframeOptions.Add(Timeframe.H4);
            LargeChartTimeframeOptions.Add(Timeframe.H2);
            LargeChartTimeframeOptions.Add(Timeframe.H1);
            LargeChartTimeframeOptions.Add(Timeframe.M30);
            LargeChartTimeframeOptions.Add(Timeframe.M15);
            LargeChartTimeframeOptions.Add(Timeframe.M5);
            LargeChartTimeframeOptions.Add(Timeframe.M1);

            RemoveSelectedLineCommand = new DelegateCommand(t => RemoveSelectedLine());
        }

        public GridLength SmallerChartWidth { get; set; } = new GridLength(1, GridUnitType.Star);

        public ChartViewModel ChartViewModel { get; } = new ChartViewModel();

        [Import] public ChartingService ChartingService { get; private set; }

        public ChartViewModel ChartViewModelSmaller1 { get; } = new ChartViewModel();

        public event PropertyChangedEventHandler PropertyChanged;

        public Timeframe SmallChartTimeframe { get; set; } = Timeframe.D1;
        public ObservableCollection<Timeframe> LargeChartTimeframeOptions { get; } = new ObservableCollection<Timeframe>();

        public DelegateCommand RemoveSelectedLineCommand { get; private set; }
        public Timeframe LargeChartTimeframe
        {
            get => _largeChartTimeframe;
            set
            {
                if (_largeChartTimeframe == value) return;

                _largeChartTimeframe = value;
                SelectedLargeChartTimeframeIndex = LargeChartTimeframeOptions.IndexOf(value);

                LargeChartTimeframeChanged();
            }
        }

        public static readonly DependencyProperty SelectedLargeChartTimeframeIndexProperty = DependencyProperty.Register(
            "SelectedLargeChartTimeframeIndex", typeof(int), typeof(DoubleChartViewModel), new PropertyMetadata(default(int)));

        public int SelectedLargeChartTimeframeIndex
        {
            get => (int) GetValue(SelectedLargeChartTimeframeIndexProperty);
            set
            {
                _dispatcher.Invoke(() =>
                {
                    SetValue(SelectedLargeChartTimeframeIndexProperty, value);
                    if (LargeChartTimeframe != LargeChartTimeframeOptions[value])
                    {
                        LargeChartTimeframe = LargeChartTimeframeOptions[value];
                    }
                });
            }
        }

        public int SelectedMainIndicatorsIndex
        {
            get => _selectedMainIndicatorsIndex;
            set => _selectedMainIndicatorsIndex = value;
        }

        protected virtual void LargeChartTimeframeChanged()
        {
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


        protected void ViewCandles(string market, Timeframe smallChartTimeframe, List<Candle> smallChartCandles,
            Timeframe largeChartTimeframe, List<Candle> largeChartCandles)
        {
            LargeChartTimeframe = largeChartTimeframe;
            ChartHelper.SetChartViewModelPriceData(largeChartCandles, ChartViewModel, largeChartTimeframe);

            if (SelectedMainIndicatorsIndex == (int)MainIndicators.EMA8_EMA25_EMA50)
            {
                ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(8), Colors.DarkBlue, largeChartTimeframe, largeChartCandles);
                ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(25), Colors.Blue, largeChartTimeframe, largeChartCandles);
                ChartHelper.AddIndicator(ChartViewModel.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(50), Colors.LightBlue, largeChartTimeframe, largeChartCandles);
            }
            else if (SelectedMainIndicatorsIndex == (int)MainIndicators.EMA20_MA50_MA200)
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

            if (SelectedMainIndicatorsIndex == (int)MainIndicators.EMA8_EMA25_EMA50)
            {
                ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(8), Colors.DarkBlue, smallChartTimeframe, smallChartCandles);
                ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(25), Colors.Blue, smallChartTimeframe, smallChartCandles);
                ChartHelper.AddIndicator(ChartViewModelSmaller1.ChartPaneViewModels[0], market,
                    new ExponentialMovingAverage(50), Colors.LightBlue, smallChartTimeframe, smallChartCandles);
            }
            else if (SelectedMainIndicatorsIndex == (int)MainIndicators.EMA20_MA50_MA200)
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
    }
}