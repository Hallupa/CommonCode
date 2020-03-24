using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using log4net;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Basics.Helpers;
using TraderTools.Core.Trading;

namespace TraderTools.Simulation
{
    public class UpdateTradeParameters
    {
        public TimeframeLookup<List<CandleAndIndicators>> TimeframeCurrentCandles { get; set; }
        public Trade Trade { get; set; }
        public string Market { get; set; }
        public long TimeTicks { get; set; }
    }


    [Flags]
    public enum SimulationRunnerFlags
    {
        Default = 1,
        DoNotValidateStopsLimitsOrders = 2,
        DoNotCacheM1Candles = 4
    }

    /// <summary>
    /// -Set trade expire/close/entry/etc to M1 candle open time so they appear in the correct position on the chart.
    /// </summary>
    public class SimulationRunner
    {
        private class TradeWithStopLimitIndex
        {
            public Trade Trade { get; set; }
            public int LimitIndex { get; set; } = -1;
            public int StopIndex { get; set; } = -1;
            public int OrderIndex { get; set; } = -1;
        }

        private IBrokersCandlesService _candlesService;
        private readonly ITradeDetailsAutoCalculatorService _calculatorService;
        private readonly IMarketDetailsService _marketDetailsService;
        private readonly SimulationRunnerFlags _options;
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public SimulationRunner(IBrokersCandlesService candleService, ITradeDetailsAutoCalculatorService calculatorService, IMarketDetailsService marketDetailsService,
            SimulationRunnerFlags options = SimulationRunnerFlags.Default)
        {
            _candlesService = candleService;
            _calculatorService = calculatorService;
            _marketDetailsService = marketDetailsService;
            _options = options;
        }

        private static List<RequiredTimeframeCandlesAttribute> GetRequiredTimeframesAndIndicators(IStrategy strategy)
        {
            return strategy.GetType().GetCustomAttributes(typeof(RequiredTimeframeCandlesAttribute), true).Cast<RequiredTimeframeCandlesAttribute>().ToList();
        }

        public List<Trade> Run(IStrategy strategy, MarketDetails market, IBroker broker,
            DateTime? earliest = null, DateTime? latest = null,
            bool updatePrices = false, bool cacheCandles = true)
        {
            if (strategy == null) return null;

            // Get update trade strategy
            var updateTradesStrategies = strategy.GetType()
                .GetCustomAttributes(typeof(UpdateTradeStrategyAttribute), true).Cast<UpdateTradeStrategyAttribute>()
                .ToList();
            if (updateTradesStrategies.Count > 1)
            {
                Log.Error("Only one update trade strategy is supported");
                return null;
            }

            var updateTradesStrategy = updateTradesStrategies.FirstOrDefault();

            // Get candles
            var requiredTimeframesAndIndicators = GetRequiredTimeframesAndIndicators(strategy);
            var strategyTimeframes = requiredTimeframesAndIndicators.Select(x => x.Timeframe).ToArray();
            var timeframeIndicators = GetTimeframeIndicatorsForRun(requiredTimeframesAndIndicators);
            var timeframesAllCandles = TimeframeLookupBasicCandleAndIndicators.PopulateCandles(broker, market.Name, strategyTimeframes.Union(new[] { Timeframe.M15 }).ToArray(),
                timeframeIndicators, _candlesService, updatePrices, cacheCandles, earliest, latest);
            var m1Candles = TimeframeLookupBasicCandleAndIndicators.GetM1Candles(
                broker, market.Name, _candlesService, !_options.HasFlag(SimulationRunnerFlags.DoNotCacheM1Candles), updatePrices, earliest, latest);

            var orders = new List<TradeWithStopLimitIndex>();
            var openTrades = new List<TradeWithStopLimitIndex>();
            var closedTrades = new List<Trade>();

            TimeframeLookupBasicCandleAndIndicators.IterateThroughCandles(
                timeframesAllCandles,
                m1Candles,
                c =>
                {
                    if (c.NewCandleFlags.HasFlag(NewCandleFlags.CompleteNonM1Candle))
                    {
                        AddNewTrades(orders, openTrades, strategy, market, c.CurrentCandles, c.M1Candle, updateTradesStrategy, c.M1Candle.CloseTime());
                    }

                    if (c.NewCandleFlags.HasFlag(NewCandleFlags.CompleteNonM1Candle) || c.NewCandleFlags.HasFlag(NewCandleFlags.IncompleteNonM1Candle))
                    {
                        // Update open trades
                        UpdateOpenTrades(market.Name, openTrades, c.M1Candle.CloseTimeTicks, c.CurrentCandles, parameters => updateTradesStrategy?.UpdateTrade(parameters));
                    }

                    // Validate and update stops/limts/orders 
                    if (!_options.HasFlag(SimulationRunnerFlags.DoNotValidateStopsLimitsOrders))
                    {
                        ValidateAndUpdateStopsLimitsOrders(orders.Concat(openTrades).ToList(), c.M1Candle);
                    }

                    // Process orders
                    FillOrders(orders, openTrades, c.M1Candle);

                    // Process open trades
                    TryCloseOpenTrades(openTrades, closedTrades, c.M1Candle);
                },
                percent => $"StrategyRunner: {market.Name} {percent:0.00}% complete - created {orders.Count + closedTrades.Count + openTrades.Count} trades");
            
            return orders.Select(t => t.Trade).Union(closedTrades).Union(openTrades.Select(t => t.Trade)).ToList();
        }


