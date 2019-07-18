using System.Collections.Generic;

namespace TraderTools.Basics
{
    public interface IMarketDetailsService
    {
        MarketDetails GetMarketDetails(string broker, string market);

        List<MarketDetails> GetAlMarketDetails();

        void AddMarketDetails(MarketDetails marketDetails);

        bool HasMarketDetails(string broker, string market);

        void SaveMarketDetailsList();
    }
}