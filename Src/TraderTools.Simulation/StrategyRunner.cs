using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Hallupa.Library;
using Hallupa.Library.Extensions;
using log4net;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;

namespace TraderTools.Simulation
{
    public class StrategyRunner
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly IBrokersCandlesService _candleService;
        private readonly IMarketDetailsService _marketDetailsService;
        private readonly IBroker _broker;
        private readonly MarketDetails _market;

        public StrategyRunner(IBrokersCandlesService candleService, IMarketDetailsService marketDetailsService, IBroker broker, MarketDetails market)
        {
            _candleService = candleService;
            _marketDetailsService = marketDetailsService;
            _broker = broker;
            _market = market;
        }

        public static List<Trade> Run(
            Type strategyType, Func<bool> stopFunc,
            IBrokersCandlesService candlesService, IMarketDetailsService marketDetailsService,
            IBroker broker, int threads)
        {
            var stopwatch = Stopwatch.StartNew();
            var strategyMarket = new Dictionary<string, StrategyBase>();
            var completed = 0;
            var trades = new List<Trade>();

            var producerConsumer = new ProducerConsumer<(Type StrategyType, MarketDetails Market)>(
                threads, d =>
            {
                if (stopFunc?.Invoke() ?? false) return ProducerConsumerActionResult.Stop;

                var strategyTester =
                    new StrategyRunner(candlesService, marketDetailsService, broker,
                        d.Market);

                var strategy = StrategyHelper.CreateStrategyInstance(d.StrategyType);
                strategyMarket[d.Market.Name] = strategy;

                var marketTrades = strategyTester.Run(strategy, stopFunc, strategy.StartTime, strategy.EndTime);

                if (marketTrades != null)
                {
                    lock (trades)
                    {
                        trades.AddRange(marketTrades);
                    }
                    
                    // _results.AddResult(result);

                    // Adding trades to UI in realtime slows down the UI too much with strategies with many trades

                    completed++;
                    Log.Info($"Completed {completed}/{strategy.Markets.Length}");
                }

                return ProducerConsumerActionResult.Success;
            });

            foreach (var market in StrategyHelper.GetStrategyMarkets(strategyType))
            {
                producerConsumer.Add((strategyType, marketDetailsService.GetMarketDetails(broker.Name, market)));
            }

            producerConsumer.Start();
            producerConsumer.SetProducerCompleted();
            producerConsumer.WaitUntilConsumersFinished();

            //var trades = _results.Results.ToList();

            // Set trade profits
            var balance = 10000M;
            foreach (var t in trades.OrderBy(z => z.OrderDateTime ?? z.EntryDateTime))
            {
                var riskAmount = (strategyMarket[t.Market].RiskEquityPercent / 100M) * balance;
                var profit = t.RMultiple * riskAmount ?? 0M;
                t.NetProfitLoss = profit;
                t.RiskAmount = riskAmount;
                balance += profit;

                if (balance < 0) balance = 0M;
            }

            stopwatch.Stop();
            Log.Info($"Simulation run completed in {stopwatch.Elapsed.TotalSeconds}s");

            return trades;
        }

