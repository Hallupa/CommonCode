using System;
using System.Linq;

namespace TraderTools.Basics.Extensions
{
    public static class BrokerExtensions
    {
        /// <summary>
        /// For most currency pairs, the 'pip' location is the fourth decimal place. In this example, if the GBP/USD moved from 1.42279 to 1.42289 you would have gained or lost one pip
        /// http://help.fxcm.com/markets/Trading/Education/Trading-Basics/32856512/How-to-calculate-PIP-value.htm
        /// </summary>
        /// <param name="price"></param>
        /// <returns></returns>
        public static decimal GetPriceInPips(this IBroker broker, decimal price, string market)
        {
            var pipInDecimals = broker.GetOnePipInDecimals(market);

            return price / pipInDecimals;
        }

        public static decimal GetPriceFromPips(this IBroker broker, decimal pips, string market)
        {
            var pipInDecimals = broker.GetOnePipInDecimals(market);

            return pips * pipInDecimals;
        }

        public static void RecalculateTrade(this IBroker broker, IBrokerAccount brokerAccount, TradeDetails trade)
        {
            var startTime = trade.OrderDateTime ?? trade.EntryDateTime;

            if (startTime == null)
            {
                trade.StopInPips = null;
                trade.InitialStopInPips = null;
                trade.InitialStop = null;
                trade.LimitInPips = null;
                trade.InitialLimitInPips = null;
                trade.InitialLimit = null;
                trade.RiskPercentOfBalance = null;
                trade.RiskAmount = null;
                trade.RMultiple = null;
                return;
            }

            // Initial stop is stop at entry point or order point
            if (trade.StopPrices.Count > 0)
            {
                DatePrice entryOrOrderStop = null;

                // Get entry or order stop price
                if (trade.EntryDateTime == null)
                {
                    entryOrOrderStop = trade.StopPrices[0];
                }
                else
                {
                    entryOrOrderStop = trade.StopPrices[0];
                    for (var i = 1; i < trade.StopPrices.Count; i++)
                    {
                        if (trade.StopPrices[i].Date > trade.EntryDateTime.Value) break;
                        entryOrOrderStop = trade.StopPrices[i];
                    }
                }

                // Update initial stop pips
                var price = trade.EntryPrice ?? trade.OrderPrice.Value;
                var stop = entryOrOrderStop;
                var stopInPips = Math.Abs(broker.GetPriceInPips(stop.Price.Value, trade.Market) -
                                          broker.GetPriceInPips(price, trade.Market));
                trade.InitialStopInPips = stopInPips;
                trade.InitialStop = entryOrOrderStop.Price;

                // Update current stop
                stop = trade.StopPrices.Last();
                stopInPips = Math.Abs(broker.GetPriceInPips(stop.Price.Value, trade.Market) -
                                      broker.GetPriceInPips(price, trade.Market));
                trade.StopInPips = stopInPips;
                trade.StopPrice = stop.Price;
            }
            else
            {
                trade.StopInPips = null;
                trade.StopPrice = null;
                trade.InitialStopInPips = null;
                trade.InitialStop = null;
            }

            // Update limit pips
            if (trade.LimitPrices.Count > 0)
            {
                DatePrice entryOrOrderLimit = null;

                // Get entry or order limit price
                if (trade.EntryDateTime == null)
                {
                    entryOrOrderLimit = trade.LimitPrices[0];
                }
                else
                {
                    entryOrOrderLimit = trade.LimitPrices[0];
                    for (var i = 1; i < trade.LimitPrices.Count; i++)
                    {
                        if (trade.LimitPrices[i].Date > trade.EntryDateTime.Value) break;
                        entryOrOrderLimit = trade.LimitPrices[i];
                    }
                }

                // Update initial limit
                var price = trade.EntryPrice ?? trade.OrderPrice.Value;
                var limit = entryOrOrderLimit;
                var limitInPips = Math.Abs(broker.GetPriceInPips(limit.Price.Value, trade.Market) -
                                           broker.GetPriceInPips(price, trade.Market));
                trade.InitialLimitInPips = limitInPips;
                trade.InitialLimit = entryOrOrderLimit.Price;

                // Update current limit
                limit = trade.LimitPrices.Last();
                limitInPips = Math.Abs(broker.GetPriceInPips(limit.Price.Value, trade.Market) -
                                      broker.GetPriceInPips(price, trade.Market));
                trade.LimitInPips = limitInPips;
                trade.LimitPrice = limit.Price;
            }
            else
            {
                trade.LimitInPips = null;
                trade.LimitPrice = null;
                trade.InitialLimitInPips = null;
                trade.InitialStop = null;
            }

            // Update risk
            if (trade.InitialStopInPips == null || trade.PricePerPip == null)
            {
                trade.RiskPercentOfBalance = null;
                trade.RiskAmount = null;
                trade.RiskPercentOfBalance = null;
            }
            else
            {
                trade.RiskAmount = trade.PricePerPip.Value * trade.InitialStopInPips.Value;

                var balance = brokerAccount.GetBalance(trade.StartDateTime);
                if (balance != 0.0M)
                {
                    trade.RiskPercentOfBalance = (trade.RiskAmount * 100M) / brokerAccount.GetBalance(startTime);
                }
                else
                {
                    trade.RiskPercentOfBalance = null;
                }
            }

            // Update RMultiple
            if (trade.RiskAmount != null && trade.RiskAmount.Value != 0M && trade.Profit != null)
            {
                trade.RMultiple = trade.Profit / trade.RiskAmount;
            }
            else if (trade.EntryPrice != null && trade.ClosePrice != null && trade.InitialStop != null)
            {
                var stopPrice = trade.InitialStop.Value;
                var risk = Math.Abs(stopPrice - trade.EntryPrice.Value);
                if (trade.TradeDirection == TradeDirection.Long)
                {
                    var gainOrLoss = Math.Abs(trade.ClosePrice.Value - trade.EntryPrice.Value);
                    trade.RMultiple = (gainOrLoss / risk) * (trade.ClosePrice.Value > trade.EntryPrice.Value ? 1 : -1);
                }
                else
                {
                    var gainOrLoss = Math.Abs(trade.ClosePrice.Value - trade.EntryPrice.Value);
                    trade.RMultiple = (gainOrLoss / risk) * (trade.ClosePrice.Value > trade.EntryPrice.Value ? -1 : 1);
                }
            }
            else
            {
                trade.RMultiple = null;
            }
        }
    }
}