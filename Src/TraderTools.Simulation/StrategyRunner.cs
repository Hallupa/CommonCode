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
        private readonly Timeframe _runTimeframe;
        private readonly decimal _transactionFee;
        private const int LogIntervalSeconds = 5;

        public StrategyRunner(
            IBrokersCandlesService candleService,
            IMarketDetailsService marketDetailsService,
            IBroker broker,
            decimal initialBalance,
            Timeframe runTimeframe = Timeframe.M1,
            decimal transactionFee = 0.0M)
        {
            _candleService = candleService;
            _marketDetailsService = marketDetailsService;
            _broker = broker;
            _runTimeframe = runTimeframe;
            _transactionFee = transactionFee;
            InitialBalance = initialBalance;
        }

        public decimal InitialBalance { get; }
        public decimal Balance { get; private set; }
        public decimal CurrentValue { get; private set; }

        public List<Trade> Run(StrategyBase strategy, Func<bool> getShouldStopFunc = null)
        {
            if (strategy.Markets == null || strategy.Markets.Length == 0) throw new ArgumentException("Strategy must set markets");
            if (strategy.Timeframes == null || strategy.Timeframes.Length == 0) throw new ArgumentException("Timesframes must set markets");

            var runCandles = GetRunCandles(strategy, _runTimeframe);
            var currentCandles = CreateCurrentCandles(strategy.Markets, strategy.Timeframes);
            var smallestNonRunTimeframe = strategy.Timeframes.First();
            var strategyCandles = GetTfOrderedStrategyCandles(strategy);
            var trades = new TradeWithIndexingCollection();
            Balance = InitialBalance;
            CurrentValue = Balance;
            var nextLogTime = DateTime.UtcNow.AddSeconds(LogIntervalSeconds);

            strategy.SetSimulationParameters(trades, currentCandles);
            strategy.SetInitialised(
                () => Balance,
                () => CurrentValue,
                (indexing, trade, candle) => ProcessTradeUpdatedOrAddedByStrategy(indexing, trade, trades, candle));

            var currentPrices = new Dictionary<string, float>();

            while (true)
            {
                if (getShouldStopFunc != null && getShouldStopFunc()) return null;

                // Progress strategy candles
                var addedCandles = ProgressStrategyCandles(
                    strategyCandles,
                    currentCandles,
                    currentPrices,
                    out var currentTime,
                    out var nextTime);

                foreach (var t in addedCandles.Select(a => a.Timeframe))
                {
                    long date = 0;
                    foreach (var m in addedCandles.Select(a => a.Market).Distinct())
                    {
                        if (date == 0)
                        {
                            date = currentCandles[m][t][currentCandles[m][t].Count - 1].OpenTimeTicks;
                        }
                        else if (Math.Abs(date - currentCandles[m][t][currentCandles[m][t].Count - 1].OpenTimeTicks) > 200000)
                        {

                        }
                    }
                }

                if (addedCandles.Count == 0) break;

                // Log progress
                if (DateTime.UtcNow > nextLogTime)
                {
                    LogProgress(trades, currentTime);
                    nextLogTime = DateTime.UtcNow.AddSeconds(LogIntervalSeconds);
                }

                // Update value
                CurrentValue = Balance;
                foreach (var t in trades.OpenTrades)
                {
                    var v = (decimal)currentPrices[t.Trade.Market] * t.Trade.EntryQuantity.Value;
                    CurrentValue += v - (v * _transactionFee);
                }

                // Process strategy
                strategy.UpdateIndicators(addedCandles);
                strategy.ProcessCandles(addedCandles);

                /*foreach (var market in strategy.Markets)
                {
                    var smallestNonRunCandle = currentCandles[market][smallestNonRunTimeframe][currentCandles[market][smallestNonRunTimeframe].Count - 1];
                    RemoveInvalidTrades(market, strategy.TradesToProcess, smallestNonRunCandle.CloseBid, smallestNonRunCandle.CloseAsk);
                }*/

                if (trades.AnyOpenWithStopOrLimit || trades.AnyOrders)
                {
                    ProgressRunCandles(runCandles, currentTime, nextTime, trades);
                }
            }


            var ret = trades.AllTrades.Select(x => x.Trade).ToList();

            CompleteTradeDetails(ret);

            LogProgress(trades);

            return ret;
        }

        private void RemoveInvalidTrades(string market, List<Trade> newTrades, float latestBidPrice, float latestAskPrice)
        {
            // Validate trades
            for (var i = newTrades.Count - 1; i >= 0; i--)
            {
                var t = newTrades[i];
                if (t.Market != market) continue;

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

        private void ProcessTradeUpdatedOrAddedByStrategy(
            TradeWithIndexing tradeWithIndexing,
            Trade trade,
            TradeWithIndexingCollection trades,
            Candle candle)
        {
            if (tradeWithIndexing == null)
            {
                // New trade
                if (trade.EntryPrice != null)
                {
                    UpdateNewOpenTradeAmount(trade, candle);
                    trades.AddOpenTrade(trade);
                }
                else if (trade.OrderPrice != null)
                {
                    trades.AddOrderTrade(trade);
                }
            }
            else
            {
                if (trade.ClosePrice != null && trade.EntryPrice != null)
                {
                    // Closed trade
                    UpdateBalanceAndClosedTradeProfit(trade);
                    trades.MoveOpenToClose(tradeWithIndexing);
                }
                else if (trade.OrderPrice != null && trade.EntryPrice == null && trade.ClosePrice != null)
                {
                    // Order
                    trades.MoveOrderToClosed(tradeWithIndexing);
                }
            }
        }


        private void ProgressRunCandles(
            List<CandlesWithIndex> runCandles,
            long currentTime,
            long nextTime,
            TradeWithIndexingCollection trades)
        {
            foreach (var c in runCandles)
            {
                var startIndex = c.Candles.BinarySearchGetItem(
                    i => c.Candles[i].CloseTimeTicks,
                    c.NextIndex,
                    currentTime,
                    BinarySearchMethod.NextHigherValue);

                if (startIndex == -1) continue;

                for (var i = startIndex; i < c.Candles.Count; i++)
                {
                    var candle = c.Candles[i];
                    if (candle.CloseTimeTicks > nextTime) break;
                    c.NextIndex = i + 1;

                    // Try to close open trades
                    foreach (var t in trades
                        .OpenTrades
                        .Where(z => z.Trade.Market == c.Market)
                        .ToList())
                    {
                        if (t.Trade.ClosePrice != null)
                        {
                            throw new ApplicationException("Open trades list contains a closed trade");
                        }


                        t.Trade.SimulateTrade(candle, out var updated);

                        if (t.Trade.CloseDateTime != null)
                        {
                            // Processed closed open trade
                            trades.MoveOpenToClose(t);
                            UpdateBalanceAndClosedTradeProfit(t.Trade);
                        }
                    }

                    // Try to fill orders
                    foreach (var t in trades
                        .OrderTradesAsc
                        .Where(z => z.Trade.Market == c.Market)
                        .ToList())
                    {
                        if (t.Trade.OrderDateTime != null && candle.CloseTimeTicks < t.Trade.OrderDateTime.Value.Ticks)
                        {
                            continue;
                        }

                        t.Trade.SimulateTrade(candle, out _);

                        if (t.Trade.EntryDateTime != null)
                        {
                            // Process filled order
                            trades.MoveOrderToOpen(t);
                            UpdateNewOpenTradeAmount(t.Trade, candle);
                        }
                        else if (t.Trade.CloseDateTime != null)
                        {
                            // Order was closed without filling it
                            trades.MoveOrderToClosed(t);
                        }
                    }
                }
            }
        }

        private void UpdateBalanceAndClosedTradeProfit(Trade t)
        {
            var sellReturn = t.EntryQuantity.Value * t.ClosePrice.Value;
            var fee = sellReturn * _transactionFee;
            var profit = sellReturn - t.RiskAmount.Value - fee;

            t.NetProfitLoss = profit;
            Balance += sellReturn - fee;
        }

        private void UpdateNewOpenTradeAmount(Trade t, Candle candle)
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

            var tradeValue = t.EntryQuantity.Value * (decimal)(t.TradeDirection == TradeDirection.Long ? candle.CloseBid : candle.CloseAsk);
            CurrentValue = CurrentValue - cost + tradeValue - (tradeValue * _transactionFee);
        }

        private Dictionary<string, TimeframeLookup<List<Candle>>> CreateCurrentCandles(string[] markets, Timeframe[] timeframes)
        {
            var ret = new Dictionary<string, TimeframeLookup<List<Candle>>>();

            foreach (var m in markets)
            {
                var lookup = new TimeframeLookup<List<Candle>>();

                foreach (var t in timeframes)
                {
                    lookup.Add(t, new List<Candle>(100000));
                }

                ret.Add(m, lookup);
            }

            return ret;
        }

        private void LogProgress(TradeWithIndexingCollection trades, long? currentDateTimeTicks = null)
        {
            var closedTrades = new List<Trade>(5000);
            closedTrades.AddRange(trades.ClosedTrades.Select(x => x.Trade));

            var orderTrades = new List<Trade>(5000);
            orderTrades.AddRange(trades.OrderTradesAsc.Select(x => x.Trade));

            var tradesForExpectancy = closedTrades.Where(x => x.RMultiple != null).ToList();
            var expectancy = TradingCalculator.CalculateExpectancy(tradesForExpectancy);

            Log.Info(
                currentDateTimeTicks != null ? $"Up to: {new DateTime(currentDateTimeTicks.Value): dd-MM-yy HH:mm} " : String.Empty
                // /* {r.PercentComplete:0.00}% /. Running: {r.SecondsRunning}s. "
                + $"Created: {orderTrades.Count() + closedTrades.Count + trades.OpenTrades.Count()} "
                + $"Open: {trades.OpenTrades.Count()}. Orders: {orderTrades.Count()} "
                + $"Closed: {closedTrades.Count} (Hit stop: {closedTrades.Count(x => x.CloseReason == TradeCloseReason.HitStop)} "
                + $"Hit limit: {closedTrades.Count(x => x.CloseReason == TradeCloseReason.HitLimit)} "
                + $"Hit expiry: {closedTrades.Count(x => x.CloseReason == TradeCloseReason.HitExpiry)} "
                + $"Cached trades: {trades.CachedTradesCount}) "
                + $"Expectancy: {expectancy:0.00}");
        }


        private List<AddedCandleTimeframe> ProgressStrategyCandles(
            List<CandlesWithIndex> tfOrderedStrategyCandles,
            Dictionary<string, TimeframeLookup<List<Candle>>> currentCandles,
            Dictionary<string, float> currentPrices,
            out long currentTimeTicks,
            out long nextTimeTicks)
        {
            var ret = new List<AddedCandleTimeframe>();
            currentTimeTicks = long.MaxValue;
            nextTimeTicks = long.MaxValue;

            if (tfOrderedStrategyCandles[0].NextIndex >= tfOrderedStrategyCandles[0].Candles.Count) return ret;

            // Get earliest next time
            foreach (var c in tfOrderedStrategyCandles)
            {
                if (c.NextIndex >= c.Candles.Count) continue;
                var closeTimeTicks = c.Candles[c.NextIndex].CloseTimeTicks;
                if (closeTimeTicks < currentTimeTicks) currentTimeTicks = closeTimeTicks;
            }

            foreach (var c in tfOrderedStrategyCandles)
            {
                for (var i = c.NextIndex; i < c.Candles.Count; i++)
                {
                    var closeTimeTicks = c.Candles[i].CloseTimeTicks;
                    if (closeTimeTicks > currentTimeTicks) break;

                    if (ret.Any(z => z.Market == c.Market && z.Timeframe == c.Timeframe))
                    {
                        throw new ApplicationException();
                    }

                    currentPrices[c.Market] = c.Candles[i].CloseBid;
                    ret.Add(new AddedCandleTimeframe(c.Market, c.Timeframe));
                    currentCandles[c.Market][c.Timeframe].Add(c.Candles[i]);
                    c.NextIndex = i + 1;
                }
            }

            // Get earliest next time
            foreach (var c in tfOrderedStrategyCandles)
            {
                if (c.NextIndex >= c.Candles.Count) continue;
                var closeTimeTicks = c.Candles[c.NextIndex].CloseTimeTicks;
                if (closeTimeTicks < nextTimeTicks) nextTimeTicks = closeTimeTicks;
            }

            return ret;
        }

        private void CompleteTradeDetails(List<Trade> trades)
        {
            foreach (var t in trades)
            {
                TradeCalculator.UpdateInitialStopPips(t);
                TradeCalculator.UpdateInitialLimitPips(t);
                TradeCalculator.UpdateRMultiple(t);
            }
        }

        private List<CandlesWithIndex> GetRunCandles(StrategyBase strategy, Timeframe runTimeframe)
        {
            var ret = new List<CandlesWithIndex>();
            foreach (var m in strategy.Markets)
            {
                ret.Add(new CandlesWithIndex(m, runTimeframe, _candleService.GetCandles(
                    _broker, m, runTimeframe, false)));
            }

            return ret;
        }

        private List<CandlesWithIndex> GetTfOrderedStrategyCandles(StrategyBase strategy)
        {
            var ret = new List<CandlesWithIndex>();
            foreach (var tf in strategy.Timeframes.OrderBy(x => x))
            {
                foreach (var m in strategy.Markets)
                {
                    ret.Add(new CandlesWithIndex(m, tf, _candleService.GetCandles(
                        _broker, m, tf, false)));
                }
            }

            return ret;
        }

        private class CandlesWithIndex
        {
            public CandlesWithIndex(string market, Timeframe timeframe, List<Candle> candles)
            {
                Market = market;
                Timeframe = timeframe;
                Candles = candles;
            }

            public string Market { get; }
            public Timeframe Timeframe { get; }
            public List<Candle> Candles { get; }
            public int NextIndex { get; set; } = 0;
        }
    }

    public class AddedCandleTimeframe
    {
        public AddedCandleTimeframe(string market, Timeframe timeframe)
        {
            Market = market;
            Timeframe = timeframe;
        }

        public string Market { get; }
        public Timeframe Timeframe { get; }
    }

}