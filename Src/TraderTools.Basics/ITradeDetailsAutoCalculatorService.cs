using System;

namespace TraderTools.Basics
{
    [Flags]
    public enum CalculateOptions
    {
        All = 0,
        ExcludePricePerPip = 1
    }

    public interface ITradeDetailsAutoCalculatorService
    {
        void AddTrade(TradeDetails trade);
        void RemoveTrade(TradeDetails trade);
        void SetOptions(CalculateOptions options);
    }
}