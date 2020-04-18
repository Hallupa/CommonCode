using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using log4net;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;

namespace TraderTools.Simulation
{
    [Flags]
    public enum NewCandleFlags
    {
        M1Candle = 1,
        CompleteNonM1Candle = 2,
        IncompleteNonM1Candle = 4
    }

    public class TimeframeLookupBasicCandleAndIndicators : TimeframeLookup<List<CandleAndIndicators>>
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private const int LogIntervalSeconds = 5;
        public static CachedDetails Cache { get; set; } = new CachedDetails();

        public static List<Candle> GetM1Candles(IBroker broker, string market, IBrokersCandlesService candlesService,
            bool cacheCandles = false, bool updatePrices = false, DateTime? earliest = null, DateTime? latest = null)
        {
            var earliestCandle = earliest?.AddDays(-100);
            List<Candle> m1Candles = null;
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

                if (cacheCandles)
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

            return m1Candles;
        }

        public static TimeframeLookupBasicCandleAndIndicators PopulateCandles(
            IBroker broker,
            string market,
            Timeframe[] timeframesForStrategy,
            TimeframeLookup<Indicator[]> timeframeIndicatorsRequired,
            IBrokersCandlesService candlesService,
            bool updatePrices = false,
            bool cacheCandles = false,
            DateTime? earliest = null,
            DateTime? latest = null)
        {
            var timeframes = timeframesForStrategy.ToList();
            var earliestCandle = earliest != null ? (DateTime?)earliest.Value.AddDays(-100) : null;

            var ret = new TimeframeLookupBasicCandleAndIndicators();


            // Get existing candles
            var missingCandles = false;
            foreach (var timeframe in timeframes)
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

            // If there is any missing candle time-frames, then recalculate all candles
            if (missingCandles)
            {
                var candlesLookup = new TimeframeLookup<List<Candle>>();
                foreach (var timeframe in timeframes)
                {
                    var candles = candlesService.GetCandles(broker, market, timeframe, updatePrices, cacheData: false,
                        minOpenTimeUtc: earliestCandle, maxCloseTimeUtc: latest);
                    candlesLookup[timeframe] = candles;
                }

                ret = SetupCandlesWithIndicators(candlesLookup, timeframes.ToArray(), timeframeIndicatorsRequired);

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

                foreach (var timeframe in timeframes)
                {
                    candlesService.UnloadCandles(market, timeframe, broker);
                }

                GC.Collect();
            }

            return ret;
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

            var smallestTimeframe = Timeframe.M1;
            foreach (var timeframe in new[] { Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.H2 })
            {
                if (timeframeAllCandles[timeframe] != null && timeframeAllCandles[timeframe].Count > 0)
                {
                    smallestTimeframe = timeframe;
                    break;
                }
            }

            var smallestCandles = timeframeAllCandles[smallestTimeframe];
            var timeframeAllCandlesProcessed = new TimeframeLookupBasicCandleAndIndicators();
            var timeframeCandleIndexes = new TimeframeLookup<int>();
            var smallestTimeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(smallestTimeframe);

            for (var i = 0; i < smallestCandles.Count; i++)
            {
                var smallestCandle = timeframeAllCandles[smallestTimeframeLookupIndex][i];

                // See if any other candles have completed
                foreach (var timeframe in timeframes)
                {
                    if (i == 0)
                    {
                        timeframeAllCandlesProcessed[timeframe] = new List<CandleAndIndicators>();
                    }

                    // Try to added completed candle if M15
                    var completedCandleAdded = false;
                    var timeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);

                    if (timeframe == Timeframe.M15)
                    {
                        var candleWithIndicators = new CandleAndIndicators(smallestCandle, timeframeMaxIndicatorValues[timeframeLookupIndex]);
                        UpdateIndicators(timeframeIndicators, timeframeLookupIndex, candleWithIndicators);
                        timeframeAllCandlesProcessed[timeframeLookupIndex].Add(candleWithIndicators);
                        continue;
                    }

                    for (var ii = timeframeCandleIndexes[timeframeLookupIndex]; ii < timeframeAllCandles[timeframeLookupIndex].Count; ii++)
                    {
                        var timeframeCandle = timeframeAllCandles[timeframeLookupIndex][ii];

                        // Look for completed candle
                        if (timeframeCandle.CloseTimeTicks <= smallestCandle.CloseTimeTicks)
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
                                smallestCandle.CloseTimeTicks,
                                prevCandle.Value.Candle.OpenBid,
                                smallestCandle.HighBid > prevCandle.Value.Candle.HighBid ? smallestCandle.HighBid : prevCandle.Value.Candle.HighBid,
                                smallestCandle.LowBid < prevCandle.Value.Candle.LowBid ? smallestCandle.LowBid : prevCandle.Value.Candle.LowBid,
                                smallestCandle.CloseBid,
                                prevCandle.Value.Candle.OpenAsk,
                                smallestCandle.HighAsk > prevCandle.Value.Candle.HighAsk ? smallestCandle.HighAsk : prevCandle.Value.Candle.HighAsk,
                                smallestCandle.LowAsk < prevCandle.Value.Candle.LowAsk ? smallestCandle.LowAsk : prevCandle.Value.Candle.LowAsk,
                                smallestCandle.CloseAsk,
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
                                smallestCandle.OpenTimeTicks,
                                smallestCandle.CloseTimeTicks,
                                smallestCandle.OpenBid,
                                smallestCandle.HighBid,
                                smallestCandle.LowBid,
                                smallestCandle.CloseBid,
                                smallestCandle.OpenAsk,
                                smallestCandle.HighAsk,
                                smallestCandle.LowAsk,
                                smallestCandle.CloseAsk,
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

        public static void IterateThroughCandles(
            TimeframeLookupBasicCandleAndIndicators timeframesAllCandles,
            List<Candle> m1Candles,
            Action<(TimeframeLookup<List<CandleAndIndicators>> CurrentCandles, Candle M1Candle, NewCandleFlags NewCandleFlags)> processNewCandleAction,
            Func<(DateTime LatestCandleDateTime, int SecondsRunning, double PercentComplete), string> getLogFunc,
            Func<bool> getShouldStopFunc)
        {
            var timeframes = timeframesAllCandles.GetSetTimeframes();
            var timeframeCandleIndexes = new TimeframeLookup<int>();
            var timeframesCurrentCandles = new TimeframeLookup<List<CandleAndIndicators>>();
            var startTimeUtc = DateTime.UtcNow;

            foreach (var timeframe in timeframes)
            {
                var timeframeLookupIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);

                if (timeframeCandleIndexes[timeframeLookupIndex] == 0)
                {
                    timeframesCurrentCandles[timeframeLookupIndex] = new List<CandleAndIndicators>();
                }
            }

            var nextLogTime = DateTime.UtcNow.AddSeconds(LogIntervalSeconds);

            // Move through all M1 candles
            for (var i = 0; i < m1Candles.Count; i++)
            {
                var m1Candle = m1Candles[i];

                if (getShouldStopFunc != null && getShouldStopFunc()) break;

                // Move candles forward
                var timeframeCandleUpdated = false;
                var timeframeCompleteCandleUpdated = false;
                var complete = false;
                foreach (var timeframe in timeframes)
                {
                    var timeframeIndex = TimeframeLookup<int>.GetLookupIndex(timeframe);
                    var currentTimeframeCandleIndex = timeframeCandleIndexes[timeframe];
                    var currentCandles = timeframesCurrentCandles[timeframeIndex];
                    var allCandles = timeframesAllCandles[timeframeIndex];
                    var nextTimeframeCandleIndex = currentTimeframeCandleIndex + 1;

                    if (nextTimeframeCandleIndex >= allCandles.Count)
                    {
                        complete = true;
                        break;

                    }

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
                        timeframeCandleUpdated = true;
                        if (newCandle.Candle.IsComplete == 1) timeframeCompleteCandleUpdated = true;
                    }
                }
                
                if (complete)
                {
                    break;
                }

                var newCandlesFlags = NewCandleFlags.M1Candle;
                if (timeframeCompleteCandleUpdated)
                {
                    newCandlesFlags = newCandlesFlags | NewCandleFlags.CompleteNonM1Candle;
                }
                else if (timeframeCandleUpdated)
                {
                    newCandlesFlags = newCandlesFlags | NewCandleFlags.IncompleteNonM1Candle;
                }

                processNewCandleAction((timeframesCurrentCandles, m1Candle, newCandlesFlags));

                if (DateTime.UtcNow > nextLogTime || i == m1Candles.Count - 1)
                {
                    var percent = (i * 100.0) / m1Candles.Count;
                    Log.Info(getLogFunc((m1Candle.CloseTime(), (int)(DateTime.UtcNow - startTimeUtc).TotalSeconds, percent)));
                    nextLogTime = DateTime.UtcNow.AddSeconds(LogIntervalSeconds);
                }
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
    }
}