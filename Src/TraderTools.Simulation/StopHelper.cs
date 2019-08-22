using System;
using System.Collections.Generic;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;


namespace TraderTools.Simulation
{
    public enum StopStrategy
    {
        Default = 0,
        VeryClose = 1,
        Close = 2,
        Far = 3,
        VeryFar = 4,
        VeryVeryFar = 5
    }

    public static class StopHelper
    {
        /// <summary>
        /// https://help.fxcm.com/markets/Trading/Execution-Rollover/Order-Types/876471181/What-is-a-Trailing-Stop-and-how-do-I-place-it.htm
        /// </summary>
        public static void TrailDynamicStop(Trade trade, TimeframeLookup<List<CandleAndIndicators>> candles, long currentTimeTicks)
        {
            if (trade.TradeDirection == null || trade.EntryPrice == null || trade.ClosePrice != null)
            {
                return;
            }

            var candle = candles[Timeframe.H2][candles[Timeframe.H2].Count - 1];

            // Only update the stop every hour
            if (trade.StopPrices.Count > 0 && trade.StopPrices[trade.StopPrices.Count - 1].Date.AddMinutes(60).Ticks >
                currentTimeTicks) return;


            var initialDiff = trade.EntryPrice.Value - trade.StopPrices[0].Price.Value;
            var newStop = trade.TradeDirection == TradeDirection.Long
                ? (decimal) candle.Candle.CloseBid - initialDiff
                : (decimal) candle.Candle.CloseAsk - initialDiff;

            if (trade.TradeDirection == TradeDirection.Long && newStop > trade.StopPrice)
            {
                trade.AddStopPrice(new DateTime(currentTimeTicks, DateTimeKind.Utc), (decimal)newStop);
                trade.StopPrice = (decimal)newStop;
            }

            if (trade.TradeDirection == TradeDirection.Short && newStop < trade.StopPrice)
            {
                trade.AddStopPrice(new DateTime(currentTimeTicks, DateTimeKind.Utc), (decimal)newStop);
                trade.StopPrice = (decimal)newStop;
            }
        }


        public static void TrailIndicator(Trade trade, Timeframe trailTimeframe, Indicator trailIndicator, TimeframeLookup<List<CandleAndIndicators>> candles, long currentTimeTicks)
        {
            if (trade.TradeDirection == null || trade.EntryPrice == null || trade.ClosePrice != null)
            {
                return;
            }

            var trailStopTimeframeCandles = candles[trailTimeframe];
            var candle = trailStopTimeframeCandles[trailStopTimeframeCandles.Count - 1];

            // Only update the stop once per candle
            if (trade.StopPrices.Count > 0 && trade.StopPrices[trade.StopPrices.Count - 1].Date.AddSeconds((int)trailTimeframe).Ticks > currentTimeTicks) return;
            if (trade.StopPrices.Count == 0 || trade.StopPrices[0].Price == null) return;
            if (!candle[trailIndicator].IsFormed) return;

            // Get initial stop distance from indicator
            var indicatorStopDiff = 0M;
            for (var i = candles[trailTimeframe].Count - 1; i >= 0; i--)
            {
                var c = candles[trailTimeframe][i];
                if (c.Candle.OpenTimeTicks <= trade.StopPrices[0].Date.Ticks)
                {
                    indicatorStopDiff = Math.Abs((decimal)candles[trailTimeframe][i][trailIndicator].Value - trade.StopPrices[0].Price.Value);
                    break;
                }
            }


            if (trade.TradeDirection == TradeDirection.Long)
            {
                var newStop = (decimal)candle[trailIndicator].Value - indicatorStopDiff;

                if (newStop > trade.StopPrice.Value)
                {
                    trade.AddStopPrice(new DateTime(currentTimeTicks, DateTimeKind.Utc), (decimal)newStop);
                    trade.StopPrice = (decimal)newStop;
                }
            }
            else
            {
                var newStop = (decimal)candle[trailIndicator].Value + indicatorStopDiff;

                if (newStop < trade.StopPrice.Value)
                {
                    trade.AddStopPrice(new DateTime(currentTimeTicks, DateTimeKind.Utc), (decimal)newStop);
                    trade.StopPrice = (decimal)newStop;
                }
            }
        }

