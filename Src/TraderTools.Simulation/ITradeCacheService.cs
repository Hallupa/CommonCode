using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Simulation
{
    public interface ITradeCacheService
    {
        void AddTrades(string market, IEnumerable<Trade> trades);
        CachedTradeResult? GetCachedTrade(Trade t);
        void SaveTrades();
        void LoadTrades(string market);
    }
}