namespace TraderTools.Basics
{
    public interface ITradeDetailsAutoCalculatorService
    {
        void AddTrade(TradeDetails trade);
        void RemoveTrade(TradeDetails trade);
    }
}