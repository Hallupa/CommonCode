using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Strategy
{
    public class CachedDetails
    {
        public virtual Dictionary<string, List<SimpleCandle>> M1CandlesLookup { get; set; } = new Dictionary<string, List<SimpleCandle>>();
        public virtual Dictionary<string, TimeframeLookupBasicCandleAndIndicators> CandlesLookup { get; set; } = new Dictionary<string, TimeframeLookupBasicCandleAndIndicators>();
        public virtual Dictionary<TradeDetailsKey, Trade> TradesLookup { get; set; } = new Dictionary<TradeDetailsKey, Trade>();
    }
}