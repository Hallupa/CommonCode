using System;
using System.Collections.Generic;
using TraderTools.Basics;


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

            if (trade.StopPrices.Count == 0 || trade.StopPrices[0].Price == null) return;

            var trailStopTimeframeCandles = candles[trailTimeframe];
            var candle = trailStopTimeframeCandles[trailStopTimeframeCandles.Count - 1];

            // Only update the stop once per candle
            if (candle.Candle.IsComplete == 0) return;
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
    }
}