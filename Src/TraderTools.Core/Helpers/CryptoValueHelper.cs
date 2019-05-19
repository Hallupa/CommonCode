using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using log4net;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Core.Broker;
using TraderTools.Core.Services;
using TraderTools.Core.Trading;

namespace TraderTools.Core.Helpers
{
    public static class CryptoValueHelper
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static List<(DateTime Time, decimal Value)> GetDailyUsdValueIfInvestingInBtc(
            IBrokersCandlesService candleService, BrokerAccount account, IBroker broker)
        {
            var ret = new List<(DateTime Time, decimal Value)>();
            /*var ownedBtc = 0.0M;

            for (var date = new DateTime(2018, 1, 1); date <= DateTime.UtcNow; date = date.AddDays(1))
            {
                var ethUsd = candleService.GetFirstCandleThatClosesBeforeDateTime("ETHUSDT", broker, Timeframe.D1, date);
                var ethBtc = candleService.GetFirstCandleThatClosesBeforeDateTime("ETHBTC", broker, Timeframe.D1, date);

                if (ethUsd != null && ethBtc != null)
                {
                    // Get BTC price in USD
                    var btcUsd = (1.0 / ethBtc.Value.Close) * ethUsd.Value.Close;

                    foreach (var depositWithdrawal in account.DepositsWithdrawals.Where(x =>
                        x.Time.Year == date.Year && x.Time.Month == date.Month && x.Time.Day == date.Day))
                    {
                        var moneyIn = GetAssetUsdValue(candleService, broker, depositWithdrawal.Asset,
                            depositWithdrawal.Time, depositWithdrawal.Amount);
                        if (moneyIn != 0.0M)
                        {
                            ownedBtc += moneyIn / (decimal)btcUsd;
                        }
                    }
                    ret.Add((date, ownedBtc * (decimal)btcUsd));
                }
                else
                {
                    ret.Add((date, 0));
                }
            }*/

            return ret;
        }

        public static List<(DateTime Time, decimal Value)> GetDailyUsdValueIfInvestingInEth(
            IBrokersCandlesService candleService, BrokerAccount account, IBroker broker)
        {
            var ret = new List<(DateTime Time, decimal Value)>();
            /*var ownedBtc = 0.0M;

            for (var date = new DateTime(2018, 1, 1); date <= DateTime.UtcNow; date = date.AddDays(1))
            {
                var ethUsd = candleService.GetFirstCandleThatClosesBeforeDateTime("ETHUSDT", broker, Timeframe.D1, date);

                if (ethUsd != null)
                {
                    foreach (var depositWithdrawal in account.DepositsWithdrawals.Where(x =>
                        x.Time.Year == date.Year && x.Time.Month == date.Month && x.Time.Day == date.Day))
                    {
                        var moneyIn = GetAssetUsdValue(candleService, broker, depositWithdrawal.Asset,
                            depositWithdrawal.Time, depositWithdrawal.Amount);
                        if (moneyIn != 0.0M)
                        {
                            ownedBtc += moneyIn / (decimal)ethUsd.Value.Close;
                        }
                    }
                    ret.Add((date, ownedBtc * (decimal)ethUsd.Value.Close));
                }
                else
                {
                    ret.Add((date, 0));
                }
            }*/

            return ret;
        }

        /*public static void CalculateTradeAssetPrice1(
            TradeDetails trade, BrokersCandlesService candleService, IBroker fxcm, IBroker binance,
            out decimal asset1USDPrice, out decimal asset2USDPrice, out decimal asset1GBPPrice,
            out decimal asset2GBPPrice)
        {
            var asset1 = trade.BaseAsset;
            var asset2 = trade.Market.Replace(asset1, string.Empty);
            asset2USDPrice = 0M;
            asset1USDPrice = 0M;

            if (asset2 == "GBP")
            {
                var candle = candleService.GetFirstCandleThatClosesBeforeDateTime("GBPUSD", fxcm, Timeframe.D1, trade.OrderDateTime.Value, false);
                asset2USDPrice = (decimal)(1.0 / candle.Value.Close);
            }
            else if (asset2 == "EUR")
            {
                var candle = candleService.GetFirstCandleThatClosesBeforeDateTime("EURUSD", fxcm, Timeframe.D1, trade.OrderDateTime.Value, false);
                asset2USDPrice = (decimal)(1.0 / candle.Value.Close);
            }

            var updateCandles = false;
            if (asset2USDPrice == 0M)
            {
                asset2USDPrice = CryptoValueHelper.GetAssetUsdValue(
                    candleService, binance, asset2, trade.OrderDateTime.Value, 1.0M, updateCandles);
            }

            if (asset1USDPrice == 0M && asset2USDPrice != 0M
                                                && trade.EntryPrice != null && trade.EntryPrice.Value != 0M)
            {
                asset1USDPrice = asset2USDPrice * trade.EntryPrice.Value;
            }
            else
            {
                asset1USDPrice = CryptoValueHelper.GetAssetUsdValue(
                    candleService, binance, asset1, trade.OrderDateTime.Value, 1.0M, updateCandles);
            }

            if (asset2USDPrice == 0M && asset1USDPrice != 0M
                                                && trade.EntryPrice != null && trade.EntryPrice.Value != 0M)
            {
                asset2USDPrice = asset1USDPrice / trade.EntryPrice.Value;
            }

            var gbpUsd = candleService.GetFirstCandleThatClosesBeforeDateTime("GBPUSD", fxcm, Timeframe.D1, trade.OrderDateTime.Value, false);
            asset1GBPPrice = (decimal)gbpUsd.Value.Close * asset1USDPrice;
            asset2GBPPrice = (decimal)gbpUsd.Value.Close * asset2USDPrice;
        }*/

