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
        private const int LogIntervalSeconds = 5;


        public SimulationRunner(IBrokersCandlesService candleService, ITradeDetailsAutoCalculatorService calculatorService, IMarketDetailsService marketDetailsService,
            SimulationRunnerFlags options = SimulationRunnerFlags.Default)
        {
            _candlesService = candleService;
            _calculatorService = calculatorService;
            _marketDetailsService = marketDetailsService;
            _options = options;
        }

        public static CachedDetails Cache { get; set; } = new CachedDetails();

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
            var timeframesAllCandles = PopulateCandles(broker, market.Name, true, strategyTimeframes,
                timeframeIndicators, _candlesService, out var m1Candles, updatePrices, cacheCandles, earliest, latest,
                _options);

            var timeframeCandleIndexes = new TimeframeLookup<int>();
            var timeframesCurrentCandles = new TimeframeLookup<List<CandleAndIndicators>>();

            foreach (var timeframe in strategyTimeframes)
            {
                var timeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);

                if (timeframeCandleIndexes[timeframeLookupIndex] == 0)
                {
                    timeframesCurrentCandles[timeframeLookupIndex] = new List<CandleAndIndicators>();
                }
            }

            var orders = new List<TradeWithStopLimitIndex>();
            var openTrades = new List<TradeWithStopLimitIndex>();
            var closedTrades = new List<Trade>();

            var nextLogTime = DateTime.UtcNow.AddSeconds(LogIntervalSeconds);

            // Move through all M1 candles
            for (var i = 0; i < m1Candles.Count; i++)
            {
                var m1Candle = m1Candles[i];

                // Move candles forward
                var strategyTimeframeCandleUpdated = false;
                var strategyTimeframeCompleteCandleUpdated = false;
                foreach (var timeframe in strategyTimeframes)
                {
                    var timeframeIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);
                    var currentTimeframeCandleIndex = timeframeCandleIndexes[timeframe];
                    var currentCandles = timeframesCurrentCandles[timeframeIndex];
                    var allCandles = timeframesAllCandles[timeframeIndex];
                    var nextTimeframeCandleIndex = currentTimeframeCandleIndex + 1;

                    if (allCandles[nextTimeframeCandleIndex].Candle.CloseTimeTicks <= m1Candle.CloseTimeTicks)
                    {
                        // Remove incomplete candle
                        if (currentCandles.Count > 0 && currentCandles[currentCandles.Count - 1].Candle.IsComplete == 0)
                        {
                            currentCandles.RemoveAt(currentCandles.Count - 1);
                        }

                        // Add next candle
                        var newCandle = allCandles[nextTimeframeCandleIndex];
                        currentCandles.Add(newCandle);
                        timeframeCandleIndexes[timeframe] = nextTimeframeCandleIndex;
                        strategyTimeframeCandleUpdated = true;
                        if (newCandle.Candle.IsComplete == 1) strategyTimeframeCompleteCandleUpdated = true;
                    }
                }

                if (strategyTimeframeCompleteCandleUpdated)
                {
                    AddNewTrades(orders, openTrades, strategy, market, timeframesCurrentCandles, m1Candle, updateTradesStrategy);
                }

                if (strategyTimeframeCandleUpdated)
                {
                    // Update open trades
                    UpdateOpenTrades(market.Name, openTrades, m1Candle.CloseTimeTicks, timeframesCurrentCandles, parameters => updateTradesStrategy?.UpdateTrade(parameters));
                }

                // Validate and update stops/limts/orders 
                if (!_options.HasFlag(SimulationRunnerFlags.DoNotValidateStopsLimitsOrders))
                {
                    ValidateAndUpdateStopsLimitsOrders(orders.Union(openTrades).ToList(), m1Candle);
                }

                // Process orders
                FillOrders(orders, openTrades, m1Candle);

                // Process open trades
                TryCloseOpenTrades(openTrades, closedTrades, m1Candle);

                if (DateTime.UtcNow > nextLogTime)
                {
                    var percent = (i * 100.0) / m1Candles.Count;
                    Log.Info($"StrategyRunner: {market} {percent:0.00}% complete - created {orders.Count + closedTrades.Count + openTrades.Count} trades");
                    nextLogTime = DateTime.UtcNow.AddSeconds(LogIntervalSeconds);
                }
            }

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
            Candle latestCandle, UpdateTradeStrategyAttribute updateTradeStrategy)
        {
            if (strategy is StrategyBase b) b.CurrentCandle = latestCandle;
            var newTrades = strategy.CreateNewTrades(market, timeframeCurrentCandles, null, _calculatorService);

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

        private static void UpdateIndicators(
            TimeframeLookup<List<(Indicator, IIndicator)>> timeframeIndicators,
            int timeframeLookupIndex,
            CandleAndIndicators timeframeCandle)
        {
            var indicators = timeframeIndicators[timeframeLookupIndex];
            if (indicators != null)
            {
                for (var i = 0; i < indicators.Count; i++)
                {
                    var indicator = indicators[i];
                    var signalAndValue = indicator.Item2.Process(timeframeCandle.Candle);
                    timeframeCandle.Set(indicator.Item1, signalAndValue);
                }
            }
        }

        private static TimeframeLookupBasicCandleAndIndicators SetupCandlesWithIndicators(
            TimeframeLookup<List<Candle>> timeframeAllCandles,
            Timeframe[] timeframes,
            TimeframeLookup<Indicator[]> timeframeIndicatorsRequired)
        {
            var timeframeIndicators = IndicatorsHelper.CreateIndicators(timeframeIndicatorsRequired);

            var timeframeMaxIndicatorValues = new TimeframeLookup<int>();
            foreach (var kvp in timeframeIndicators)
            {
                timeframeMaxIndicatorValues[kvp.Key] = kvp.Value != null ? kvp.Value.Max(t => (int)t.Item1) + 1 : 0;
            }

            var m15Candles = timeframeAllCandles[Timeframe.M15];
            var timeframeExclD1Tiger = timeframes.ToList();
            var timeframeAllCandlesProcessed = new TimeframeLookupBasicCandleAndIndicators();
            var timeframeCandleIndexes = new TimeframeLookup<int>();
            var m15TimeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(Timeframe.M15);

            for (var i = 0; i < m15Candles.Count; i++)
            {
                var m15Candle = timeframeAllCandles[m15TimeframeLookupIndex][i];

                // See if any other candles have completed
                foreach (var timeframe in timeframeExclD1Tiger)
                {
                    if (i == 0)
                    {
                        timeframeAllCandlesProcessed[timeframe] = new List<CandleAndIndicators>();
                    }

                    // Try to added completed candles
                    var completedCandleAdded = false;
                    var timeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);
                    for (var ii = timeframeCandleIndexes[timeframeLookupIndex]; ii < timeframeAllCandles[timeframeLookupIndex].Count; ii++)
                    {
                        var timeframeCandle = timeframeAllCandles[timeframeLookupIndex][ii];

                        // Look for completed candle
                        if (timeframeCandle.CloseTimeTicks <= m15Candle.CloseTimeTicks)
                        {
                            completedCandleAdded = true;
                            var candleWithIndicators = new CandleAndIndicators(timeframeCandle, timeframeMaxIndicatorValues[timeframeLookupIndex]);
                            UpdateIndicators(timeframeIndicators, timeframeLookupIndex, candleWithIndicators);
                            timeframeCandleIndexes[timeframeLookupIndex] = ii + 1;
                            timeframeAllCandlesProcessed[timeframeLookupIndex].Add(candleWithIndicators);
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Try to add incomplete candle
                    if (!completedCandleAdded)
                    {
                        CandleAndIndicators? prevCandle = timeframeAllCandlesProcessed[timeframeLookupIndex].Count > 0
                            ? timeframeAllCandlesProcessed[timeframeLookupIndex][timeframeAllCandlesProcessed[timeframeLookupIndex].Count - 1]
                            : (CandleAndIndicators?)null;

                        if (prevCandle != null && prevCandle.Value.Candle.IsComplete == 0)
                        {
                            // Add updated incomplete candle
                            var incompleteCandle = new CandleAndIndicators(
                                prevCandle.Value.Candle.OpenTimeTicks,
                                m15Candle.CloseTimeTicks,
                                prevCandle.Value.Candle.OpenBid,
                                m15Candle.HighBid > prevCandle.Value.Candle.HighBid ? m15Candle.HighBid : prevCandle.Value.Candle.HighBid,
                                m15Candle.LowBid < prevCandle.Value.Candle.LowBid ? m15Candle.LowBid : prevCandle.Value.Candle.LowBid,
                                m15Candle.CloseBid,
                                prevCandle.Value.Candle.OpenAsk,
                                m15Candle.HighAsk > prevCandle.Value.Candle.HighAsk ? m15Candle.HighAsk : prevCandle.Value.Candle.HighAsk,
                                m15Candle.LowAsk < prevCandle.Value.Candle.LowAsk ? m15Candle.LowAsk : prevCandle.Value.Candle.LowAsk,
                                m15Candle.CloseAsk,
                                0,
                                timeframeMaxIndicatorValues[timeframeLookupIndex]
                            );

                            timeframeAllCandlesProcessed[timeframeLookupIndex].Add(incompleteCandle);
                            UpdateIndicators(timeframeIndicators, timeframeLookupIndex, incompleteCandle);
                        }
                        else
                        {
                            // Add new incomplete candle
                            var incompleteCandle = new CandleAndIndicators(
                                m15Candle.OpenTimeTicks,
                                m15Candle.CloseTimeTicks,
                                m15Candle.OpenBid,
                                m15Candle.HighBid,
                                m15Candle.LowBid,
                                m15Candle.CloseBid,
                                m15Candle.OpenAsk,
                                m15Candle.HighAsk,
                                m15Candle.LowAsk,
                                m15Candle.CloseAsk,
                                0,
                                timeframeMaxIndicatorValues[timeframeLookupIndex]
                            );

                            timeframeAllCandlesProcessed[timeframeLookupIndex].Add(incompleteCandle);
                            UpdateIndicators(timeframeIndicators, timeframeLookupIndex, incompleteCandle);
                        }
                    }
                }
            }

            GC.Collect();

            return timeframeAllCandlesProcessed;
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

            return PopulateCandles(broker, market, false,
                requiredTimeframesAndIndicators.Select(x => x.Timeframe).ToArray(), timeframeIndicators, candlesService, out _);
        }

        public static TimeframeLookupBasicCandleAndIndicators PopulateCandles(
            IBroker broker,
            string market,
            bool getM1Candles,
            Timeframe[] timeframesForStrategy,
            TimeframeLookup<Indicator[]> timeframeIndicatorsRequired,
            IBrokersCandlesService candlesService,
            out List<Candle> m1Candles,
            bool updatePrices = false,
            bool cacheCandles = false,
            DateTime? earliest = null,
            DateTime? latest = null,
            SimulationRunnerFlags options = SimulationRunnerFlags.Default)
        {
            m1Candles = null;
            var timeframes = timeframesForStrategy.ToList();

            var timeframesExcludingM1 = timeframes.Union(new[] { Timeframe.M15 }).Distinct().ToArray();
            var timeframesExcludingM1M15 = timeframes.Where(t => t != Timeframe.M15).ToArray();
            var earliestCandle = earliest != null ? (DateTime?)earliest.Value.AddDays(-100) : null;

            var ret = new TimeframeLookupBasicCandleAndIndicators();

            if (getM1Candles)
            {
                // Get M1 candles
                bool gotM1CandlesFromCache;

                lock (Cache.M1CandlesLookup)
                {
                    gotM1CandlesFromCache = Cache.M1CandlesLookup.TryGetValue(market, out m1Candles);
                }

                if (!gotM1CandlesFromCache)
                {
                    m1Candles = candlesService.GetCandles(broker, market, Timeframe.M1, updatePrices,
                        cacheData: false, minOpenTimeUtc: earliestCandle, maxCloseTimeUtc: latest);

                    if (!options.HasFlag(SimulationRunnerFlags.DoNotCacheM1Candles))
                    {
                        lock (Cache.M1CandlesLookup)
                        {
                            Cache.M1CandlesLookup[market] = m1Candles;
                        }
                    }

                    // Unload in separate block so M1 candles can be collected
                    candlesService.UnloadCandles(market, Timeframe.M1, broker);

                    GC.Collect();
                }

                // Get existing candles
                var missingCandles = false;
                foreach (var timeframe in timeframesExcludingM1M15)
                {
                    lock (Cache.CandlesLookup)
                    {
                        if (Cache.CandlesLookup.ContainsKey(market) && Cache.CandlesLookup[market][timeframe] != null)
                        {
                            ret.Add(timeframe, Cache.CandlesLookup[market][timeframe]);
                        }
                        else
                        {
                            if (timeframesForStrategy.Contains(timeframe))
                            {
                                missingCandles = true;
                            }

                            break;
                        }
                    }
                }

                // If there is any missing candle timeframes, then recalculate all candles
                if (missingCandles)
                {
                    var candlesLookup = new TimeframeLookup<List<Candle>>();
                    foreach (var timeframe in timeframesExcludingM1)
                    {
                        var candles = candlesService.GetCandles(broker, market, timeframe, updatePrices, cacheData: false,
                            minOpenTimeUtc: earliestCandle, maxCloseTimeUtc: latest);
                        candlesLookup[timeframe] = candles;
                    }

                    ret = SetupCandlesWithIndicators(candlesLookup, timeframesExcludingM1M15, timeframeIndicatorsRequired);

                    if (cacheCandles)
                    {
                        lock (Cache.CandlesLookup)
                        {
                            Cache.CandlesLookup[market] = ret;
                        }
                    }
                }

                // Unload in separate block so there is no references to the candles
                if (missingCandles)
                {
                    if (!timeframesForStrategy.Contains(Timeframe.D1))
                    {
                        ret[Timeframe.D1] = null;
                    }

                    foreach (var timeframe in timeframesExcludingM1)
                    {
                        candlesService.UnloadCandles(market, timeframe, broker);
                    }

                    GC.Collect();
                }
            }

            return ret;
        }
    }
}