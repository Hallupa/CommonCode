using TraderTools.Basics;
using TraderTools.Basics.Extensions;

namespace TraderTools.Core.Extensions
{
    public static class CandleExtensions
    {
        public static CandleColour Colour(this CandleAndIndicators candle)
        {
            if (candle.Candle.CloseBid > candle.Candle.OpenBid)
            {
                return CandleColour.White;
            }

            if (candle.Candle.CloseBid < candle.Candle.OpenBid)
            {
                return CandleColour.Black;
            }

            return CandleColour.None;
        }
    }
}