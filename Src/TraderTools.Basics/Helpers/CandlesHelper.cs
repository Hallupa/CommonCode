using System;
using System.Collections.Generic;
using System.Reflection;
using Hallupa.Library;
using log4net;

namespace TraderTools.Basics.Helpers
{
    public static class CandlesHelper
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static Candle? GetFirstCandleThatClosesBeforeDateTime(IList<Candle> candles, DateTime dateTime)
        {
            // Candles will be ordered in ascending date order
            for (var i = candles.Count - 1; i >= 0; i--)
            {
                var c = candles[i];
                if (c.CloseTimeTicks <= dateTime.Ticks)
                {
                    return c;
                }
            }

            return null;
        }

        public static void UpdateCandles(IBroker broker, IBrokersCandlesService candlesService, IEnumerable<string> markets, IEnumerable<Timeframe> timeframes, int threads = 3)
        {
            var producerConsumer =
                new ProducerConsumer<(string Market, Timeframe Timeframe)>(threads,
                    data =>
                    {
                        Log.Info($"Updating {data.Timeframe} candles for {data.Market}");
                        candlesService.UpdateCandles(broker, data.Market, data.Timeframe);
                        candlesService.UnloadCandles(data.Market, data.Timeframe, broker);
                        Log.Info($"Updated {data.Timeframe} candles for {data.Market}");
                        return ProducerConsumerActionResult.Success;
                    });


            foreach (var market in markets)
            {
                foreach (var timeframe in timeframes)
                {
                    producerConsumer.Add((market, timeframe));
                }
            }

            producerConsumer.SetProducerCompleted();
            producerConsumer.Start();
            producerConsumer.WaitUntilConsumersFinished();
            Log.Info("Updated FX candles");

        }
    }
}