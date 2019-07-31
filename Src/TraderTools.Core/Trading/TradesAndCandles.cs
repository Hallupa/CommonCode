using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Core.Trading
{
    public class TradesAndCandles
    {
        public List<Trade> Trades { get; set; } = new List<Trade>();
        public TimeframeLookup<List<BasicCandleAndIndicators>> CandlesLookup { get; set; } = 
            new TimeframeLookup<List<BasicCandleAndIndicators>>();
    }
}