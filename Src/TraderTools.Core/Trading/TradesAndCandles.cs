using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Core.Trading
{
    public class TradesAndCandles
    {
        public List<Trade> Trades { get; set; } = new List<Trade>();
        public TimeframeLookup<List<CandleAndIndicators>> CandlesLookup { get; set; } = 
            new TimeframeLookup<List<CandleAndIndicators>>();
    }
}