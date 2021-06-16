using System;
using System.Collections.Generic;
using TraderTools.Basics;
using TraderTools.Core.Helpers;

namespace TraderTools.Core.Extensions
{
    public static class BrokersCandlesServiceExtensions
    {
        public static List<Candle> GetDerivedCandles(this IBrokersCandlesService candlesService,
            IBroker broker, string firstSymbol, string secondSymbol, Timeframe timeframe,
            bool updateCandles = false, DateTime? minOpenTimeUtc = null, DateTime? maxCloseTimeUtc = null)
        {
            var pair = $"{firstSymbol}{secondSymbol}";
            var candles = candlesService.GetCandles(broker, pair, timeframe, false);
            if (candles != null && candles.Count > 0) return candles;

            var calculatedMarketCandles = new DerivedMarketCandles(broker, candlesService);

            return calculatedMarketCandles.CreateCandlesSeries(
                firstSymbol,
                secondSymbol,
                timeframe,
                updateCandles,
                minOpenTimeUtc,
                maxCloseTimeUtc);
        }

        public static List<Candle> GetDerivedCandles(this IBrokersCandlesService candlesService,
            IBroker broker, string pair, Timeframe timeframe,
            bool updateCandles = false, DateTime? minOpenTimeUtc = null, DateTime? maxCloseTimeUtc = null)
        {
            var candles = candlesService.GetCandles(broker, pair, timeframe, false);
            if (candles != null && candles.Count > 0) return candles;

            var calculatedMarketCandles = new DerivedMarketCandles(broker, candlesService);

            return calculatedMarketCandles.CreateCandlesSeries(
                pair,
                timeframe,
                updateCandles,
                minOpenTimeUtc,
                maxCloseTimeUtc);
        }
    }
}