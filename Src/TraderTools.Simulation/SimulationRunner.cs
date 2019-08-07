using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Basics.Helpers;
using TraderTools.Core.Trading;

namespace TraderTools.Simulation
{
    /// <summary>
    /// -Set trade expire/close/entry/etc to M1 candle open time so they appear in the correct position on the chart.
    /// </summary>
    public class SimulationRunner
    {
        private IBrokersCandlesService _candlesService;
        private readonly ITradeDetailsAutoCalculatorService _calculatorService;
        private readonly IMarketDetailsService _marketDetailsService;
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private CodeBlockStats _updateOpenTradesBlockStats;
        private CodeBlockStats _createNewTradesBlockStats;
        private CodeBlockStats _fillOrExpireOrdersBlockStats;
        private CodeBlockStats _closeTradesBlockStats;
        private CodeBlockStats _updateIndicatorsCompletedCandlesBlockStats;
        private CodeBlockStats _updateIncompleteCandlesBlockStats;
        private CodeBlockStats _unloadCandlesAndGCCollectBlockStats;
        private static CodeBlockStats _populateCandlesBlockStats;
        private CodeBlockStats _simulateTradesBlockStats;
        private CodeBlockStats _createOrdersBlockStats;
        private List<CodeBlockStats> _codeBlockStatsList = new List<CodeBlockStats>();

        static SimulationRunner()
        {
            _populateCandlesBlockStats = new CodeBlockStats("Populate candles");
        }

        public SimulationRunner(IBrokersCandlesService candleService, ITradeDetailsAutoCalculatorService calculatorService, IMarketDetailsService marketDetailsService)
        {
            _candlesService = candleService;
            _calculatorService = calculatorService;
            _marketDetailsService = marketDetailsService;

            _updateOpenTradesBlockStats = LogCodeBlockStats.GetCodeBlockStats("Update open trades");
            _createNewTradesBlockStats = new CodeBlockStats("Create new trades");
            _fillOrExpireOrdersBlockStats = new CodeBlockStats("Fill or expire orders");
            _closeTradesBlockStats = new CodeBlockStats("Close trades");
            _updateIndicatorsCompletedCandlesBlockStats = new CodeBlockStats("Update indicators on completed candles");
            _updateIncompleteCandlesBlockStats = new CodeBlockStats("Update incomplete candles");
            _unloadCandlesAndGCCollectBlockStats = new CodeBlockStats("Unload candles and GC collect");
            _simulateTradesBlockStats = new CodeBlockStats("Simulate trades");
            _createOrdersBlockStats = new CodeBlockStats("Create orders");

            _codeBlockStatsList.AddRange(new[]
            {
                _updateOpenTradesBlockStats, _createNewTradesBlockStats, _fillOrExpireOrdersBlockStats, _closeTradesBlockStats,
                _updateIndicatorsCompletedCandlesBlockStats, _updateIncompleteCandlesBlockStats, _unloadCandlesAndGCCollectBlockStats, _populateCandlesBlockStats,
                _simulateTradesBlockStats, _createOrdersBlockStats
            });
        }

        public static CachedDetails Cache { get; set; } = new CachedDetails();

