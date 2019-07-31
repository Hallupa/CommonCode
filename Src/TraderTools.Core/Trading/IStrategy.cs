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
            TimeframeLookup<List<BasicCandleAndIndicators>> candlesLookup,
            List<Trade> existingTrades,
            ITradeDetailsAutoCalculatorService calculatorService);

        void UpdateExistingOpenTrades(
            Trade trade,
            string market,
            TimeframeLookup<List<BasicCandleAndIndicators>> candles);
    }
}