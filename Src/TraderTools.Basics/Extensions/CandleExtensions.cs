using System;

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
    }
}