        private static TimeframeLookup<Indicator[]> GetTimeframeIndicatorsForRun(List<RequiredTimeframeCandlesAttribute> requiredTimeframesAndIndicators)
        {
            var timeframeIndicators = new TimeframeLookup<Indicator[]>();
            foreach (var r in requiredTimeframesAndIndicators)
            {
                if (r.Indicators.Length > 0)
                {
                    timeframeIndicators.Add(r.Timeframe, r.Indicators);
                }
            }

            return timeframeIndicators;
        }

        private void ValidateAndUpdateStopsLimitsOrders(List<TradeWithStopLimitIndex> trades, Candle m1Candle)
        {
            var timeTicks = m1Candle.CloseTimeTicks;

            foreach (var t in trades)
            {
                for (var i = t.StopIndex + 1; i < t.Trade.StopPrices.Count; i++)
                {
                    var stopPrice = t.Trade.StopPrices[i];
                    if (stopPrice.Date.Ticks <= timeTicks)
                    {
                        t.Trade.StopPrice = stopPrice.Price;
                        t.StopIndex = i;
                    }
                    else
                    {
                        break;
                    }
                }
                if (t.StopIndex == -1) t.Trade.StopPrice = null;

                for (var i = t.LimitIndex + 1; i < t.Trade.LimitPrices.Count; i++)
                {
                    var limitPrice = t.Trade.LimitPrices[i];
                    if (limitPrice.Date.Ticks <= timeTicks)
                    {
                        t.Trade.LimitPrice = limitPrice.Price;
                        t.LimitIndex = i;
                    }
                    else
                    {
                        break;
                    }
                }
                if (t.LimitIndex == -1) t.Trade.LimitPrice = null;

                for (var i = t.OrderIndex + 1; i < t.Trade.OrderPrices.Count; i++)
                {
                    var orderPrice = t.Trade.OrderPrices[i];
                    if (orderPrice.Date.Ticks <= timeTicks)
                    {
                        t.Trade.OrderPrice = orderPrice.Price;
                        t.OrderIndex = i;
                    }
                    else
                    {
                        break;
                    }
                }
                if (t.OrderIndex == -1) t.Trade.OrderPrice = null;
            }
        }

