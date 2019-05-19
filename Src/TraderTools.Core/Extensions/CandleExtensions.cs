using System;
using System.Collections.Generic;
using System.Text;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;

namespace TraderTools.Core.Extensions
{
    public static class CandleExtensions
    {
        public static CandleColour Colour(this BasicCandleAndIndicators candle)
        {
            if (candle.Close > candle.Open)
            {
                return CandleColour.White;
            }

            if (candle.Close < candle.Open)
            {
                return CandleColour.Black;
            }

            return CandleColour.None;
        }
    }
}