        public static void Trail00or50LevelList(Trade trade, string market, TimeframeLookup<List<CandleAndIndicators>> candles)
        {
            if (trade.TradeDirection == null)
            {
                return;
            }

            var trailStopTimeframe = (Timeframe)trade.Custom2;
            var emaIndex = (Indicator)trade.Custom3;
            var trailStopStrategy = (StopStrategy)trade.Custom4;
            var trailStopTimeframeCandles = candles[trailStopTimeframe];

            if (trailStopTimeframeCandles.Count - 2 < 0)
            {
                return;
            }

            var candle = trailStopTimeframeCandles[trailStopTimeframeCandles.Count - 1];
            var prevCandle = trailStopTimeframeCandles[trailStopTimeframeCandles.Count - 2];

            // Only update the stop once per candle
            if (trade.StopPrices.Count > 0 && trade.StopPrices[trade.StopPrices.Count - 1].Date.AddSeconds((int)trailStopTimeframe) > candle.Candle.CloseTime()) return;

            var initialStopDistance = Math.Abs((trade.OrderPrice ?? trade.EntryPrice.Value) - trade.InitialStop.Value);
            
            var emaValue = candle[emaIndex].Value;
            var priceAdjustRatio = double.NaN;

            switch (trailStopStrategy)
            {
                case StopStrategy.VeryClose:
                    priceAdjustRatio = 0.02;
                    break;
                case StopStrategy.Close:
                    priceAdjustRatio = 0.03;
                    break;
                case StopStrategy.Far:
                    priceAdjustRatio = 0.04;
                    break;
                case StopStrategy.VeryFar:
                    priceAdjustRatio = 0.045;
                    break;
                case StopStrategy.VeryVeryFar:
                    priceAdjustRatio = 0.07;
                    break;
            }

            //Update so only move stop once one cursor above 00 50
            //improve expiry - should be 3 bars
            var nextStop = GetNextStop(candle, trade.TradeDirection.Value, market, (decimal)emaValue, out var priceAdjust, trailStopTimeframe, priceAdjustRatio);

            if (nextStop != null)
            {
                var currentStop = trade.StopPrice.Value;

                if (trade.TradeDirection == TradeDirection.Long && nextStop.Value > currentStop && Math.Abs(nextStop.Value - currentStop) > (decimal)priceAdjust
                    && Math.Abs(nextStop.Value - (decimal)candle.Candle.HighBid) >= initialStopDistance
                    && Math.Abs(nextStop.Value - (decimal)prevCandle.Candle.HighBid) >= initialStopDistance)
                {
                    trade.AddStopPrice(candle.Candle.CloseTime(), nextStop.Value);
                }

                if (trade.TradeDirection == TradeDirection.Short && nextStop.Value < currentStop && Math.Abs(nextStop.Value - currentStop) > (decimal)priceAdjust
                    && Math.Abs(nextStop.Value - (decimal)candle.Candle.LowAsk) >= initialStopDistance
                    && Math.Abs(nextStop.Value - (decimal)prevCandle.Candle.LowAsk) >= initialStopDistance)
                {
                    trade.AddStopPrice(candle.Candle.CloseTime(), nextStop.Value);
                }
            }
        }

        /// <summary>
        /// EMA8 must be past EMA8 by a cursor distance for 00/50 level to be valid.
        /// If EMA8 is past the 00/50 level, then the stop is a cursor distance above (Short) or below (Long)
        /// the 00/50 level.
        /// </summary>
        /// <param name="candlesLookup"></param>
        /// <param name="market"></param>
        /// <param name="currentEma"></param>
        /// <param name="priceAdjustTimeframe"></param>
        /// <param name="priceAdjustRatio"></param>
        /// <returns></returns>
        public static decimal? GetNextStop(
            CandleAndIndicators candle, TradeDirection direction, string market, decimal currentEma,
            out double priceAdjust, Timeframe priceAdjustTimeframe = Timeframe.H2, double priceAdjustRatio = double.NaN)
        {
            // TODO needs fixing
            /*
            var currentEmaInPips = (double)PipsHelper.GetPriceInPips(currentEma, market);

            priceAdjust =
                double.IsNaN(priceAdjustRatio)
                    ? DistanceHelper.GetPriceAdjust(candle, priceAdjustTimeframe)
                    : DistanceHelper.GetPriceAdjust(candle, priceAdjustTimeframe, priceAdjustRatio);
            var priceAdjustInPips = (double)PipsHelper.GetPriceInPips((decimal)priceAdjust, market);
            decimal? ret = null;

            if (direction == TradeDirection.Long)
            {
                var startPrice = currentEmaInPips - priceAdjustInPips;
                var nearest00 = (int)(startPrice / 100.0) * 100.0;
                var nearest50 = nearest00 + 50.0;

                if (nearest50 <= startPrice)
                    ret = (decimal)nearest50;
                else
                    ret = (decimal)nearest00;
            }

            if (direction == TradeDirection.Short)
            {
                var startPrice = currentEmaInPips + priceAdjustInPips;
                var level00 = (int)(startPrice / 100.0) * 100.0;
                var nearest50 = level00 + 50.0;

                if (level00.Equals(startPrice))
                    ret = (decimal)startPrice;
                else if (nearest50 >= startPrice)
                    ret = (decimal)nearest50;
                else
                    ret = (decimal)(level00 + 100.0);
            }*/

            // TODO needs fixing
            /*return ret != null
                ? (direction == TradeDirection.Long
                    ? PipsHelper.GetPriceFromPips(ret.Value, market) - (decimal)priceAdjust
                    : PipsHelper.GetPriceFromPips(ret.Value, market) + (decimal)priceAdjust)
                : (decimal?)null;*/

            priceAdjust = 0.0;
            return (decimal?)null;
        }
    }
}