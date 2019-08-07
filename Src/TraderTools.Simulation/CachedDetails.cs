using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Simulation
{
    public class CachedDetails
    {
        public virtual Dictionary<string, List<ICandle>> M1CandlesLookup { get; set; } = new Dictionary<string, List<ICandle>>();
        public virtual Dictionary<string, TimeframeLookupBasicCandleAndIndicators> CandlesLookup { get; set; } = new Dictionary<string, TimeframeLookupBasicCandleAndIndicators>();
        public virtual Dictionary<TradeDetailsKey, Trade> TradesLookup { get; set; } = new Dictionary<TradeDetailsKey, Trade>();
    }
}