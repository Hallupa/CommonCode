using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Abt.Controls.SciChart;
using Abt.Controls.SciChart.Common.Extensions;
using Abt.Controls.SciChart.Model.DataSeries;
using Abt.Controls.SciChart.Visuals.Annotations;
using Abt.Controls.SciChart.Visuals.RenderableSeries;
using TraderTools.Basics;
using TraderTools.Basics.Helpers;
using TraderTools.Core.UI.Controls;

namespace TraderTools.Core.UI
{
    [Flags]
    public enum TradeAnnotationsToShow
    {
        All = 1,
        StopsLines = 2,
        LimitsLines = 4,
        OrderOrEntryLines = 8,
        OrderMarker = 16,
        EntryMarker = 32,
        CloseMarker = 64,
        MakeEntryCloseMarkerSmaller = 128
    }

    public static class ChartHelper
    {
        static ChartHelper()
        {
            LocalUtcOffset = TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now);
        }

        public static TimeSpan LocalUtcOffset { get; private set; }

        public static void SetChartViewModelPriceData(IList<ICandle> candles, ChartViewModel cvm, Timeframe timeframe)
        {
            var priceDataSeries = new OhlcDataSeries<DateTime, double>();
            var time = new DateTime(0);
            var xvalues = new List<DateTime>();
            var openValues = new List<double>();
            var highValues = new List<double>();
            var lowValues = new List<double>();
            var closeValues = new List<double>();

            for (var i = 0; i < candles.Count; i++)
            {
                time = DateTime.SpecifyKind(new DateTime(candles[i].OpenTimeTicks, DateTimeKind.Utc) + LocalUtcOffset, DateTimeKind.Local);

                xvalues.Add(time);
                openValues.Add((double)candles[i].Open);
                highValues.Add((double)candles[i].High);
                lowValues.Add((double)candles[i].Low);
                closeValues.Add((double)candles[i].Close);
            }

            priceDataSeries.Append(xvalues, openValues, highValues, lowValues, closeValues);

            var pricePaneVm = cvm.ChartPaneViewModels.Count > 0 ? cvm.ChartPaneViewModels[0] : null;
            if (pricePaneVm == null)
            {
                pricePaneVm = new ChartPaneViewModel(cvm, cvm.ViewportManager)
                {
                    IsFirstChartPane = true,
                    IsLastChartPane = false
                };

                pricePaneVm.ChartSeriesViewModels.Add(new ChartSeriesViewModel(priceDataSeries, new FastCandlestickRenderableSeries { AntiAliasing = false }));
                cvm.ChartPaneViewModels.Add(pricePaneVm);
            }
            else
            {
                pricePaneVm.ChartSeriesViewModels.Clear();
                pricePaneVm.ChartSeriesViewModels.Add(new ChartSeriesViewModel(priceDataSeries, new FastCandlestickRenderableSeries { AntiAliasing = false }));
            }
        }

        public static IDataSeries CreateIndicatorSeries(string market, IIndicator indicator, Color color, Timeframe timeframe, IList<ICandle> candles)
        {
            var series = new XyDataSeries<DateTime, double>();
            var xvalues = new List<DateTime>();
            var yvalues = new List<double>();

            foreach (var candle in candles)
            {
                var signalAndValue = indicator.Process(new SimpleCandle(candle));
                if (indicator.IsFormed)
                {
                    xvalues.Add(DateTime.SpecifyKind(new DateTime(candle.OpenTimeTicks, DateTimeKind.Utc) + LocalUtcOffset, DateTimeKind.Local));
                    yvalues.Add((double)signalAndValue.Value);
                }
                else
                {
                    xvalues.Add(DateTime.SpecifyKind(new DateTime(candle.OpenTimeTicks, DateTimeKind.Utc) + LocalUtcOffset, DateTimeKind.Local));
                    yvalues.Add(double.NaN);
                }
            }

            series.Append(xvalues, yvalues);

            return series;
        }

