using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
    public class StrategyRunner
    {
        private IBrokersCandlesService _candlesService;
        private readonly ITradeDetailsAutoCalculatorService _calculatorService;
        private readonly IMarketDetailsService _marketDetailsService;
        private readonly ITradeCacheService _tradeCacheService;
        private readonly SimulationRunnerFlags _options;
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public StrategyRunner(IBrokersCandlesService candleService, ITradeDetailsAutoCalculatorService calculatorService, IMarketDetailsService marketDetailsService, ITradeCacheService tradeCacheService,
            SimulationRunnerFlags options = SimulationRunnerFlags.Default)
        {
            _candlesService = candleService;
            _calculatorService = calculatorService;
            _marketDetailsService = marketDetailsService;
            _tradeCacheService = tradeCacheService;
            _options = options;
        }

        private static List<RequiredTimeframeCandlesAttribute> GetRequiredTimeframesAndIndicators(IStrategy strategy)
        {
            return strategy.GetType().GetCustomAttributes(typeof(RequiredTimeframeCandlesAttribute), true).Cast<RequiredTimeframeCandlesAttribute>().ToList();
        }

        public List<Trade> Run(IStrategy strategy, MarketDetails market, IBroker broker,
            DateTime? earliest = null, DateTime? latest = null,
            bool updatePrices = false, bool cacheCandles = true, Func<bool> getShouldStopFunc = null,
            Action<List<Trade>> tradesCompletedProgressFunc = null)
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
            const int provideProgressIntervalSeconds = 10;
            DateTime provideProgressTime = DateTime.UtcNow.AddSeconds(provideProgressIntervalSeconds);

            // Get candles
            var requiredTimeframesAndIndicators = GetRequiredTimeframesAndIndicators(strategy);
            var strategyTimeframes = requiredTimeframesAndIndicators.Select(x => x.Timeframe).ToArray();
            var timeframeIndicators = GetTimeframeIndicatorsForRun(requiredTimeframesAndIndicators);
            var timeframesAllCandles = TimeframeLookupBasicCandleAndIndicators.PopulateCandles(broker, market.Name, strategyTimeframes.Union(new[] { Timeframe.M15 }).ToArray(),
                timeframeIndicators, _candlesService, updatePrices, cacheCandles, earliest, latest);
            var m1Candles = TimeframeLookupBasicCandleAndIndicators.GetM1Candles(
                broker, market.Name, _candlesService, !_options.HasFlag(SimulationRunnerFlags.DoNotCacheM1Candles), updatePrices, earliest, latest);

            var trades = new TradeWithIndexingCollection();

            TimeframeLookupBasicCandleAndIndicators.IterateThroughCandles(
                timeframesAllCandles,
                m1Candles,
                c =>
                {
                    ProcessNewCandles(strategy, market, c, trades, updateTradesStrategy);

                    if (tradesCompletedProgressFunc != null && DateTime.UtcNow >= provideProgressTime)
                    {
                        Task.Run(() => { tradesCompletedProgressFunc(trades.ClosedTrades.Select(x => x.Trade).ToList()); });
                        provideProgressTime = DateTime.UtcNow.AddSeconds(provideProgressIntervalSeconds);
                    }
                },
                r =>
                {
                    var tradesForExpectancy = trades.ClosedTrades.Where(x => x.Trade.RMultiple != null).ToList();
                    var expectancy = tradesForExpectancy.Any() ? tradesForExpectancy.Average(x => x.Trade.RMultiple.Value) : 0.0M;
                    return $"StrategyRunner: {market.Name} Up to: {r.LatestCandleDateTime:dd-MM-yy HH:mm} - {r.PercentComplete:0.00}% complete. Running for: {r.SecondsRunning}s. "
                        + $"Created {trades.OrderTrades.Count() + trades.ClosedTrades.Count() + trades.OpenTrades.Count()} trades. "
                        + $"Open: {trades.OpenTrades.Count()}. Orders: {trades.OrderTrades.Count()} "
                        + $"Closed: {trades.ClosedTrades.Count()} (Hit stop: {trades.ClosedTrades.Count(x => x.Trade.CloseReason == TradeCloseReason.HitStop)} "
                        + $"Hit limit: {trades.ClosedTrades.Count(x => x.Trade.CloseReason == TradeCloseReason.HitLimit)} "
                        + $"Hit expiry: {trades.ClosedTrades.Count(x => x.Trade.CloseReason == TradeCloseReason.HitExpiry)} "
                        + $"Cached trades: {trades.CachedTradesCount}) " 
                        + $"Expectancy: {expectancy:0.00}";
                },
                getShouldStopFunc);

            foreach (var t in trades.ClosedTrades)
            {
                if (t.Trade.UpdateMode == TradeUpdateMode.Unchanging)
                {
                    _tradeCacheService.AddTrade(t.Trade);
                }
            }

            _tradeCacheService.SaveTrades();

            return trades.AllTrades.Select(x => x.Trade).ToList();
        }



        private void ProcessNewCandles(
            IStrategy strategy,
            MarketDetails market,
            (TimeframeLookup<List<CandleAndIndicators>> CurrentCandles, Candle M1Candle, NewCandleFlags NewCandleFlags) c,
            TradeWithIndexingCollection trades, UpdateTradeStrategyAttribute updateTradesStrategy)
        {
            if (c.NewCandleFlags.HasFlag(NewCandleFlags.CompleteNonM1Candle))
            {


                AddNewTrades(trades, strategy, market, c.CurrentCandles, c.M1Candle,
                    updateTradesStrategy, c.M1Candle.CloseTime());
            }

            if (updateTradesStrategy != null && (c.NewCandleFlags.HasFlag(NewCandleFlags.CompleteNonM1Candle) || c.NewCandleFlags.HasFlag(NewCandleFlags.IncompleteNonM1Candle)))
            {
                // Update open trades
                UpdateOpenTrades(market.Name, trades, c.M1Candle.CloseTimeTicks, c.CurrentCandles, parameters => updateTradesStrategy?.UpdateTrade(parameters));
            }

            // Validate and update stops/limts/orders 
            if (!_options.HasFlag(SimulationRunnerFlags.DoNotValidateStopsLimitsOrders))
            {
                ValidateAndUpdateStopsLimitsOrders(trades, c.M1Candle);
            }




            // Process orders
            FillOrders(trades, c.M1Candle, out var anyOrdersFilledOrClosed);

            // Process open trades
            TryCloseOpenTrades(trades, c.M1Candle, out var anyClosed);


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

        private void ValidateAndUpdateStopsLimitsOrders(TradeWithIndexingCollection trades, Candle m1Candle)
        {
            var timeTicks = m1Candle.CloseTimeTicks;

            foreach (var t in trades.OpenTrades.Concat(trades.OrderTrades))
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

        private static void TryCloseOpenTrades(TradeWithIndexingCollection trades, Candle m1Candle, out bool anyClosed)
        {
            anyClosed = false;
            foreach (var trade in trades.OpenTrades)
            {
                if (trade.Trade.EntryDateTime != null && m1Candle.OpenTimeTicks >= trade.Trade.EntryDateTime.Value.Ticks)
                {
                    trade.Trade.SimulateTrade(m1Candle, out _);
                }
                
                if (trade.Trade.CloseDateTime != null)
                {
                    anyClosed = true;
                    trades.MoveOpenToClose(trade);
                }
            }
        }

        private static void FillOrders(TradeWithIndexingCollection trades, Candle m1Candle, out bool anyOrdersFilledOrClosed)
        {
            anyOrdersFilledOrClosed = false;

            foreach (var order in trades.OrderTrades)
            {
                var candleCloseTimeTicks = m1Candle.CloseTimeTicks;

                if (order.Trade.OrderDateTime != null && candleCloseTimeTicks < order.Trade.OrderDateTime.Value.Ticks)
                {
                    break;
                }

                order.Trade.SimulateTrade(m1Candle, out _);


                if (order.Trade.EntryDateTime != null)
                {
                    anyOrdersFilledOrClosed = true;
                    trades.MoveOrderToOpen(order);
                }
                else if (order.Trade.CloseDateTime != null)
                {
                    anyOrdersFilledOrClosed = true;
                    trades.MoveOrderToClosed(order);
                }
            }
        }

        private void AddNewTrades(TradeWithIndexingCollection trades,
            IStrategy strategy, MarketDetails market,
            TimeframeLookup<List<CandleAndIndicators>> timeframeCurrentCandles,
            Candle latestCandle, UpdateTradeStrategyAttribute updateTradeStrategy, DateTime currentTime)
        {
            var newTrades = strategy.CreateNewTrades(market, timeframeCurrentCandles, trades.OpenTrades.Concat(trades.OrderTrades).Select(x => x.Trade), _calculatorService, currentTime);
            
            if (newTrades != null && newTrades.Count > 0)
            {
                newTrades.ForEach(t =>
                {
                    if (string.IsNullOrEmpty(t.Strategies)) t.Strategies = strategy.Name;
                });
                var latestBidPrice = (decimal)latestCandle.CloseBid;
                var latestAskPrice = (decimal)latestCandle.CloseAsk;

                foreach (var trade in newTrades.Where(t => t.OrderPrice != null && t.EntryPrice == null))
                {
                    if (trade.TradeDirection == TradeDirection.Long)
                    {
                        trade.OrderType = trade.OrderPriceFloat <= latestCandle.CloseAsk
                            ? OrderType.LimitEntry
                            : OrderType.StopEntry;
                    }
                    else
                    {
                        trade.OrderType = trade.OrderPriceFloat <= latestCandle.CloseBid
                            ? OrderType.StopEntry
                            : OrderType.LimitEntry;
                    }
                }

                RemoveInvalidTrades(newTrades, latestBidPrice, latestAskPrice, _marketDetailsService);

                foreach (var t in newTrades)
                {
                    if (t.UpdateMode == TradeUpdateMode.Unchanging)
                    {
                        var cachedTrade = _tradeCacheService.GetCachedTrade(t);
                        if (cachedTrade != null)
                        {
                            t.CloseDateTime = cachedTrade.CloseDateTime;
                            t.ClosePrice = cachedTrade.ClosePrice;
                            t.CloseReason = cachedTrade.CloseReason;
                            t.RMultiple = cachedTrade.RMultiple;
                            trades.CachedTradesCount++;
                        }
                    }
                }

                foreach (var t in newTrades)
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
                else
                {
                    // Market trade
                    // decimal latestBidPrice, decimal latestAskPrice,
                    if (t.LimitPrice != null)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.LimitPrice < t.EntryPrice)
                        {
                            Log.Error($"Long trade for {t.Market} has limit price below current price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.LimitPrice > t.EntryPrice)
                        {
                            Log.Error($"Short trade for {t.Market} has limit price above current price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }

                    if (t.StopPrices != null && !removed)
                    {
                        if (t.TradeDirection == TradeDirection.Long && t.StopPrice > t.EntryPrice)
                        {
                            Log.Error($"Long trade for {t.Market} has stop price above current price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                        else if (t.TradeDirection == TradeDirection.Short && t.StopPrice < t.EntryPrice)
                        {
                            Log.Error($"Short trade for {t.Market} has stop price below current price. Ignoring trade");
                            newTrades.RemoveAt(i);
                            removed = true;
                        }
                    }
                }
            }
        }

        private void UpdateOpenTrades(string market, TradeWithIndexingCollection trades,
            long timeTicks,
            TimeframeLookup<List<CandleAndIndicators>> timeframesCurrentCandles, Action<UpdateTradeParameters> updateOpenTradesAction)
        {
            if (updateOpenTradesAction == null)
            {
                return;
            }

            foreach(var openTrade in trades.OpenTrades)
            {
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