using System;
using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Core.Trading
{
    public interface IStrategy
    {
        string Name { get; }

        List<Trade> CreateNewTrades(
            MarketDetails market,
            TimeframeLookup<List<CandleAndIndicators>> candlesLookup,
            IEnumerable<Trade> existingTrades,
            ITradeDetailsAutoCalculatorService calculatorService,
            DateTime currentTime);
    }
}