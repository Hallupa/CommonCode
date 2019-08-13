using System;

namespace TraderTools.Basics
{
    [Flags]
    public enum CalculateOptions
    {
        Default = 0,
        ExcludePricePerPip = 1,
        IncludeOpenTradesInRMultipleCalculation
    }

    public interface ITradeDetailsAutoCalculatorService
    {
        void AddTrade(Trade trade);
        void RemoveTrade(Trade trade);
        void RecalculateTrade(Trade trade, CalculateOptions options = CalculateOptions.Default);
    }
}