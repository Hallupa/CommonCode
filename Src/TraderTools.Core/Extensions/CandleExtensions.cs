using TraderTools.Basics;
using TraderTools.Basics.Extensions;

namespace TraderTools.Core.Extensions
{
    public static class CandleExtensions
    {
        public static CandleColour Colour(this BasicCandleAndIndicators candle)
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
    }
}