using System;
using System.Collections.Generic;

namespace TraderTools.Basics.Helpers
{
    public static class CandlesHelper
    {
        public static Candle? GetFirstCandleThatClosesBeforeDateTime(IList<Candle> candles, DateTime dateTime)
        {
            // Candles will be ordered in ascending date order
            for (var i = candles.Count - 1; i >= 0; i--)
            {
                var c = candles[i];
                if (c.CloseTimeTicks <= dateTime.Ticks)
                {
                    return c;
                }
            }

            return null;
        }
    }
}