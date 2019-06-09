using System;
using System.Collections.Generic;
using TraderTools.Basics;
using TraderTools.Core.Services;

namespace TraderTools.Core.Trading
{
    public interface IStrategy
    {
        string Name { get; }

        TimeframeLookup<Indicator[]> CreateTimeframeIndicators();

        Timeframe[] CandleTimeframesRequired { get; }

        List<TradeDetails> CreateNewTrades(
            Timeframe timeframe,
            MarketDetails market,
            TimeframeLookup<List<BasicCandleAndIndicators>> candlesLookup,
            List<TradeDetails> existingTrades);

        void UpdateExistingOpenTrades(
            TradeDetails trade,
            string market,
            TimeframeLookup<List<BasicCandleAndIndicators>> candles);
    }
}