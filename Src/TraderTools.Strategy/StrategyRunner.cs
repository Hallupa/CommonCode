using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Basics.Helpers;
using TraderTools.Core;
using TraderTools.Core.Helpers;
using TraderTools.Core.Services;
using TraderTools.Core.Trading;

namespace TraderTools.Strategy
{
    /// <summary>
    /// -Set trade expire/close/entry/etc to M1 candle open time so they appear in the correct position on the chart.
    /// </summary>
    public class StrategyRunner
    {
        private IBrokersCandlesService _candlesService;
        private readonly ITradeDetailsAutoCalculatorService _calculatorService;
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private StopPoint _stopPoint;
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

        static StrategyRunner()
        {
            _populateCandlesBlockStats = new CodeBlockStats("Populate candles");
        }

        public StrategyRunner(IBrokersCandlesService candleService, ITradeDetailsAutoCalculatorService calculatorService)
        {
            _candlesService = candleService;
            _calculatorService = calculatorService;

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

        public List<TradeDetails> Run(IStrategy strategy, MarketDetails market, IBroker broker,
            out int expectedTrades, out int expectedTradesFound,
            DateTime? earliest = null, DateTime? latest = null,
            StopPoint stopPoint = null, bool simulateTrades = true, bool updatePrices = false, bool cacheCandles = true)
        {
            expectedTrades = 0;
            expectedTradesFound = 0;
            if (strategy == null)
            {
                return null;
            }

            var logTimeSeconds = 15;
            var lastLogTime = DateTime.Now;
            var logInterval = 1000;
            var startTime = Environment.TickCount;

            Log.Debug($"Running - {strategy.Name} on {market}");
            _stopPoint = stopPoint;
            var timeframeCandleIndexes = new TimeframeLookup<int>();
            var timeframesCurrentCandles = new TimeframeLookup<List<BasicCandleAndIndicators>>();
            var trades = new List<TradeDetails>();
            var orders = new List<TradeDetails>();
            var openTrades = new List<TradeDetails>();
            var closedTrades = new List<TradeDetails>();
            var strategyRequiredTimeframes = strategy.CandleTimeframesRequired;


            var timeframesList = new List<Timeframe>();
            foreach (var t in new[] { Timeframe.M1, Timeframe.M15, Timeframe.H2, Timeframe.H4, Timeframe.D1 })
            {
                if ((t == Timeframe.M1 && simulateTrades) || t == Timeframe.M15)
                {
                    timeframesList.Add(t);
                }

                if (strategyRequiredTimeframes.Contains(t))
                {
                    timeframesList.Add(t);
                }
            }

            var timeframes = timeframesList.ToArray();
            var timeframesAllCandles = PopulateCandles(broker, market.Name, timeframes, strategy, updatePrices, cacheCandles, earliest, latest, _candlesService, out var m1Candles);
            var timeframesExcludingM1M15 = timeframes.Where(t => t != Timeframe.M15 && t != Timeframe.M1).ToList();
            var H2TimeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(Timeframe.H2);
            var totalH2TimeframeCandles = timeframesAllCandles[H2TimeframeLookupIndex].Count;

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

                while (timeframeCandleIndexes[H2TimeframeLookupIndex] < totalH2TimeframeCandles)
                {
                    if (stop) break;

                    // Setup
                    ISimpleCandle smallestTimeframeCandle = null;

                    // Log progress
                    if (DateTime.UtcNow > nextLogTime)
                    {
                        var percent = (timeframeCandleIndexes[H2TimeframeLookupIndex] * 100.0) /
                                      (double)totalH2TimeframeCandles;
                        Log.Info($"StrategyRunner: {market} {percent:0.00}% complete - created {trades.Count} trades");
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

                            if (latest != null && timeframe == Timeframe.H2 && timeframeCandle.OpenTime() > latest.Value)
                            {
                                stop = true;
                                break;
                            }

                            if (timeframe == Timeframe.H2)
                            {
                                h2Time = timeframeCandle.OpenTime();
                            }

                            if (timeframe == Timeframe.H2 && timeframeCandle.IsComplete == 0)
                            {
                                timeframeCandleIndexes[timeframeLookupIndex] = ii + 1;
                                continue;
                            }
                            else if (timeframe == Timeframe.H2 && timeframeCandle.IsComplete == 1)
                            {
                                timeframeCandleIndexes[timeframeLookupIndex] = ii + 1;
                                timeframeCurrentCandles.Add(timeframeCandle);
                                smallestTimeframeCandle = timeframeCandle;
                                break;
                            }
                            else if (timeframe == Timeframe.H2 ||
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
                            CreateNewTrades(strategy, market, timeframesCurrentCandles, strategyRequiredTimeframes, trades, orders);
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

            var updatedTrades = new List<TradeDetails>();
            lock (Cache.TradesLookup)
            {
                foreach (var t in trades)
                {
                    if (Cache.TradesLookup.TryGetValue(TradeDetailsKey.Create(t), out var existingTrade))
                    {
                        updatedTrades.Add(existingTrade);
                        orders.Remove(t);
                    }
                    else
                    {
                        updatedTrades.Add(t);
                    }
                }
            }

            trades = updatedTrades;

            // Simulate trades
            if (simulateTrades)
            {
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
                                Log.Info($"StrategyRunner: {market.Name} {percent:0.00}% Orders: {orders.Count} Open trades: {openTrades.Count} Closed trades: {closedTrades.Count}");
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
                            for (var ii = orders.Count - 1; ii >= 0; ii--)
                            {
                                orders[ii].SimulateTrade(m1Candle.Low, m1Candle.High, m1Candle.Close,
                                    m1Candle.OpenTimeTicks, m1Candle.CloseTimeTicks, out _);

                                if (orders[ii].EntryDateTime != null)
                                {
                                    openTrades.Add(orders[ii]);
                                    orders.RemoveAt(ii);
                                }
                                else if (orders[ii].CloseDateTime != null)
                                {
                                    orders.RemoveAt(ii);
                                }
                            }

                            // Process open trades
                            for (var ii = openTrades.Count - 1; ii >= 0; ii--)
                            {
                                openTrades[ii].SimulateTrade(m1Candle.Low, m1Candle.High, m1Candle.Close,
                                    m1Candle.OpenTimeTicks, m1Candle.CloseTimeTicks, out _);

                                if (openTrades[ii].CloseDateTime != null)
                                {
                                    closedTrades.Add(openTrades[ii]);
                                    openTrades.RemoveAt(ii);
                                }
                            }
                        }
                    }
                }

                // Check results
                var expectedTradesForStrategy = GetTests(market, strategy);
                if (expectedTradesForStrategy.Count > 0)
                {
                    Log.Info($"Found {expectedTradesForStrategy.Count} expected trades");

                    var matchedTrades = 0;
                    foreach (var expectedTrade in expectedTradesForStrategy)
                    {
                        var results = trades.Where(t =>
                                t.OrderDateTime.Value.Year == expectedTrade.Year &&
                                t.OrderDateTime.Value.Month == expectedTrade.Month &&
                                t.OrderDateTime.Value.Day == expectedTrade.Day &&
                                t.OrderDateTime.Value.Hour == expectedTrade.OrderHourUtc)
                            .ToList();

                        if (results.Count > 0)
                        {
                            foreach (var foundTrade in results)
                            {
                                if (foundTrade.Timeframe == expectedTrade.Timeframe
                                    && foundTrade.InitialStopInPips >= expectedTrade.Pips * 0.87M
                                    && foundTrade.InitialStopInPips <= expectedTrade.Pips * 1.13M
                                    && foundTrade.OrderPrice >= (decimal)expectedTrade.OrderPrice * 0.95M
                                    && foundTrade.OrderPrice <= (decimal)expectedTrade.OrderPrice * 1.05M)
                                {
                                    matchedTrades++;
                                    break;
                                }
                            }
                        }
                    }

                    Log.Info($"Expected trades found {matchedTrades}/{expectedTradesForStrategy.Count}");
                    expectedTrades = expectedTradesForStrategy.Count;
                    expectedTradesFound = matchedTrades;
                }

                // Run complete
                var tradesToStore = new List<(TradeDetailsKey, TradeDetails)>();
                foreach (var trade in trades)
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

            Log.Info($"Run complete in {Environment.TickCount - startTime}ms - {strategy.Name} - {market} - Trades: {trades.Count} Completed trades: {trades.Where(t => t.ClosePrice != null).Count()} Sum R: {trades.Where(t => t.ClosePrice != null).Sum(t => t.RMultiple):0.00}");
            LogCodeBlockStats.LogCodeBlockStatsReport(_codeBlockStatsList, singleLine: false);

            return trades;
        }

        private static List<ExpectedTradeAttribute> GetTests(MarketDetails market, IStrategy strategy)
        {
            var expectedTrades = strategy.GetType().GetCustomAttributes<ExpectedTradeAttribute>();

            var ret = new List<ExpectedTradeAttribute>();
            foreach (var expectedTrade in expectedTrades.Where(e => e.Market == market.Name))
            {
                ret.Add(expectedTrade);
            }

            return ret;
        }

        private void CreateNewTrades(IStrategy strategy, MarketDetails market,
            TimeframeLookup<List<BasicCandleAndIndicators>> timeframeCurrentCandles,
            Timeframe[] timeframesForNewTrades,
            List<TradeDetails> trades, List<TradeDetails> orders)
        {
            foreach (var timeframeToTest in timeframesForNewTrades)
            {
                if (timeframeCurrentCandles[timeframeToTest].Count == 0)
                {
                    continue;
                }

                List<TradeDetails> newTrades;

                var candle = timeframeCurrentCandles[timeframeToTest][timeframeCurrentCandles[timeframeToTest].Count - 1];

                if (_stopPoint != null && candle.CloseTime() == _stopPoint.DateTime && candle.IsComplete == 1
                    && _stopPoint.Timeframe == timeframeToTest)
                {
                    Debugger.Break();
                }

                newTrades = strategy.CreateNewTrades(timeframeToTest, market, timeframeCurrentCandles, trades, _calculatorService);

                if (newTrades != null && newTrades.Count > 0)
                {
                    trades.AddRange(newTrades);
                    orders.AddRange(newTrades);
                }
            }
        }

        private void UpdateOpenTrades(
            IStrategy strategy, string market, List<TradeDetails> openTrades,
            TimeframeLookup<List<BasicCandleAndIndicators>> timeframeCurrentCandles)
        {
            if (openTrades.Count == 0)
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
                                prevCandle.Value.Open,
                                (float)(m15Candle.High > prevCandle.Value.High ? m15Candle.High : prevCandle.Value.High),
                                (float)(m15Candle.Low < prevCandle.Value.Low ? m15Candle.Low : prevCandle.Value.Low),
                                (float)m15Candle.Close,
                                0,
                                timeframeMaxIndicatorValues[timeframeLookupIndex]
                            );

                            timeframeAllCandlesProcessed[timeframeLookupIndex].Add(incompleteCandle);
                            UpdateIndicators(timeframeIndicators, timeframeLookupIndex, incompleteCandle);

                            /*var timeframeIndicatorItems = timeframeIndicators[timeframeLookupIndex];
                            if (timeframeIndicatorItems != null)
                            {
                                foreach (var timeframeIndicator in timeframeIndicatorItems)
                                {
                                    timeframeIndicator.Item2.RollbackLastValue();
                                }
                            }*/
                        }
                        else
                        {
                            // Add new incomplete candle
                            var incompleteCandle = new BasicCandleAndIndicators(
                                m15Candle.OpenTimeTicks,
                                m15Candle.CloseTimeTicks,
                                (float)m15Candle.Open,
                                (float)m15Candle.High,
                                (float)m15Candle.Low,
                                (float)m15Candle.Close,
                                0,
                                timeframeMaxIndicatorValues[timeframeLookupIndex]
                            );

                            timeframeAllCandlesProcessed[timeframeLookupIndex].Add(incompleteCandle);
                            UpdateIndicators(timeframeIndicators, timeframeLookupIndex, incompleteCandle);

                            /*var timeframeIndicatorItems = timeframeIndicators[timeframeLookupIndex];
                            if (timeframeIndicatorItems != null)
                            {
                                foreach (var timeframeIndicator in timeframeIndicatorItems)
                                {
                                    timeframeIndicator.Item2.RollbackLastValue();
                                }
                            }*/
                        }
                    }
                }
            }

            var intervalForTigerCandles = IntervalForCandles.Minutes30;
            var intervalCount = 0;

            GC.Collect();

            return timeframeAllCandlesProcessed;
        }

        public static TimeframeLookupBasicCandleAndIndicators PopulateCandles(
            IBroker broker,
            string market,
            Timeframe[] timeframesForStrategy,
            IStrategy strategy,
            bool updatePrices,
            bool cacheCandles,
            DateTime? earliest,
            DateTime? latest,
            IBrokersCandlesService candlesService,
            out List<SimpleCandle> m1Candles)
        {
            m1Candles = null;
            var timeframes = timeframesForStrategy.ToList();

            var timeframesExcludingM1D1Tiger = timeframes.Where(t => t != Timeframe.M1).ToArray();
            var timeframesExcludingM1M15 = timeframes.Where(t => t != Timeframe.M1 && t != Timeframe.M15).ToArray();
            var earliestCandle = earliest != null ? (DateTime?)earliest.Value.AddDays(-100) : null;

            var ret = new TimeframeLookupBasicCandleAndIndicators();

            using (_populateCandlesBlockStats.Log())
            {
                // Get M1 candles
                if (timeframes.Contains(Timeframe.M1))
                {
                    bool gotM1Candles;

                    lock (Cache.M1CandlesLookup)
                    {
                        gotM1Candles = Cache.M1CandlesLookup.TryGetValue(market, out m1Candles);
                    }

                    if (!gotM1Candles)
                    {
                        var loadedM1Candles = candlesService.GetCandles(broker, market, Timeframe.M1, updatePrices, cacheData: false,
                            minOpenTimeUtc: earliestCandle, maxCloseTimeUtc: latest).ToList();
                        m1Candles = loadedM1Candles.Select(c => new SimpleCandle(c)).ToList();

                        lock (Cache.M1CandlesLookup)
                        {
                            Cache.M1CandlesLookup[market] = m1Candles;
                        }
                    }

                    if (!gotM1Candles)
                    {
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
                    foreach (var timeframe in timeframesExcludingM1D1Tiger)
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

                    foreach (var timeframe in timeframesExcludingM1D1Tiger)
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