        public static void AddIndicator(ChartPaneViewModel paneViewModel, string market, IIndicator indicator, Color color, Timeframe timeframe, IList<ICandle> candles)
        {
            var series = CreateIndicatorSeries(market, indicator, color, timeframe, candles);

            paneViewModel.ChartSeriesViewModels.Add(new ChartSeriesViewModel(series, new FastLineRenderableSeries
            {
                AntiAliasing = false,
                SeriesColor = color,
                StrokeThickness = 2
            }));
        }

        public static AnnotationCollection CreateTradeAnnotations(AnnotationCollection annotations, ChartViewModel cvm, TradeAnnotationsToShow annotationsToShow, Timeframe timeframe, IList<ICandle> candles, TradeDetails trade)
        {
            // Setup annotations
            if (candles.Count == 0) return annotations;

            var dataSeries = cvm.ChartPaneViewModels[0].ChartSeriesViewModels[0].DataSeries;
            var startTime = trade.StartDateTimeLocal;
            var entryCloseMarketSmaller = annotationsToShow.HasFlag(TradeAnnotationsToShow.MakeEntryCloseMarkerSmaller);

            if (startTime != null && (trade.OrderPrice != null || trade.EntryPrice != null))
            {
                if (trade.EntryPrice != null && (annotationsToShow.HasFlag(TradeAnnotationsToShow.All) || annotationsToShow.HasFlag(TradeAnnotationsToShow.EntryMarker)))
                {
                    var profit = trade.NetProfitLoss ?? trade.GrossProfitLoss;
                    var colour = profit != null && profit < 0 ? Colors.DarkRed : Colors.Green;
                    AddBuySellMarker(trade.TradeDirection.Value, annotations, trade, trade.EntryDateTimeLocal.Value, trade.EntryPrice.Value,
                        entryCloseMarketSmaller, colour: colour);
                }

                if (annotationsToShow.HasFlag(TradeAnnotationsToShow.All) || annotationsToShow.HasFlag(TradeAnnotationsToShow.OrderOrEntryLines))
                {
                    if (trade.OrderPrice != null && trade.OrderDateTimeLocal != null)
                    {
                        var orderPrices = trade.OrderPrices.ToList();
                        if (orderPrices.Count > 0)
                        {
                            orderPrices.Add(new DatePrice(trade.EntryDateTime?.ToLocalTime() ?? new DateTime(candles[candles.Count - 1].CloseTimeTicks, DateTimeKind.Utc), null));

                            AddLineAnnotations(orderPrices, cvm.ChartPaneViewModels[0].ChartSeriesViewModels[0].DataSeries, annotations, Colors.Gray);
                        }
                    }
                }
            }

            // Add close price line
            if (trade.ClosePrice != null)
            {
                var oppositeTradeDirection = trade.TradeDirection.Value == TradeDirection.Long
                    ? TradeDirection.Short
                    : TradeDirection.Long;

                if (annotationsToShow.HasFlag(TradeAnnotationsToShow.All) || annotationsToShow.HasFlag(TradeAnnotationsToShow.CloseMarker))
                {
                    var profit = trade.NetProfitLoss ?? trade.GrossProfitLoss;
                    var colour = profit != null && profit < 0 ? Colors.DarkRed : Colors.Green;
                    AddBuySellMarker(oppositeTradeDirection, annotations, trade, trade.CloseDateTimeLocal.Value, trade.ClosePrice.Value, entryCloseMarketSmaller, colour: colour);
                }
            }

            // Add stop prices
            if (annotationsToShow.HasFlag(TradeAnnotationsToShow.All) || annotationsToShow.HasFlag(TradeAnnotationsToShow.StopsLines))
            {
                var stopPrices = trade.StopPrices.ToList();
                if (stopPrices.Count > 0)
                {
                    stopPrices.Add(new DatePrice(trade.CloseDateTime != null
                        ? trade.CloseDateTime.Value.ToLocalTime()
                        : new DateTime(candles[candles.Count - 1].CloseTimeTicks, DateTimeKind.Utc).ToLocalTime(), null));

                    AddLineAnnotations(stopPrices, cvm.ChartPaneViewModels[0].ChartSeriesViewModels[0].DataSeries, annotations, Colors.Red);
                }
            }

            // Add limit prices
            if (annotationsToShow.HasFlag(TradeAnnotationsToShow.All) || annotationsToShow.HasFlag(TradeAnnotationsToShow.LimitsLines))
            {
                var limitPrices = trade.LimitPrices.ToList();
                if (limitPrices.Count > 0)
                {
                    limitPrices.Add(new DatePrice(trade.CloseDateTime?.ToLocalTime() ?? new DateTime(candles[candles.Count - 1].CloseTimeTicks, DateTimeKind.Utc), null));

                    AddLineAnnotations(limitPrices, cvm.ChartPaneViewModels[0].ChartSeriesViewModels[0].DataSeries, annotations, Colors.DarkGreen);
                }
            }

            return annotations;
        }

