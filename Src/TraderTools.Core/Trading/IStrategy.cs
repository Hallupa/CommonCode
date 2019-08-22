using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Core.Trading
{
    public interface IStrategy
    {
        string Name { get; }

        TimeframeLookup<Indicator[]> CreateTimeframeIndicators();

        Timeframe[] CandleTimeframesRequired { get; }

        List<Trade> CreateNewTrades(
            MarketDetails market,
            TimeframeLookup<List<CandleAndIndicators>> candlesLookup,
            List<Trade> existingTrades,
            ITradeDetailsAutoCalculatorService calculatorService);
    }
}