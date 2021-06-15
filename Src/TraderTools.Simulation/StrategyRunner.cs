using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private readonly Timeframe _runTimeframe;
        private readonly decimal _transactionFee;

        public StrategyRunner(
            IBrokersCandlesService candleService,
            IMarketDetailsService marketDetailsService,
            IBroker broker,
            MarketDetails market,
            Timeframe runTimeframe = Timeframe.M1,
            decimal transactionFee = 0.0M)
        {
            _candleService = candleService;
            _marketDetailsService = marketDetailsService;
            _broker = broker;
            _market = market;
            _runTimeframe = runTimeframe;
            _transactionFee = transactionFee;
        }

        public decimal InitialBalance { get; set; } = 10000M;

        /*public static List<Trade> Run(
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
                        d.Data.Market);

                var strategy = StrategyHelper.CreateStrategyInstance(d.Data.StrategyType);

                lock (strategyMarket)
                {
                    strategyMarket[d.Data.Market.Name] = strategy;
                }

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

            /*foreach (var market in StrategyHelper.GetStrategyMarkets(strategyType))
            {
                producerConsumer.Add((strategyType, marketDetailsService.GetMarketDetails(broker.Name, market)));
            }

            producerConsumer.Start();
            producerConsumer.SetProducerCompleted();
            producerConsumer.WaitUntilConsumersFinished();/

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
        }/

        public List<Trade> Run(StrategyBase strategy, Func<bool> getShouldStopFunc, DateTime? startTime = null, DateTime? endTime = null)
        {
            var logIntervalSeconds = 5;
            var candleTimeframes = strategy.Timeframes;
            var smallestNonRunTimeframe = candleTimeframes.First();
            var candleTimeframesExcSmallest = candleTimeframes.Where(x => x != smallestNonRunTimeframe).ToList();
            var runCandles = _candleService.GetCandles(_broker, _market.Name, _runTimeframe, false);
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
            var smallestNonRunTimeframeCount = allCandles[smallestNonRunTimeframe].Count;

            var runCandleIndex = 0;

            Candle smallestNonRunCandle;

            var startTimeTicks = startTime != null ? (long?)startTime.Value.Ticks : null;
            var endTimeTicks = endTime != null ? (long?)endTime.Value.Ticks : null;

            strategy.SetInitialised(() => Balance);

            Balance = InitialBalance;

            // Ignore run timeframe candles
            for (var smallestNonRunTimeframeIndex = 0;
                smallestNonRunTimeframeIndex < smallestNonRunTimeframeCount;
                smallestNonRunTimeframeIndex++)
            {
                if (getShouldStopFunc != null && getShouldStopFunc()) return null;

                // Progress smallest non-run candle
                smallestNonRunCandle = allCandles[smallestNonRunTimeframe][smallestNonRunTimeframeIndex];
                currentCandles[smallestNonRunTimeframe].Add(smallestNonRunCandle);
                var newCandleTimeframes = new List<Timeframe> { smallestNonRunTimeframe };

                if (DateTime.UtcNow > nextLogTime)
                {
                    LogProgress(trades, smallestNonRunCandle.CloseTimeTicks);
                    nextLogTime = DateTime.UtcNow.AddSeconds(logIntervalSeconds);
                }

                // Progress other timeframes
                foreach (var tf in candleTimeframesExcSmallest)
                {
                    for (var i = timeframeCandleIndexes[tf]; i < allCandles[tf].Count; i++)
                    {
                        var c = allCandles[tf][i];
                        if (c.CloseTimeTicks <= smallestNonRunCandle.CloseTimeTicks)
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

                // Process candles if any trades are open or and orders
                if ((trades.AnyOpen || trades.AnyOrders) && runCandleIndex < runCandles.Count)
                {
                    runCandleIndex = TryFillOrdersAndTryCloseOpenTrades(
                        runCandles, runCandleIndex, smallestNonRunTimeframeIndex, allCandles, smallestNonRunTimeframe, smallestNonRunCandle, trades, ref calls);
                }

                // Process new completed candles in strategy
                try
                {
                    strategy.UpdateIndicators(newCandleTimeframes);
                    strategy.TradesToProcess.Clear();

                    if (startTimeTicks == null || (smallestNonRunCandle.CloseTimeTicks >= startTimeTicks && smallestNonRunCandle.CloseTimeTicks <= endTimeTicks))
                    {
                        strategy.ProcessCandles(newCandleTimeframes);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Error processing new candles", ex);
                    return null;
                }

                RemoveInvalidTrades(strategy.TradesToProcess, smallestNonRunCandle.CloseBid, smallestNonRunCandle.CloseAsk);

                // Process trades
                foreach (var t in strategy.TradesToProcess)
                {
                    if (t.CloseDateTime != null)
                    {
                        trades.AddClosedTrade(t);
                        UpdateCloseTradeProfit(t);
                    }
                    else if (t.EntryDateTime == null && t.OrderDateTime != null)
                    {
                        trades.AddOrderTrade(t);
                    }
                    else if (t.EntryDateTime != null)
                    {
                        trades.AddOpenTrade(t);
                        UpdateNewOpenTradeAmount(t);
                    }
                }
            }

            LogProgress(trades, allCandles[smallestNonRunTimeframe][allCandles[smallestNonRunTimeframe].Count - 1].CloseTimeTicks);

            var ret = trades.AllTrades.Select(x => x.Trade).ToList();

            // Set trade profits
            /*var balance = InitialBalance;
            foreach (var t in ret.OrderBy(z => z.OrderDateTime ?? z.EntryDateTime))
            {
                var fees = 0M;
                var riskAmount = (strategy.RiskEquityPercent / 100M) * balance;

                if (_transactionFee >= 0.0M)
                {
                    riskAmount = riskAmount / (1.0M + _transactionFee);
                    fees += riskAmount * _transactionFee;
                }

                if (t.EntryQuantity == 0M)
                {
                    t.EntryQuantity = riskAmount / t.EntryPrice.Value;
                }

                var profit = 0M;

                if (t.RMultiple != null)
                {
                    profit = t.RMultiple.Value * riskAmount;
                }
                else if (t.EntryPrice != null && t.ClosePrice != null)
                {
                    profit = (t.ClosePrice.Value / t.EntryPrice.Value) * riskAmount - riskAmount;
                }

                if (_transactionFee >= 0.0M)
                {
                    var amount = riskAmount + profit;
                    fees += amount * _transactionFee;
                }

                t.NetProfitLoss = profit - fees;
                t.RiskAmount = riskAmount;
                balance += profit - fees;

                if (balance < 0) balance = 0M;
            }*/

            foreach (var t in ret)
            {
                TradeCalculator.UpdateInitialStopPips(t);
                TradeCalculator.UpdateInitialLimitPips(t);
                TradeCalculator.UpdateRMultiple(t);
            }

            return ret;
        }

        private void UpdateNewOpenTradeAmount(Trade t)
        {
            var amount = t.EntryQuantity;
            var cost = (t.EntryQuantity.Value * t.EntryPrice.Value) + (_transactionFee * (t.EntryQuantity.Value * t.EntryPrice.Value));
            var fee = _transactionFee * (t.EntryQuantity * t.EntryPrice);

            if (cost > Balance)
            {
                cost = Balance;
                fee = cost - (cost / (1M + _transactionFee));
                amount = (cost - fee) / t.EntryPrice.Value;
            }

            t.RiskAmount = cost;
            t.RiskPercentOfBalance = t.RiskAmount != 0 ? Balance / t.RiskAmount : 0;
            t.EntryQuantity = amount;

            Balance -= cost;
        }

        private void UpdateCloseTradeProfit(Trade t)
        {
            var sellReturn = t.EntryQuantity.Value * t.ClosePrice.Value;
            var fee = sellReturn * _transactionFee;
            var profit = sellReturn - t.RiskAmount.Value - fee;

            t.NetProfitLoss = profit;
            Balance += sellReturn - fee;
        }

        public decimal Balance { get; private set; }

        private int TryFillOrdersAndTryCloseOpenTrades(List<Candle> runCandles, int runCandleIndex,
            int smallestNonRunTimeframeIndex, TimeframeLookup<List<Candle>> allCandles, Timeframe smallestNonRunTimeframe,
            Candle smallestNonRunCandle, TradeWithIndexingCollection trades, ref int calls)
        {
            // Include run candles
            var nextIndex = runCandles.BinarySearchGetItem(
                i => runCandles[i].CloseTimeTicks,
                runCandleIndex,
                smallestNonRunTimeframeIndex > 0
                    ? allCandles[smallestNonRunTimeframe][smallestNonRunTimeframeIndex - 1].CloseTimeTicks
                    : smallestNonRunCandle.OpenTimeTicks,
                BinarySearchMethod.NextHigherValue);

            if (nextIndex != -1 && runCandles[nextIndex].CloseTimeTicks <= runCandles[runCandleIndex].CloseTimeTicks)
            {
                throw new ApplicationException("Run candles are not running in order");
            }

            if (nextIndex != -1 && runCandles[nextIndex].CloseTimeTicks <= smallestNonRunCandle.CloseTimeTicks)
            {
                runCandleIndex = nextIndex;

                for (var i = runCandleIndex; i < runCandles.Count; i++)
                {
                    var runCandle = runCandles[i];
                    if (runCandle.CloseTimeTicks > smallestNonRunCandle.CloseTimeTicks) break;

                    runCandleIndex = i;
                    if (!trades.AnyOpen && !trades.AnyOrders) break;

                    if (trades.AnyOrders && Balance >= 0M)
                    {
                        FillOrders(trades, runCandle);
                    }

                    if (trades.AnyOpen)
                    {
                        TryCloseOpenTrades(trades, runCandle);
                        calls++;
                    }
                }
            }

            return runCandleIndex;
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


        private void TryCloseOpenTrades(TradeWithIndexingCollection trades, Candle runCandle)
        {
            if (!trades.AnyOpen) return;

            foreach (var t in trades.OpenTrades)
            {
                if (t.Trade.EntryDateTime != null && runCandle.OpenTimeTicks >= t.Trade.EntryDateTime.Value.Ticks)
                {
                    t.Trade.SimulateTrade(runCandle, out _);
                }

                if (t.Trade.CloseDateTime != null)
                {
                    trades.MoveOpenToClose(t);
                    UpdateCloseTradeProfit(t.Trade);
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
                $"{_market.Name} Up to: {new DateTime(currentDateTimeTicks): dd-MM-yy HH:mm} "// /* {r.PercentComplete:0.00}% /. Running: {r.SecondsRunning}s. "
                + $"Created: {orderTrades.Count() + closedTrades.Count + trades.OpenTrades.Count()} "
                + $"Open: {trades.OpenTrades.Count()}. Orders: {orderTrades.Count()} "
                + $"Closed: {closedTrades.Count} (Hit stop: {closedTrades.Count(x => x.CloseReason == TradeCloseReason.HitStop)} "
                + $"Hit limit: {closedTrades.Count(x => x.CloseReason == TradeCloseReason.HitLimit)} "
                + $"Hit expiry: {closedTrades.Count(x => x.CloseReason == TradeCloseReason.HitExpiry)} "
                + $"Cached trades: {trades.CachedTradesCount}) "
                + $"Expectancy: {expectancy:0.00}");
        }

        private void FillOrders(TradeWithIndexingCollection trades, Candle runCandle)
        {
            foreach (var order in trades.OrderTradesAsc)
            {
                var candleCloseTimeTicks = runCandle.CloseTimeTicks;

                if (order.Trade.OrderDateTime != null && candleCloseTimeTicks < order.Trade.OrderDateTime.Value.Ticks)
                {
                    break;
                }

                order.Trade.SimulateTrade(runCandle, out _);

                if (order.Trade.EntryDateTime != null)
                {
                    trades.MoveOrderToOpen(order);
                    UpdateNewOpenTradeAmount(order.Trade);
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