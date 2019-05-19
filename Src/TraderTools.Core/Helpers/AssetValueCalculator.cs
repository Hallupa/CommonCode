using System;
using System.Reflection;
using log4net;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;

namespace TraderTools.Core.Helpers
{
    public static class AssetValueCalculator
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private const Timeframe TimeframeForGettingPrices = Timeframe.H1;

        public static decimal GetAsetGbpValue(
            IBrokersCandlesService candleService, IBroker binanceBroker, IBroker fxcmBroker,
            string asset, DateTime date, decimal amount, out bool success, bool updateCandles = false)
        {
            success = false;
            if (asset == "GBP")
            {
                success = true;
                return amount;
            }

            var valueUsd = GetAssetUsdValue(candleService, binanceBroker, fxcmBroker, asset, date, amount,
                out success, updateCandles);
            var gbpUsd =
                candleService.GetFirstCandleThatClosesBeforeDateTime("GBP/USD", fxcmBroker, TimeframeForGettingPrices,
                    date,
                    updateCandles);

            return valueUsd / (decimal)gbpUsd.Close;
        }

        public static decimal GetAssetUsdValue(
            IBrokersCandlesService candleService, IBroker binanceBroker, IBroker fxcmBroker,
            string asset, DateTime date, decimal amount, out bool success, bool updateCandles = false)
        {
            success = false;
            if (amount == 0M) return 0M;

            if (asset == "EUR")
            {
                var eurUsd = candleService.GetFirstCandleThatClosesBeforeDateTime("EUR/USD", fxcmBroker,
                    TimeframeForGettingPrices, date, updateCandles);
                success = true;
                return amount * (decimal)eurUsd.Close;
            }

            if (asset == "GBP")
            {
                var gbpUsd = candleService.GetFirstCandleThatClosesBeforeDateTime("GBP/USD", fxcmBroker,
                    TimeframeForGettingPrices, date, updateCandles);
                success = true;
                return amount * (decimal)gbpUsd.Close;
            }

            if (asset == "USD")
            {
                success = true;
                return amount;
            }

            var ethUsd = candleService.GetFirstCandleThatClosesBeforeDateTime("ETHUSDT", binanceBroker,
                TimeframeForGettingPrices, date, updateCandles);

            if (asset == "BTC")
            {
                var dailyCandle = candleService.GetFirstCandleThatClosesBeforeDateTime($"ETH{asset}", binanceBroker,
                    TimeframeForGettingPrices, date, updateCandles);
                if (dailyCandle == null)
                {
                    //Log.Error($"Unable to calculate accurate assets value - ETH{asset} candles not loaded");
                    return 0M;
                }

                var ethAmount = amount / (decimal)dailyCandle.Close;
                var usdAmount = ethAmount * (decimal)ethUsd.Close;
                success = true;
                return usdAmount;
            }

            if (asset == "USDT")
            {
                success = true;
                return amount;
            }

            var btcUsd = candleService.GetFirstCandleThatClosesBeforeDateTime("BTCUSDT", binanceBroker,
                TimeframeForGettingPrices, date, updateCandles);

            if (asset == "GAS" && btcUsd != null)
            {
                var dailyCandle = candleService.GetFirstCandleThatClosesBeforeDateTime("GASBTC", binanceBroker,
                    TimeframeForGettingPrices, date, updateCandles);
                if (dailyCandle != null)
                {
                    var btcAmount = (decimal) dailyCandle.Close * amount;
                    var usdAmount = btcAmount * (decimal) btcUsd.Close;
                    success = true;
                    return usdAmount;
                }
            }

            if (asset != "ETH")
            {
                var dailyCandle = candleService.GetFirstCandleThatClosesBeforeDateTime($"{asset}ETH", binanceBroker,
                    TimeframeForGettingPrices, date, updateCandles);
                if (dailyCandle != null)
                {
                    var ethAmount = (decimal)dailyCandle.Close * amount;
                    var usdAmount = ethAmount * (decimal)ethUsd.Close;
                    success = true;
                    return usdAmount;
                }
            }

            if (asset != "BTC")
            {
                var dailyCandle = candleService.GetFirstCandleThatClosesBeforeDateTime($"{asset}BTC", binanceBroker, TimeframeForGettingPrices, date, updateCandles);
                if (dailyCandle != null)
                {
                    var btcAmount = (decimal)dailyCandle.Close * amount;
                    var usdAmount = btcAmount * (decimal)btcUsd.Close;
                    success = true;
                    return usdAmount;
                }
            }

            if (asset == "ETH" && ethUsd != null)
            {
                var ethAmount = amount;
                var usdAmount = ethAmount * (decimal)ethUsd.Close;
                success = true;
                return usdAmount;
            }

            return 0M;
        }
    }
}