        private static void AddBuySellMarker(
            TradeDirection direction, AnnotationCollection annotations, TradeDetails trade,
            DateTime timeLocal, decimal price, bool makeSmaller, bool isFilled = true, Color? colour = null)
        {
            var buyMarker = new BuyMarkerAnnotation();
            var sellMarker = new SellMarkerAnnotation();
            var annotation = direction == TradeDirection.Long ? buyMarker : (CustomAnnotation)sellMarker;
            annotation.Width = makeSmaller ? 12 : 24;
            annotation.Height = makeSmaller ? 12 : 24;
            ((Path)annotation.Content).Stretch = Stretch.Fill;
            annotation.Margin = new Thickness(0, direction == TradeDirection.Long ? 5 : -5, 0, 0);
            annotation.DataContext = trade;

            var brush = new SolidColorBrush
            {
                Color = colour ?? (direction == TradeDirection.Long ? Colors.Green : Colors.DarkRed)
            };

            if (isFilled)
            {
                ((Path)annotation.Content).Fill = brush;
            }

            buyMarker.StrokeBrush = brush;
            sellMarker.StrokeBrush = brush;
            annotation.X1 = timeLocal;
            annotation.BorderThickness = new Thickness(20);
            annotation.Y1 = (double)price;
            annotations.Add(annotation);
        }

        public static void AddHorizontalLine(decimal price, DateTime start, DateTime end, IDataSeries dataSeries,
            AnnotationCollection annotations, TradeDetails trade, Color colour, bool extendLeftAndRight = false, 
            bool extendRightIfZeroLength = false, DoubleCollection strokeDashArray = null)
        {
            var dateStartIndex = dataSeries.FindIndex(start, SearchMode.RoundDown);
            var dateEndIndex = dataSeries.FindIndex(end, SearchMode.RoundUp);

            if (extendLeftAndRight) dateStartIndex -= 4;
            if (dateStartIndex < 0) dateStartIndex = 0;
            if (extendLeftAndRight) dateEndIndex += 4;

            if (extendRightIfZeroLength && dateStartIndex == dateEndIndex) dateEndIndex++;

            var lineAnnotation = new LineAnnotation
            {
                DataContext = trade,
                X1 = dateStartIndex,
                Y1 = price,
                X2 = dateEndIndex,
                Y2 = price,
                StrokeThickness = 3,
                Opacity = 0.8,
                Stroke = new SolidColorBrush(colour)
            };

            if (strokeDashArray != null)
            {
                lineAnnotation.StrokeDashArray = strokeDashArray;
            }
            annotations.Add(lineAnnotation);
        }