        private static void TryCloseOpenTrades(List<TradeWithStopLimitIndex> openTrades, List<Trade> closedTrades, Candle m1Candle)
        {
            for (var ii = openTrades.Count - 1; ii >= 0; ii--)
            {
                var trade = openTrades[ii];

                if (trade.Trade.EntryDateTime != null && m1Candle.OpenTimeTicks >= trade.Trade.EntryDateTime.Value.Ticks)
                {
                    trade.Trade.SimulateTrade(m1Candle, out _);
                }


                if (trade.Trade.CloseDateTime != null)
                {
                    closedTrades.Add(trade.Trade);
                    openTrades.RemoveAt(ii);
                }
            }
        }

        private static void FillOrders(List<TradeWithStopLimitIndex> orders, List<TradeWithStopLimitIndex> openTrades, Candle m1Candle)
        {
            for (var ii = 0; ii < orders.Count; ii++)
            {
                var order = orders[ii];
                var candleCloseTimeTicks = m1Candle.CloseTimeTicks;

                if (order.Trade.OrderDateTime != null && candleCloseTimeTicks < order.Trade.OrderDateTime.Value.Ticks)
                {
                    break;
                }

                orders[ii].Trade.SimulateTrade(m1Candle, out _);

                if (orders[ii].Trade.EntryDateTime != null)
                {
                    openTrades.Add(orders[ii]);
                    orders.RemoveAt(ii);
                    ii--;
                }
                else if (orders[ii].Trade.CloseDateTime != null)
                {
                    orders.RemoveAt(ii);
                    ii--;
                }
            }
        }


        private void AddNewTrades(List<TradeWithStopLimitIndex> ordersList, List<TradeWithStopLimitIndex> openTrades, IStrategy strategy, MarketDetails market,
            TimeframeLookup<List<CandleAndIndicators>> timeframeCurrentCandles,
            Candle latestCandle, UpdateTradeStrategyAttribute updateTradeStrategy, DateTime currentTime)
        {
            var newTrades = strategy.CreateNewTrades(market, timeframeCurrentCandles, ordersList.Concat(openTrades).Select(t => t.Trade).ToList(), _calculatorService, currentTime);

            if (newTrades != null && newTrades.Count > 0)
            {
                newTrades.ForEach(t => t.Strategies = strategy.Name);
                var latestBidPrice = (decimal)latestCandle.CloseBid;
                var latestAskPrice = (decimal)latestCandle.CloseAsk;

                foreach (var trade in newTrades.Where(t => t.OrderPrice != null && t.EntryPrice == null))
                {
                    if (trade.TradeDirection == TradeDirection.Long)
                    {
                        trade.OrderType = (float)trade.OrderPrice.Value <= latestCandle.CloseAsk
                            ? OrderType.LimitEntry
                            : OrderType.StopEntry;
                    }
                    else
                    {
                        trade.OrderType = (float)trade.OrderPrice.Value <= latestCandle.CloseBid
                            ? OrderType.StopEntry
                            : OrderType.LimitEntry;
                    }
                }

                if (updateTradeStrategy != null)
                {
                    newTrades.ForEach(t => t.Custom1 = updateTradeStrategy.GetHashCode());
                }

                RemoveInvalidTrades(newTrades, latestBidPrice, latestAskPrice, _marketDetailsService);

                ordersList.AddRange(newTrades.Where(t => t.EntryDateTime == null && t.OrderDateTime != null).Select(t => new TradeWithStopLimitIndex { Trade = t }));
                openTrades.AddRange(newTrades.Where(t => t.EntryDateTime != null).Select(t => new TradeWithStopLimitIndex { Trade = t }));
            }
        }

