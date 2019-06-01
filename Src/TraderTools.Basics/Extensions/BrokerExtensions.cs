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

        public static void UpdateTradeStopLimitPips(this IBroker broker, TradeDetails trade)
        {
            // Update stop pips
            if ((trade.EntryPrice == null && trade.OrderPrice == null) || trade.StopPrices.Count == 0)
            {
                trade.StopInPips = null;
                trade.InitialStopInPips = null;
            }
            else
            {
                var price = trade.OrderPrice ?? trade.EntryPrice.Value;
                var stop = trade.GetStopPrices().First();
                var stopInPips = Math.Abs(broker.GetPriceInPips(stop.Price.Value, trade.Market) -
                                          broker.GetPriceInPips(price, trade.Market));
                trade.InitialStopInPips = stopInPips;

                stop = trade.GetStopPrices().Last();
                stopInPips = Math.Abs(broker.GetPriceInPips(stop.Price.Value, trade.Market) -
                                      broker.GetPriceInPips(price, trade.Market));
                trade.StopInPips = stopInPips;
            }

            // Update limit pips
            if ((trade.EntryPrice == null && trade.OrderPrice == null) || trade.LimitPrices.Count == 0)
            {
                trade.LimitInPips = null;
                trade.InitialLimitInPips = null;
            }
            else
            {
                var price = trade.OrderPrice ?? trade.EntryPrice.Value;
                var limit = trade.GetLimitPrices().First();
                var limitInPips = Math.Abs(broker.GetPriceInPips(limit.Price.Value, trade.Market) -
                                           broker.GetPriceInPips(price, trade.Market));
                trade.InitialLimitInPips = limitInPips;

                limit = trade.GetLimitPrices().Last();
                limitInPips = Math.Abs(broker.GetPriceInPips(limit.Price.Value, trade.Market) -
                                      broker.GetPriceInPips(price, trade.Market));
                trade.LimitInPips = limitInPips;
            }
        }
    }
}