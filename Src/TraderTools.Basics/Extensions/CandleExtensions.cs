using System;
using System.Collections.Generic;

namespace TraderTools.Basics.Extensions
{
    public enum CandleColour
    {
        White,
        Black,
        None
    }

    public static class CandleExtensions
    {
        public static CandleColour Colour(this Candle candle)
        {
            if (candle.CloseBid > candle.OpenBid)
            {
                return CandleColour.White;
            }

            if (candle.CloseBid < candle.OpenBid)
            {
                return CandleColour.Black;
            }

            return CandleColour.None;
        }

        public static DateTime OpenTime(this Candle candle)
        {
            return new DateTime(candle.OpenTimeTicks, DateTimeKind.Utc);
        }

        public static DateTime CloseTime(this Candle candle)
        {
            return new DateTime(candle.CloseTimeTicks, DateTimeKind.Utc);
        }

        public static List<Candle> CreateHeikinAshiCandles(this List<Candle> candles)
        {
            var hk = new List<Candle>();

            for (var i = 0; i < candles.Count; i++)
            {
                var c = candles[i];

                var closeAsk = (float)(1.0 / 4.0) * (c.OpenAsk + c.HighAsk + c.LowAsk + c.CloseAsk);
                var closeBid = (float)(1.0 / 4.0) * (c.OpenBid + c.HighBid + c.LowBid + c.CloseBid);
                var openAsk = i > 0
                    ? (float) (1.0 / 2.0) * (hk[i - 1].OpenAsk + hk[i - 1].CloseAsk)
                    : (float) (1.0 / 2.0) * (c.OpenAsk + c.CloseAsk);
                var openBid = i > 0
                    ? (float)(1.0 / 2.0) * (hk[i - 1].OpenBid + hk[i - 1].CloseBid)
                    : (float)(1.0 / 2.0) * (c.OpenBid + c.CloseBid);
                var highAsk = Math.Max(c.HighAsk, Math.Max(closeAsk, openAsk));
                var highBid = Math.Max(c.HighBid, Math.Max(closeBid, openBid));
                var lowAsk = Math.Max(c.LowAsk, Math.Max(openAsk, closeAsk));
                var lowBid = Math.Max(c.LowBid, Math.Max(openBid, closeBid));


                hk.Add(new Candle
                {
                    CloseAsk = closeAsk,
                    CloseBid = closeBid,
                    OpenAsk = openAsk,
                    OpenBid = openBid,
                    HighAsk = highAsk,
                    HighBid = highBid,
                    LowAsk = lowAsk,
                    LowBid = lowBid,
                    OpenTimeTicks = c.OpenTimeTicks,
                    CloseTimeTicks = c.CloseTimeTicks,
                    IsComplete = c.IsComplete
                });
            }

            return hk;
        }
    }
}