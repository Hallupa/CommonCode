using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Simulation
{
    public class CachedDetails
    {
        public virtual Dictionary<string, List<Candle>> M1CandlesLookup { get; set; } = new Dictionary<string, List<Candle>>();
        public virtual Dictionary<string, TimeframeLookupBasicCandleAndIndicators> CandlesLookup { get; set; } = new Dictionary<string, TimeframeLookupBasicCandleAndIndicators>();
        public virtual Dictionary<TradeDetailsKey, Trade> TradesLookup { get; set; } = new Dictionary<TradeDetailsKey, Trade>();
    }
}