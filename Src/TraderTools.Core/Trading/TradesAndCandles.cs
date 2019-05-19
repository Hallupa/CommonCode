using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Core.Trading
{
    public class TradesAndCandles
    {
        public List<TradeDetails> Trades { get; set; } = new List<TradeDetails>();
        public TimeframeLookup<List<BasicCandleAndIndicators>> CandlesLookup { get; set; } = 
            new TimeframeLookup<List<BasicCandleAndIndicators>>();
    }
}