        public List<Trade> Run(IStrategy strategy, MarketDetails market, IBroker broker,
            DateTime? earliest = null, DateTime? latest = null,
            bool simulateTrades = true, bool updatePrices = false, bool cacheCandles = true)
        {
            if (strategy == null)
            {
                return null;
            }

            var logTimeSeconds = 15;
            var logInterval = 1000;
            var startTime = Environment.TickCount;

            Log.Debug($"Running - {strategy.Name} on {market}");
            var timeframeCandleIndexes = new TimeframeLookup<int>();
            var timeframesCurrentCandles = new TimeframeLookup<List<BasicCandleAndIndicators>>();
            var orders = new List<Trade>();
            var openTrades = new List<Trade>();
            var closedTrades = new List<Trade>();
            var strategyRequiredTimeframes = strategy.CandleTimeframesRequired;


            var timeframesList = new List<Timeframe>();
            var lowestStrategyTimeframe = strategyRequiredTimeframes.OrderBy(x => x).First();
            if (lowestStrategyTimeframe > Timeframe.H2)
            {
                lowestStrategyTimeframe = Timeframe.H2;
            }

            foreach (var t in new[] { Timeframe.M15 }.Union(strategyRequiredTimeframes).Union(new List<Timeframe> { lowestStrategyTimeframe }))
            {
                if (t == Timeframe.M15)
                {
                    if (!timeframesList.Contains(t))
                    {
                        timeframesList.Add(t);
                    }
                }

                if (strategyRequiredTimeframes.Contains(t))
                {
                    timeframesList.Add(t);
                }
            }

            var timeframes = timeframesList.ToArray();
            var timeframesAllCandles = PopulateCandles(broker, market.Name, simulateTrades, timeframes, strategy, updatePrices, cacheCandles, earliest, latest, _candlesService, out var m1Candles);
            var timeframesExcludingM1M15 = timeframes.Where(t => t != Timeframe.M15 && t != Timeframe.M1).ToList();

            var lowestStrategyTimeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(lowestStrategyTimeframe);
            var lowestStrategyTimeframeCandles = timeframesAllCandles[lowestStrategyTimeframeLookupIndex].Count;

            foreach (var timeframe in timeframesExcludingM1M15)
            {
                var timeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);

                if (timeframeCandleIndexes[timeframeLookupIndex] == 0)
                {
                    timeframesCurrentCandles[timeframeLookupIndex] = new List<BasicCandleAndIndicators>();
                }
            }

            var stop = false;
            DateTime h2Time = new DateTime(0);