        private static void RemoveInvalidTrades(List<Trade> newTrades, decimal latestBidPrice, decimal latestAskPrice, IMarketDetailsService marketDetailsService)
        {
            // Validate trades
            for (var i = newTrades.Count - 1; i >= 0; i--)
            {
                var t = newTrades[i];
                var removed = false;
                if (t.OrderPrice != null)
                {
                    if (t.LimitPrice != null)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.LimitPrice < t.OrderPrice.Value)
                        {
                            Log.Error($"Long trade for {t.Market} has limit price below order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.LimitPrice > t.OrderPrice.Value)
                        {
                            Log.Error($"Short trade for {t.Market} has limit price above order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (t.StopPrices != null && !removed)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.StopPrice > t.OrderPrice.Value)
                        {
                            Log.Error($"Long trade for {t.Market} has stop price above order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.StopPrice < t.OrderPrice.Value)
                        {
                            Log.Error($"Short trade for {t.Market} has stop price below order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (!removed)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.OrderType == OrderType.LimitEntry && t.OrderPrice.Value > latestAskPrice)
                        {
                            Log.Error($"Long trade for {t.Market} has limit entry but order price is above latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Long && t.OrderType == OrderType.StopEntry && t.OrderPrice.Value < latestAskPrice)
                        {
                            Log.Error($"Long trade for {t.Market} has stop entry but order price is below latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.OrderType == OrderType.LimitEntry && t.OrderPrice.Value < latestBidPrice)
                        {
                            Log.Error($"Short trade for {t.Market} has limit entry but order price is below latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.OrderType == OrderType.StopEntry && t.OrderPrice.Value > latestBidPrice)
                        {
                            Log.Error($"Short trade for {t.Market} has stop entry but order price is above latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    var maxPips = 4;
                    if (!removed && t.StopPrice != null)
                    {
                        var stopPips = PipsHelper.GetPriceInPips(Math.Abs(t.StopPrice.Value - t.OrderPrice.Value), marketDetailsService.GetMarketDetails("FXCM", t.Market));
                        if (stopPips <= maxPips)
                        {
                            Log.Error($"Trade for {t.Market} has stop within {maxPips} pips. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (!removed && t.LimitPrice != null)
                    {
                        var limitPips = PipsHelper.GetPriceInPips(Math.Abs(t.LimitPrice.Value - t.OrderPrice.Value), marketDetailsService.GetMarketDetails("FXCM", t.Market));
                        if (limitPips <= maxPips)
                        {
                            Log.Error($"Trade for {t.Market} has stop within {maxPips} pips. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }
                }
            }
        }

        private void UpdateOpenTrades(string market, List<TradeWithStopLimitIndex> openTrades,
            long timeTicks,
            TimeframeLookup<List<CandleAndIndicators>> timeframesCurrentCandles, Action<UpdateTradeParameters> updateOpenTradesAction)
        {
            if (openTrades.Count == 0 || updateOpenTradesAction == null)
            {
                return;
            }

            for (var i = openTrades.Count - 1; i >= 0; i--)
            {
                var openTrade = openTrades[i];
                updateOpenTradesAction(new UpdateTradeParameters
                {
                    Market = market,
                    Trade = openTrade.Trade,
                    TimeframeCurrentCandles = timeframesCurrentCandles,
                    TimeTicks = timeTicks
                });
            }
        }

        public static TimeframeLookupBasicCandleAndIndicators PopulateCandles(IBroker broker, IStrategy strategy, string market, IBrokersCandlesService candlesService)
        {
            var requiredTimeframesAndIndicators = GetRequiredTimeframesAndIndicators(strategy);

            var timeframeIndicators = new TimeframeLookup<Indicator[]>();
            foreach (var r in requiredTimeframesAndIndicators)
            {
                if (r.Indicators.Length > 0)
                {
                    timeframeIndicators.Add(r.Timeframe, r.Indicators);
                }
            }

            return TimeframeLookupBasicCandleAndIndicators.PopulateCandles(
                broker, market, requiredTimeframesAndIndicators.Select(x => x.Timeframe).ToArray(), timeframeIndicators, candlesService);
        }
    }
}