        public List<Trade> Run(StrategyBase strategy, Func<bool> getShouldStopFunc, DateTime? startTime = null, DateTime? endTime = null)
        {
            var logIntervalSeconds = 5;
            var candleTimeframes = strategy.Timeframes;
            var smallestNonM1Timeframe = candleTimeframes.First();
            var candleTimeframesExcSmallest = candleTimeframes.Where(x => x != smallestNonM1Timeframe).ToList();
            var m1Candles = _candleService.GetCandles(_broker, _market.Name, Timeframe.M1, false);
            var allCandles = GetCandles(candleTimeframes);
            var timeframeCandleIndexes = new TimeframeLookup<int>();
            var currentCandles = new TimeframeLookup<List<Candle>>();
            var trades = new TradeWithIndexingCollection();
            var nextLogTime = DateTime.UtcNow.AddSeconds(logIntervalSeconds);
            var calls = 0;
            
            strategy.SetSimulationParameters(trades, currentCandles, _market);

            foreach (var tf in candleTimeframes)
            {
                currentCandles[tf] = new List<Candle>(10000);
            }
            var smallestNonM1TimeframeCount = allCandles[smallestNonM1Timeframe].Count;

            var m1CandleIndex = 0;

            Candle smallestNonM1Candle;

            var startTimeTicks = startTime != null ? (long?)startTime.Value.Ticks : null;
            var endTimeTicks = endTime != null ? (long?)endTime.Value.Ticks : null;

            strategy.SetInitialised();

            // Ignore M1 candles
            for (var smallestNonM1TfIndex = 0;
                smallestNonM1TfIndex < smallestNonM1TimeframeCount;
                smallestNonM1TfIndex++)
            {
                if (getShouldStopFunc != null && getShouldStopFunc()) return null;

                // Progress smallest non-M1 candle
                smallestNonM1Candle = allCandles[smallestNonM1Timeframe][smallestNonM1TfIndex];
                currentCandles[smallestNonM1Timeframe].Add(smallestNonM1Candle);
                var newCandleTimeframes = new List<Timeframe> { smallestNonM1Timeframe };

                if (DateTime.UtcNow > nextLogTime)
                {
                    LogProgress(trades, smallestNonM1Candle.CloseTimeTicks);
                    nextLogTime = DateTime.UtcNow.AddSeconds(logIntervalSeconds);
                }

                // Progress other timeframes
                foreach (var tf in candleTimeframesExcSmallest)
                {
                    for (var i = timeframeCandleIndexes[tf]; i < allCandles[tf].Count; i++)
                    {
                        var c = allCandles[tf][i];
                        if (c.CloseTimeTicks <= smallestNonM1Candle.CloseTimeTicks)
                        {
                            currentCandles[tf].Add(c);
                            newCandleTimeframes.Add(tf);
                            timeframeCandleIndexes[tf] = i + 1;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // Process M1 candles if any trades are open or and orders
                if ((trades.AnyOpen || trades.AnyOrders) && m1CandleIndex < m1Candles.Count)
                {
                    // Include M1 candles
                    var nextIndex = m1Candles.BinarySearchGetItem(
                        i => m1Candles[i].CloseTimeTicks,
                        m1CandleIndex,
                        smallestNonM1TfIndex > 0 ? allCandles[smallestNonM1Timeframe][smallestNonM1TfIndex - 1].CloseTimeTicks : smallestNonM1Candle.OpenTimeTicks,
                        BinarySearchMethod.NextHigherValue);

                    if (nextIndex != -1 &&  m1Candles[nextIndex].CloseTimeTicks <= m1Candles[m1CandleIndex].CloseTimeTicks)
                    {
                        throw new ApplicationException("M1 candles are not running in order");
                    }

                    if (nextIndex != -1 && m1Candles[nextIndex].CloseTimeTicks <= smallestNonM1Candle.CloseTimeTicks)
                    {
                        m1CandleIndex = nextIndex;

                        for (var i = m1CandleIndex; i < m1Candles.Count; i++)
                        {
                            var m1 = m1Candles[i];
                            if (m1.CloseTimeTicks > smallestNonM1Candle.CloseTimeTicks) break;

                            m1CandleIndex = i;
                            if (!trades.AnyOpen && !trades.AnyOrders) break;

                            if (trades.AnyOrders) FillOrders(trades, m1);
                            if (trades.AnyOpen)
                            {
                                TryCloseOpenTrades(trades, m1);
                                calls++;
                            }
                        }
                    }
                }

                // Process new completed candles in strategy
                try
                {
                    strategy.UpdateIndicators(newCandleTimeframes);
                    strategy.NewTrades.Clear();

                    if (startTimeTicks == null || (smallestNonM1Candle.CloseTimeTicks >= startTimeTicks && smallestNonM1Candle.CloseTimeTicks <= endTimeTicks))
                    {
                        strategy.ProcessCandles(newCandleTimeframes);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Error processing new candles", ex);
                    return null;
                }

                RemoveInvalidTrades(strategy.NewTrades, smallestNonM1Candle.CloseBid, smallestNonM1Candle.CloseAsk);

                // Add new trades
                foreach (var t in strategy.NewTrades)
                {
                    if (t.CloseDateTime != null)
                    {
                        trades.AddClosedTrade(t);
                    }
                    else if (t.EntryDateTime == null && t.OrderDateTime != null)
                    {
                        trades.AddOrderTrade(t);
                    }
                    else if (t.EntryDateTime != null)
                    {
                        trades.AddOpenTrade(t);
                    }
                }
            }

            LogProgress(trades, allCandles[smallestNonM1Timeframe][allCandles[smallestNonM1Timeframe].Count - 1].CloseTimeTicks);

            var ret = trades.AllTrades.Select(x => x.Trade).ToList();

            foreach (var t in ret)
            {
                TradeCalculator.UpdateInitialStopPips(t);
                TradeCalculator.UpdateInitialLimitPips(t);
                TradeCalculator.UpdateRMultiple(t);
            }

            return ret;
        }

        private void RemoveInvalidTrades(List<Trade> newTrades, float latestBidPrice, float latestAskPrice)
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
                            Log.Error(
                                $"Long trade for {t.Market} has limit price below order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.LimitPrice > t.OrderPrice.Value)
                        {
                            Log.Error(
                                $"Short trade for {t.Market} has limit price above order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (t.StopPrices != null && !removed)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.StopPrice > t.OrderPrice.Value)
                        {
                            Log.Error(
                                $"Long trade for {t.Market} has stop price above order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.StopPrice < t.OrderPrice.Value)
                        {
                            Log.Error(
                                $"Short trade for {t.Market} has stop price below order price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (!removed)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.OrderType == OrderType.LimitEntry &&
                            t.OrderPrice.Value > (decimal)latestAskPrice)
                        {
                            Log.Error($"Long trade for {t.Market} has limit entry but order price is above latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Long && t.OrderType == OrderType.StopEntry &&
                                 t.OrderPrice.Value < (decimal)latestAskPrice)
                        {
                            Log.Error($"Long trade for {t.Market} has stop entry but order price is below latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.OrderType == OrderType.LimitEntry &&
                                 t.OrderPrice.Value < (decimal)latestBidPrice)
                        {
                            Log.Error($"Short trade for {t.Market} has limit entry but order price is below latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.OrderType == OrderType.StopEntry &&
                                 t.OrderPrice.Value > (decimal)latestBidPrice)
                        {
                            Log.Error($"Short trade for {t.Market} has stop entry but order price is above latest price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    /*var maxPips = 4;
                    if (!removed && t.StopPrice != null)
                    {
                        var stopPips = PipsHelper.GetPriceInPips(Math.Abs(t.StopPrice.Value - t.OrderPrice.Value),
                            marketDetailsService.GetMarketDetails("FXCM", t.Market));
                        if (stopPips <= maxPips)
                        {
                            Log.Error($"Trade for {t.Market} has stop within {maxPips} pips. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (!removed && t.LimitPrice != null)
                    {
                        var limitPips = PipsHelper.GetPriceInPips(Math.Abs(t.LimitPrice.Value - t.OrderPrice.Value),
                            marketDetailsService.GetMarketDetails("FXCM", t.Market));
                        if (limitPips <= maxPips)
                        {
                            Log.Error($"Trade for {t.Market} has stop within {maxPips} pips. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }*/
                }
                else
                {
                    // Market trade
                    // decimal latestBidPrice, decimal latestAskPrice,
                    if (t.LimitPrice != null)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.LimitPrice < t.EntryPrice)
                        {
                            Log.Error(
                                $"Long trade for {t.Market} has limit price below current price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.LimitPrice > t.EntryPrice)
                        {
                            Log.Error(
                                $"Short trade for {t.Market} has limit price above current price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (t.StopPrices != null && !removed)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.StopPrice > t.EntryPrice)
                        {
                            Log.Error(
                                $"Long trade for {t.Market} has stop price above current price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.StopPrice < t.EntryPrice)
                        {
                            Log.Error(
                                $"Short trade for {t.Market} has stop price below current price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }
                }
            }
        }


        private static void TryCloseOpenTrades(TradeWithIndexingCollection trades, Candle m1Candle)
        {
            if (!trades.AnyOpen) return;

            foreach (var trade in trades.OpenTrades)
            {
                if (trade.Trade.EntryDateTime != null && m1Candle.OpenTimeTicks >= trade.Trade.EntryDateTime.Value.Ticks)
                {
                    trade.Trade.SimulateTrade(m1Candle, out _);
                }

                if (trade.Trade.CloseDateTime != null)
                {
                    trades.MoveOpenToClose(trade);
                }
            }
        }

        private void LogProgress(TradeWithIndexingCollection trades, long currentDateTimeTicks)
        {
            var closedTrades = new List<Trade>(5000);
            closedTrades.AddRange(trades.ClosedTrades.Select(x => x.Trade));

            var orderTrades = new List<Trade>(5000);
            orderTrades.AddRange(trades.OrderTradesAsc.Select(x => x.Trade));

            var tradesForExpectancy = closedTrades.Where(x => x.RMultiple != null).ToList();
            var expectancy = TradingCalculator.CalculateExpectancy(tradesForExpectancy);

            Log.Info(
                $"{_market.Name} Up to: {new DateTime(currentDateTimeTicks): dd-MM-yy HH:mm} "// /* {r.PercentComplete:0.00}% */. Running: {r.SecondsRunning}s. "
                + $"Created: {orderTrades.Count() + closedTrades.Count + trades.OpenTrades.Count()} "
                + $"Open: {trades.OpenTrades.Count()}. Orders: {orderTrades.Count()} "
                + $"Closed: {closedTrades.Count} (Hit stop: {closedTrades.Count(x => x.CloseReason == TradeCloseReason.HitStop)} "
                + $"Hit limit: {closedTrades.Count(x => x.CloseReason == TradeCloseReason.HitLimit)} "
                + $"Hit expiry: {closedTrades.Count(x => x.CloseReason == TradeCloseReason.HitExpiry)} "
                + $"Cached trades: {trades.CachedTradesCount}) "
                + $"Expectancy: {expectancy:0.00}");
        }

        private static void FillOrders(TradeWithIndexingCollection trades, Candle m1Candle)
        {
            foreach (var order in trades.OrderTradesAsc)
            {
                var candleCloseTimeTicks = m1Candle.CloseTimeTicks;

                if (order.Trade.OrderDateTime != null && candleCloseTimeTicks < order.Trade.OrderDateTime.Value.Ticks)
                {
                    break;
                }

                order.Trade.SimulateTrade(m1Candle, out _);


                if (order.Trade.EntryDateTime != null)
                {
                    trades.MoveOrderToOpen(order);
                }
                else if (order.Trade.CloseDateTime != null)
                {
                    trades.MoveOrderToClosed(order);
                }
            }
        }

        private TimeframeLookup<List<Candle>> GetCandles(IEnumerable<Timeframe> timeframes)
        {
            var ret = new TimeframeLookup<List<Candle>>();

            foreach (var tf in timeframes)
            {
                var candles = _candleService.GetCandles(_broker, _market.Name, tf, false);
                ret[tf] = candles;
            }

            return ret;
        }
    }
}