        /*public static void CalculateTradeAssetPrice(
            TradeDetails trade, BrokersCandlesService candleService, IBroker fxcm, IBroker binance,
            out decimal asset1USDPrice, out decimal asset2USDPrice, out decimal asset1GBPPrice,
            out decimal asset2GBPPrice)
        {
            var asset1 = trade.BaseAsset;
            var asset2 = trade.Market.Replace(trade.BaseAsset, string.Empty);
            var date = trade.OrderDateTime.Value;

            asset1USDPrice = 0M;
            asset2USDPrice = 0M;
            asset1GBPPrice = 0M;
            asset2GBPPrice = 0M;

            if (asset2 == "USD")
            {
                var candle = candleService.GetFirstCandleThatClosesBeforeDateTime("GBPUSD", fxcm, Timeframe.D1, date, false);
                asset2USDPrice = 1M;
                asset2GBPPrice = 1.0M / (decimal)candle.Value.Close;
            }
            else if (asset2 == "GBP")
            {
                var candle = candleService.GetFirstCandleThatClosesBeforeDateTime("GBPUSD", fxcm, Timeframe.D1, date, false);
                asset2GBPPrice = 1M;
                asset2USDPrice = (decimal)candle.Value.Close;
            }
            else if (asset2 == "EUR")
            {
                var candle1 = candleService.GetFirstCandleThatClosesBeforeDateTime("EURUSD", fxcm, Timeframe.D1, date, false);
                var candle2 = candleService.GetFirstCandleThatClosesBeforeDateTime("GBPUSD", fxcm, Timeframe.D1, date, false);
                asset2USDPrice = (decimal)candle1.Value.Close;
                asset2GBPPrice = asset2USDPrice / (decimal)candle2.Value.Close;
            }

            if (asset2USDPrice == 0M)
            {
                asset2USDPrice = CryptoValueHelper.GetAssetUsdValue(candleService, binance, asset2, date, 1.0M, false);
                var candle2 = candleService.GetFirstCandleThatClosesBeforeDateTime("GBPUSD", fxcm, Timeframe.D1, date, false);
                asset2GBPPrice = asset2USDPrice / (decimal)candle2.Value.Close;
            }

            if (asset2USDPrice == 0M)
            {
                // Try to get asset 1 price
                asset1USDPrice = CryptoValueHelper.GetAssetUsdValue(candleService, binance, asset1, date, 1.0M, false);
                var candle2 = candleService.GetFirstCandleThatClosesBeforeDateTime("GBPUSD", fxcm, Timeframe.D1, date, false);
                asset1GBPPrice = asset1USDPrice / (decimal)candle2.Value.Close;
            }

            if (asset1USDPrice == 0M)
            {
                asset1USDPrice = trade.EntryPrice.Value * asset2USDPrice;
                asset1GBPPrice = trade.EntryPrice.Value * asset2GBPPrice;
            }

            if (asset2USDPrice == 0M)
            {
                asset2USDPrice = asset1USDPrice / trade.EntryPrice.Value;
                asset2GBPPrice = asset1GBPPrice / trade.EntryPrice.Value;
            }

            if (asset2USDPrice == 0M || asset2GBPPrice == 0M || asset1USDPrice == 0M || asset1GBPPrice == 0M)
            {
            }
        }*/
    }
}