        private static void AddLineAnnotations(
            List<DatePrice> prices, IDataSeries series, AnnotationCollection annotations, Color colour)
        {
            int? startIndex = null;
            decimal? currentPrice = null;
            foreach (var p in prices)
            {
                if (p.Price != currentPrice)
                {
                    // Price changed
                    if (currentPrice != null && startIndex != null)
                    {
                        var endIndex = series.FindIndex(p.Date.ToLocalTime(), SearchMode.Nearest);

                        if (endIndex == startIndex.Value)
                        {
                            endIndex++;
                        }

                        var brush = new SolidColorBrush(colour) { Opacity = 0.5 };
                        var annotation = new LineAnnotation
                        {
                            X1 = startIndex.Value,
                            X2 = endIndex,
                            Y1 = currentPrice.Value,
                            Y2 = currentPrice.Value,
                            Stroke = brush
                        };

                        annotations.Add(annotation);
                    }

                    startIndex = series.FindIndex(p.Date.ToLocalTime(), SearchMode.Nearest);
                    currentPrice = p.Price;
                }
            }
        }

        public static void SetChartViewModelVisibleRange(
            TradeDetails trade, ChartViewModel cvm, IList<ICandle> candles, Timeframe timeframe)
        {
            if (candles.Count == 0) return;

            var startTime = trade.OrderDateTime ?? trade.EntryDateTime.Value;
            var endTime = trade.CloseDateTime ?? new DateTime(candles.Last().CloseTimeTicks, DateTimeKind.Utc);

            var startCandle = CandlesHelper.GetFirstCandleThatClosesBeforeDateTime(candles, startTime);

            var endCandle = CandlesHelper.GetFirstCandleThatClosesBeforeDateTime(candles, endTime) ?? candles.Last();

            var candlesBeforeTrade = 0;

            switch (timeframe)
            {
                case Timeframe.M1:
                    candlesBeforeTrade = (1 * 60);
                    break;
                case Timeframe.M5:
                    candlesBeforeTrade = (6 * 12);
                    break;
                case Timeframe.M15:
                    candlesBeforeTrade = (2 * 12);
                    break;
                case Timeframe.H1:
                    candlesBeforeTrade = (2 * 24);
                    break;
                case Timeframe.H2:
                    candlesBeforeTrade = (5 * 12);
                    break;
                case Timeframe.H4:
                    candlesBeforeTrade = (10 * 6);
                    break;
                case Timeframe.H8:
                    candlesBeforeTrade = (20 * 3);
                    break;
                case Timeframe.D1:
                    candlesBeforeTrade = 30;
                    break;
                default:
                    throw new ApplicationException(timeframe + " timeframe not found for chart helper vis range");
            }

            var candlesAfterTrade = (int)(candlesBeforeTrade * 0.1);

            var min = candles.IndexOf(startCandle) - candlesBeforeTrade;
            var max = candles.IndexOf(endCandle) + candlesAfterTrade;

            if (min < 0)
            {
                min = 0;
            }

            SetChartXVisibleRange(cvm, min, max);

            var miny = double.NaN;
            var maxy = double.NaN;
            for (var i = min; i < candles.Count; i++)
            {
                if (double.IsNaN(miny) || candles[i].Low < miny) miny = candles[i].Low;
                if (double.IsNaN(maxy) || candles[i].High > maxy) maxy = candles[i].High;
            }

            if (trade.LimitPrice != null && trade.LimitPrice < (decimal)miny) miny = (double)trade.LimitPrice;
            if (trade.LimitPrice != null && trade.LimitPrice > (decimal)maxy) maxy = (double)trade.LimitPrice;
            if (trade.StopPrice != null && trade.StopPrice < (decimal)miny) miny = (double)trade.StopPrice;
            if (trade.StopPrice != null && trade.StopPrice > (decimal)maxy) maxy = (double)trade.StopPrice;
        }

        public static void SetChartXVisibleRange(ChartViewModel cvm, int min, int max)
        {
            if (min <= cvm.XVisibleRange.Max)
            {
                cvm.XVisibleRange.Min = min;
                cvm.XVisibleRange.Max = max;
            }
            else
            {
                cvm.XVisibleRange.Max = max;
                cvm.XVisibleRange.Min = min;
            }

        }
    }
}