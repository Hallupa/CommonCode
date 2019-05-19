using System;
using System.Collections.Generic;
using System.Linq;
using TraderTools.Basics;
using TraderTools.Core.Broker;
using TraderTools.Core.Helpers;
using TraderTools.Core.Services;
using TraderTools.Core.Trading;

namespace TraderTools.Core.Extensions
{
    public static class BrokerTradesExtensions
    {
        public static List<Balance> GetAssetBalances(
            this BrokerAccount account, IBroker broker, IBrokersCandlesService candleService, DateTime date)
        {
            var balancesLookup = new Dictionary<string, decimal>();
            foreach (var trade in account.Trades.OrderBy(x => x.EntryDateTime).Where(x => x.EntryDateTime <= date))
            {
                var sellingAsset = trade.Market.Replace(trade.BaseAsset, string.Empty);
                balancesLookup.TryGetValue(trade.BaseAsset, out var baseAssetBalance);
                balancesLookup[trade.BaseAsset] =
                    baseAssetBalance + (trade.TradeDirection == TradeDirection.Long ? trade.EntryQuantity.Value : -trade.EntryQuantity.Value);

                balancesLookup.TryGetValue(trade.CommissionAsset, out var commissionAssetBalance);
                balancesLookup[trade.CommissionAsset] = commissionAssetBalance - trade.Commission.Value;

                balancesLookup.TryGetValue(sellingAsset, out var sellingAssetBalance);
                balancesLookup[sellingAsset] = sellingAssetBalance -
                                               (trade.TradeDirection == TradeDirection.Long
                                                   ? trade.EntryQuantity.Value * trade.EntryPrice.Value
                                                   : -trade.EntryQuantity.Value * trade.EntryPrice.Value);
            }

            // Add deposits/withdrawals
            foreach (var depositWithdrawal in account.DepositsWithdrawals.Where(x => x.Time <= date))
            {
                if (balancesLookup.ContainsKey(depositWithdrawal.Asset))
                {
                    balancesLookup[depositWithdrawal.Asset] += depositWithdrawal.Amount;
                }
            }

            var balances = new List<Balance>();
            foreach (var kvp in balancesLookup)
            {
                var value = AssetValueCalculator.GetAssetUsdValue(candleService, broker, null, kvp.Key, DateTime.UtcNow, kvp.Value, out var success);

                balances.Add(new Balance
                {
                    Amount = kvp.Value,
                    Asset = kvp.Key,
                    Value = value
                });
            }

            var totalValue = balances.Sum(b => b.Value);
            foreach (var balance in balances)
            {
                balance.PortfolioPercent = totalValue != 0 ? balance.Value / totalValue : 0;
            }

            return balances;
        }
    }
}