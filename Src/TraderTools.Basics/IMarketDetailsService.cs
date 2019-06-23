using TraderTools.Basics;

namespace TraderTools.Core.Services
{
    public interface IMarketDetailsService
    {
        MarketDetails GetMarketDetails(string broker, string market);

        void AddMarketDetails(MarketDetails marketDetails);

        bool HasMarketDetails(string broker, string market);

        void SaveMarketDetailsList();
    }
}