            // Process H2 candles and above to create new trades
            using (_createOrdersBlockStats.Log())
            {
                var logIntervalSeconds = 5;
                var nextLogTime = DateTime.UtcNow.AddSeconds(logIntervalSeconds);

                while (timeframeCandleIndexes[lowestStrategyTimeframeLookupIndex] < lowestStrategyTimeframeCandles)
                {
                    if (stop) break;

                    // Setup
                    ICandle smallestTimeframeCandle = null;

                    // Log progress
                    if (DateTime.UtcNow > nextLogTime)
                    {
                        var percent = (timeframeCandleIndexes[lowestStrategyTimeframeLookupIndex] * 100.0) /
                                      (double)lowestStrategyTimeframeCandles;
                        Log.Info($"StrategyRunner: {market} {percent:0.00}% complete - created {orders.Count + closedTrades.Count + openTrades.Count} trades");
                        nextLogTime = DateTime.UtcNow.AddSeconds(logIntervalSeconds);
                    }

                    // Update candles for each timeframe
                    foreach (var timeframe in timeframesExcludingM1M15)
                    {
                        var timeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);
                        var timeframeAllCandles = timeframesAllCandles[timeframeLookupIndex];
                        var timeframeCurrentCandles = timeframesCurrentCandles[timeframeLookupIndex];

                        var start = timeframeCandleIndexes[timeframeLookupIndex];
                        for (var ii = start; ii < timeframeAllCandles.Count; ii++)
                        {
                            var timeframeCandle = timeframeAllCandles[ii];

                            if (latest != null && timeframe == lowestStrategyTimeframe && timeframeCandle.OpenTime() > latest.Value)
                            {
                                stop = true;
                                break;
                            }

                            if (timeframe == lowestStrategyTimeframe)
                            {
                                h2Time = timeframeCandle.OpenTime();
                            }

                            if (timeframe == lowestStrategyTimeframe && timeframeCandle.IsComplete == 0)
                            {
                                timeframeCandleIndexes[timeframeLookupIndex] = ii + 1;
                                continue;
                            }
                            else if (timeframe == lowestStrategyTimeframe && timeframeCandle.IsComplete == 1)
                            {
                                timeframeCandleIndexes[timeframeLookupIndex] = ii + 1;
                                timeframeCurrentCandles.Add(timeframeCandle);
                                smallestTimeframeCandle = timeframeCandle;
                                break;
                            }
                            else if (timeframe == lowestStrategyTimeframe ||
                                     (smallestTimeframeCandle != null && timeframeCandle.CloseTimeTicks <= smallestTimeframeCandle.CloseTimeTicks))
                            {
                                // Remove incomplete candles if not D1 Tiger or if is D1 Tiger and new candle is complete
                                for (var i = timeframeCurrentCandles.Count - 1; i >= 0; i--)
                                {
                                    if (timeframeCurrentCandles[i].IsComplete == 0)
                                    {
                                        timeframeCurrentCandles.RemoveAt(i);
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }

                                timeframeCurrentCandles.Add(timeframeCandle);
                                timeframeCandleIndexes[timeframeLookupIndex] = ii + 1;
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (stop) break;
                    }

                    // Try to create new orders
                    if (earliest == null || h2Time >= earliest.Value)
                    {
                        var tickStart = Environment.TickCount;
                        try
                        {
                            AddNewTrades(orders, strategy, market, timeframesCurrentCandles, timeframesExcludingM1M15);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Unable to create trades", ex);
                        }
                        finally
                        {
                            _createNewTradesBlockStats.AddTime(Environment.TickCount - tickStart);
                        }
                    }
                }
            }

            // Find cached trades
            lock (Cache.TradesLookup)
            {
                foreach (var t in orders)
                {
                    if (Cache.TradesLookup.TryGetValue(TradeDetailsKey.Create(t), out var existingTrade))
                    {
                        if (existingTrade.CloseDateTime != null)
                        {
                            closedTrades.Add(existingTrade);
                            orders.Remove(t);
                        }
                        else if (existingTrade.EntryDateTime != null)
                        {
                            openTrades.Add(existingTrade);
                            orders.Remove(t);
                        }
                    }
                }
            }

            orders = orders.OrderBy(x => x.OrderDateTime.Value).ToList();

            if (simulateTrades)
            {
                // Simulate trades
                SimulateTrades(strategy, market, orders, openTrades, closedTrades, timeframesExcludingM1M15, m1Candles, timeframesAllCandles);
            }

            Log.Info($"Run complete in {Environment.TickCount - startTime}ms - {strategy.Name} - {market.Name} - Trades: {orders.Count + closedTrades.Count + openTrades.Count} Completed trades: {closedTrades.Count()} Sum R: {closedTrades.Sum(t => t.RMultiple):0.00}");
            LogCodeBlockStats.LogCodeBlockStatsReport(_codeBlockStatsList, singleLine: false);

            return orders.Union(closedTrades).Union(openTrades).ToList();
        }

        public void SimulateTrades(IStrategy strategy, MarketDetails market, List<Trade> orders, List<Trade> openTrades, List<Trade> closedTrades,
            List<Timeframe> timeframesExcludingM1M15,
            List<ICandle> m1Candles, TimeframeLookupBasicCandleAndIndicators timeframesAllCandles)
        {
            orders = orders.OrderBy(x => x.OrderDateTime).ToList();
            TimeframeLookup<int> timeframeCandleIndexes;
            TimeframeLookup<List<BasicCandleAndIndicators>> timeframesCurrentCandles;
            using (_simulateTradesBlockStats.Log())
            {
                if (orders.Count > 0 || openTrades.Count > 0)
                {
                    timeframeCandleIndexes = new TimeframeLookup<int>();
                    timeframesCurrentCandles = new TimeframeLookup<List<BasicCandleAndIndicators>>();

                    foreach (var timeframe in timeframesExcludingM1M15)
                    {
                        var timeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);

                        if (timeframeCandleIndexes[timeframeLookupIndex] == 0)
                        {
                            timeframesCurrentCandles[timeframeLookupIndex] = new List<BasicCandleAndIndicators>();
                        }
                    }

                    var loggingIntervalSeconds = 5;
                    var nextLogTime = DateTime.UtcNow.AddSeconds(loggingIntervalSeconds);

                    for (var i = 0; i < m1Candles.Count; i++)
                    {
                        if (DateTime.UtcNow > nextLogTime)
                        {
                            var percent = (i * 100.0) / m1Candles.Count;
                            Log.Info(
                                $"StrategyRunner: {market.Name} {percent:0.00}% Orders: {orders.Count} Open trades: {openTrades.Count} Closed trades: {closedTrades.Count}");
                            nextLogTime = DateTime.UtcNow.AddSeconds(loggingIntervalSeconds);
                        }

                        var m1Candle = m1Candles[i];

                        // Setup
                        var completedCandleAddedExcludingM1 = false;
                        var candleAddedExcludingM1 = false;

                        // Update candles for each timeframe
                        foreach (var timeframe in timeframesExcludingM1M15)
                        {
                            var timeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);
                            var timeframeAllCandles = timeframesAllCandles[timeframeLookupIndex];
                            var timeframeCurrentCandles = timeframesCurrentCandles[timeframeLookupIndex];

                            for (var ii = timeframeCandleIndexes[timeframeLookupIndex];
                                ii < timeframeAllCandles.Count;
                                ii++)
                            {
                                var timeframeCandle = timeframeAllCandles[ii];
                                if (timeframeCandle.CloseTimeTicks <= m1Candle.CloseTimeTicks)
                                {
                                    // Remove incomplete candles if not D1 Tiger or if is D1 Tiger and new candle is complete

                                    for (var iii = timeframeCurrentCandles.Count - 1; iii >= 0; iii--)
                                    {
                                        if (timeframeCurrentCandles[iii].IsComplete == 0)
                                        {
                                            timeframeCurrentCandles.RemoveAt(iii);
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }

                                    timeframeCurrentCandles.Add(timeframeCandle);
                                    timeframeCandleIndexes[timeframeLookupIndex] = ii + 1;
                                    candleAddedExcludingM1 = true;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }

                        if (candleAddedExcludingM1)
                        {
                            // Update open trades
                            UpdateOpenTrades(strategy, market.Name, openTrades, timeframesCurrentCandles);
                        }

                        // Process orders
                        for (var ii = 0; ii < orders.Count; ii++)
                        {
                            var order = orders[ii];
                            var candleOpenTimeTicks = m1Candle.OpenTimeTicks;
                            var candleCloseTimeTicks = m1Candle.CloseTimeTicks;

                            // If candle's open is before order date or open in past and close not after order date
                            if ((candleOpenTimeTicks <= order.OrderDateTime.Value.Ticks &&
                                 candleCloseTimeTicks <= order.OrderDateTime.Value.Ticks)
                                && candleOpenTimeTicks <= order.OrderDateTime.Value.Ticks)
                            {
                                break;
                            }

                            orders[ii].SimulateTrade(m1Candle, out _);

                            if (orders[ii].EntryDateTime != null)
                            {
                                openTrades.Add(orders[ii]);
                                orders.RemoveAt(ii);
                                ii--;
                            }
                            else if (orders[ii].CloseDateTime != null)
                            {
                                orders.RemoveAt(ii);
                                ii--;
                            }
                        }

                        // Process open trades
                        for (var ii = openTrades.Count - 1; ii >= 0; ii--)
                        {
                            openTrades[ii].SimulateTrade(m1Candle, out _);

                            if (openTrades[ii].CloseDateTime != null)
                            {
                                closedTrades.Add(openTrades[ii]);
                                openTrades.RemoveAt(ii);
                            }
                        }
                    }
                }
            }

            // Run complete - cache results
            var tradesToStore = new List<(TradeDetailsKey, Trade)>();
            foreach (var trade in orders.Union(openTrades).Union(closedTrades))
            {
                tradesToStore.Add((TradeDetailsKey.Create(trade), trade));
            }

            lock (Cache.TradesLookup)
            {
                foreach (var tradeToStore in tradesToStore)
                {
                    Cache.TradesLookup[tradeToStore.Item1] = tradeToStore.Item2;
                }
            }
        }

        private void AddNewTrades(List<Trade> ordersList, IStrategy strategy, MarketDetails market,
            TimeframeLookup<List<BasicCandleAndIndicators>> timeframeCurrentCandles,
            List<Timeframe> timeframesExcludingM1M15)
        {
            var newTrades = strategy.CreateNewTrades(market, timeframeCurrentCandles, null, _calculatorService);

            if (newTrades != null && newTrades.Count > 0)
            {
                newTrades.ForEach(t => t.Strategies = strategy.Name);
                var timeframe = timeframesExcludingM1M15.First();
                var latestBidPrice = (decimal)timeframeCurrentCandles[timeframe][timeframeCurrentCandles[timeframe].Count - 1].CloseBid;
                var latestAskPrice = (decimal)timeframeCurrentCandles[timeframe][timeframeCurrentCandles[timeframe].Count - 1].CloseAsk;
                RemoveInvalidTrades(newTrades, latestBidPrice, latestAskPrice, _marketDetailsService);

                ordersList.AddRange(newTrades);
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
                        if (t.TradeDirection == TradeDirection.Long && t.LimitPrice <= t.OrderPrice.Value)
                        {
                            Log.Error($"Long trade for {t.Market} has limit price below order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.LimitPrice >= t.OrderPrice.Value)
                        {
                            Log.Error($"Short trade for {t.Market} has limit price above order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (t.StopPrices != null && !removed)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.StopPrice >= t.OrderPrice.Value)
                        {
                            Log.Error($"Long trade for {t.Market} has stop price above order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.StopPrice <= t.OrderPrice.Value)
                        {
                            Log.Error($"Short trade for {t.Market} has stop price below order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (!removed)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.OrderType == OrderType.LimitEntry && t.OrderPrice.Value >= latestAskPrice)
                        {
                            Log.Error($"Long trade for {t.Market} has limit entry but order price is above latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Long && t.OrderType == OrderType.StopEntry && t.OrderPrice.Value <= latestAskPrice)
                        {
                            Log.Error($"Long trade for {t.Market} has stop entry but order price is below latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.OrderType == OrderType.LimitEntry && t.OrderPrice.Value <= latestBidPrice)
                        {
                            Log.Error($"Short trade for {t.Market} has limit entry but order price is below latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.OrderType == OrderType.StopEntry && t.OrderPrice.Value >= latestBidPrice)
                        {
                            Log.Error($"Short trade for {t.Market} has stop entry but order price is above latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    var maxPips = 4;
                    if (!removed && t.StopPrice != null)
                    {
                        var stopPips = PipsHelper.GetPriceInPips((decimal)Math.Abs(t.StopPrice.Value - t.OrderPrice.Value), marketDetailsService.GetMarketDetails("FXCM", t.Market));
                        if (stopPips <= maxPips)
                        {
                            Log.Error($"Trade for {t.Market} has stop within {maxPips} pips. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (!removed && t.LimitPrice != null)
                    {
                        var limitPips = PipsHelper.GetPriceInPips((decimal)Math.Abs(t.LimitPrice.Value - t.OrderPrice.Value), marketDetailsService.GetMarketDetails("FXCM", t.Market));
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

        private void UpdateOpenTrades(
            IStrategy strategy, string market, List<Trade> openTrades,
            TimeframeLookup<List<BasicCandleAndIndicators>> timeframeCurrentCandles)
        {
            if (openTrades.Count == 0 || strategy == null)
            {
                return;
            }

            using (_updateOpenTradesBlockStats.Log())
            {
                for (var i = openTrades.Count - 1; i >= 0; i--)
                {
                    var openTrade = openTrades[i];
                    strategy.UpdateExistingOpenTrades(openTrade, market, timeframeCurrentCandles);

                    if (openTrade.CloseDateTime != null)
                    {
                        openTrades.RemoveAt(i);
                    }
                }
            }
        }

        private static void UpdateIndicators(
            TimeframeLookup<List<(Indicator, IIndicator)>> timeframeIndicators,
            int timeframeLookupIndex,
            BasicCandleAndIndicators timeframeCandle)
        {
            var indicators = timeframeIndicators[timeframeLookupIndex];
            if (indicators != null)
            {
                for (var i = 0; i < indicators.Count; i++)
                {
                    var indicator = indicators[i];
                    var signalAndValue = indicator.Item2.Process(timeframeCandle);
                    timeframeCandle.Set(indicator.Item1, signalAndValue);
                }
            }
        }

        public enum IntervalForCandles
        {
            Minutes15 = 1,
            Minutes30 = 2
        }

        private static TimeframeLookupBasicCandleAndIndicators SetupCandlesWithIndicators(
            TimeframeLookup<List<ICandle>> timeframeAllCandles,
            string market,
            Timeframe[] timeframes,
            IStrategy strategy)
        {
            var timeframeIndicators = IndicatorsHelper.CreateIndicators(strategy.CreateTimeframeIndicators());

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
                        timeframeAllCandlesProcessed[timeframe] = new List<BasicCandleAndIndicators>();
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
                            var candleWithIndicators = new BasicCandleAndIndicators(timeframeCandle, timeframeMaxIndicatorValues[timeframeLookupIndex]);
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
                        BasicCandleAndIndicators? prevCandle = timeframeAllCandlesProcessed[timeframeLookupIndex].Count > 0
                            ? timeframeAllCandlesProcessed[timeframeLookupIndex][timeframeAllCandlesProcessed[timeframeLookupIndex].Count - 1]
                            : (BasicCandleAndIndicators?)null;

                        if (prevCandle != null && prevCandle.Value.IsComplete == 0)
                        {
                            // Add updated incomplete candle
                            var incompleteCandle = new BasicCandleAndIndicators(
                                prevCandle.Value.OpenTimeTicks,
                                m15Candle.CloseTimeTicks,
                                prevCandle.Value.OpenBid,
                                (float)(m15Candle.HighBid > prevCandle.Value.HighBid ? m15Candle.HighBid : prevCandle.Value.HighBid),
                                (float)(m15Candle.LowBid < prevCandle.Value.LowBid ? m15Candle.LowBid : prevCandle.Value.LowBid),
                                (float)m15Candle.CloseBid,
                                prevCandle.Value.OpenAsk,
                                (float)(m15Candle.HighAsk > prevCandle.Value.HighAsk ? m15Candle.HighAsk : prevCandle.Value.HighAsk),
                                (float)(m15Candle.LowAsk < prevCandle.Value.LowAsk ? m15Candle.LowAsk : prevCandle.Value.LowAsk),
                                (float)m15Candle.CloseAsk,
                                0,
                                timeframeMaxIndicatorValues[timeframeLookupIndex]
                            );

                            timeframeAllCandlesProcessed[timeframeLookupIndex].Add(incompleteCandle);
                            UpdateIndicators(timeframeIndicators, timeframeLookupIndex, incompleteCandle);
                        }
                        else
                        {
                            // Add new incomplete candle
                            var incompleteCandle = new BasicCandleAndIndicators(
                                m15Candle.OpenTimeTicks,
                                m15Candle.CloseTimeTicks,
                                (float)m15Candle.OpenBid,
                                (float)m15Candle.HighBid,
                                (float)m15Candle.LowBid,
                                (float)m15Candle.CloseBid,
                                (float)m15Candle.OpenAsk,
                                (float)m15Candle.HighAsk,
                                (float)m15Candle.LowAsk,
                                (float)m15Candle.CloseAsk,
                                0,
                                timeframeMaxIndicatorValues[timeframeLookupIndex]
                            );

                            timeframeAllCandlesProcessed[timeframeLookupIndex].Add(incompleteCandle);
                            UpdateIndicators(timeframeIndicators, timeframeLookupIndex, incompleteCandle);
                        }
                    }
                }
            }

            var intervalForTigerCandles = IntervalForCandles.Minutes30;
            var intervalCount = 0;

            GC.Collect();

            return timeframeAllCandlesProcessed;
        }

        public TimeframeLookupBasicCandleAndIndicators PopulateCandles(
            IBroker broker,
            string market,
            bool getM1Candles,
            Timeframe[] timeframesForStrategy,
            IStrategy strategy,
            bool updatePrices,
            bool cacheCandles,
            DateTime? earliest,
            DateTime? latest,
            IBrokersCandlesService candlesService,
            out List<ICandle> m1Candles)
        {
            m1Candles = null;
            var timeframes = timeframesForStrategy.ToList();

            var timeframesExcludingM1 = timeframes.ToArray();
            var timeframesExcludingM1M15 = timeframes.Where(t => t != Timeframe.M15).ToArray();
            var earliestCandle = earliest != null ? (DateTime?)earliest.Value.AddDays(-100) : null;

            var ret = new TimeframeLookupBasicCandleAndIndicators();

            using (_populateCandlesBlockStats.Log())
            {
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

                        lock (Cache.M1CandlesLookup)
                        {
                            Cache.M1CandlesLookup[market] = m1Candles;
                        }

                        // Unload in separate block so M1 candles can be collected
                        candlesService.UnloadCandles(market, Timeframe.M1, broker);

                        GC.Collect();
                    }
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
                    var candlesLookup = new TimeframeLookup<List<ICandle>>();
                    foreach (var timeframe in timeframesExcludingM1)
                    {
                        var candles = candlesService.GetCandles(broker, market, timeframe, updatePrices, cacheData: false,
                            minOpenTimeUtc: earliestCandle, maxCloseTimeUtc: latest);
                        candlesLookup[timeframe] = candles;
                    }

                    ret = SetupCandlesWithIndicators(candlesLookup, market, timeframesExcludingM1M15, strategy);

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