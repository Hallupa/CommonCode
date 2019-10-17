using System;
using TraderTools.Basics.Helpers;

namespace TraderTools.Basics.Extensions
{
    public static class TradeExtensions
    {
        public static decimal GetTradeProfit(this Trade trade, DateTime dateTimeUTC, Timeframe candlesTimeframe,
            IBrokersCandlesService candlesService, MarketDetails marketDetails, IBroker broker, bool updateCandles)
        {
            if (trade.EntryPrice == null || trade.EntryDateTime == null)
            {
                return 0M;
            }

            if (trade.CloseDateTime != null && trade.CloseDateTime.Value <= dateTimeUTC)
            {
                return trade.Profit ?? 0M;
            }

            if (trade.EntryDateTime >= dateTimeUTC)
            {
                return 0M;
            }

            var latestCandle = candlesService.GetLastClosedCandle(
                trade.Market, broker, candlesTimeframe, dateTimeUTC, updateCandles);

            if (latestCandle != null && trade.PricePerPip != null)
            {
                var closePriceToUse = trade.TradeDirection == TradeDirection.Long
                    ? (decimal)latestCandle.Value.CloseBid
                    : (decimal)latestCandle.Value.CloseAsk;
                var profitPips = PipsHelper.GetPriceInPips(trade.TradeDirection == TradeDirection.Long ? closePriceToUse - trade.EntryPrice.Value : trade.EntryPrice.Value - closePriceToUse, marketDetails);
                var totalRunningTime = (DateTime.UtcNow - trade.EntryDateTime.Value).TotalDays;
                var runningTime = (trade.EntryDateTime.Value - dateTimeUTC).TotalDays;

                var tradeProfit = trade.PricePerPip.Value * profitPips +
                                  (!totalRunningTime.Equals(0.0) && trade.Rollover != null
                                      ? trade.Rollover.Value * (decimal)(runningTime / totalRunningTime)
                                      : 0M);

                return tradeProfit;
            }

            return 0M;
        }

        public static void SimulateTrade(this Trade trade, Candle candle, out bool updated)
        {
            SimulateTrade(trade, candle.LowBid, candle.HighBid, candle.CloseBid,
                candle.LowAsk, candle.HighAsk, candle.CloseAsk,
                candle.OpenTimeTicks, candle.CloseTimeTicks, out updated);
        }

        public static void SimulateTrade(this Trade trade,
			float candleBidLow, float candleBidHigh, float candleBidClose,
			float candleAskLow, float candleAskHigh, float candleAskClose,
			long candleOpenTimeTicks, long candleCloseTimeTicks, out bool updated)
        {
            updated = false;

            if (trade.CloseDateTime != null) return;
            if (trade.TradeDirection == null) return;

			// Ask = buy price, Bid = sell price
			
            // Try to close trade
            if (trade.EntryPrice != null && trade.CloseReason == null)
            {
                ProcessClose(trade, candleBidLow, candleBidHigh, candleAskLow, candleAskHigh, candleCloseTimeTicks, ref updated);
            }



            // Process market orders
            if (trade.EntryPrice == null && trade.OrderPrice == null
                                         && candleCloseTimeTicks >= trade.OrderDateTime.Value.Ticks)
            {
                ProcessMarketEntry(trade, candleBidClose, candleAskClose, candleCloseTimeTicks, ref updated);
            }

            // Try to fill order
            if (trade.EntryPrice == null && trade.OrderPrice != null 
                                         && ((candleOpenTimeTicks <= trade.OrderDateTime.Value.Ticks
                                            && candleCloseTimeTicks >= trade.OrderDateTime.Value.Ticks) || candleOpenTimeTicks >= trade.OrderDateTime.Value.Ticks))
            {
                ProcessOrder(trade, candleBidLow, candleBidHigh, candleBidClose, candleAskLow, candleAskHigh, candleAskClose, candleCloseTimeTicks, ref updated);
            }
        }

        private static void ProcessClose(Trade trade, float candleBidLow, float candleBidHigh, float candleAskLow,
            float candleAskHigh, long candleCloseTimeTicks, ref bool updated)
        {
            var openTrade = trade;

            if (openTrade.StopPrice != null && openTrade.TradeDirection.Value == TradeDirection.Long &&
                candleBidLow <= (double) openTrade.StopPrice.Value)
            {
                var stopPrice = Math.Min((decimal) candleBidHigh, openTrade.StopPrice.Value);
                openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), stopPrice, TradeCloseReason.HitStop);
                updated = true;
            }
            else if (openTrade.StopPrice != null && openTrade.TradeDirection.Value == TradeDirection.Short &&
                     candleAskHigh >= (double) openTrade.StopPrice.Value)
            {
                var stopPrice = Math.Max((decimal) candleAskLow, openTrade.StopPrice.Value);
                openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), stopPrice, TradeCloseReason.HitStop);
                updated = true;
            }
            else if (openTrade.LimitPrice != null && openTrade.TradeDirection.Value == TradeDirection.Long &&
                     candleBidHigh >= (double) openTrade.LimitPrice.Value)
            {
                var limitPrice = Math.Max((decimal) candleBidLow, openTrade.LimitPrice.Value);
                openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), limitPrice, TradeCloseReason.HitLimit);
                updated = true;
            }
            else if (openTrade.LimitPrice != null && openTrade.TradeDirection.Value == TradeDirection.Short &&
                     candleAskLow <= (double) openTrade.LimitPrice.Value)
            {
                var limitPrice = Math.Min((decimal) candleAskHigh, openTrade.LimitPrice.Value);
                openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), limitPrice, TradeCloseReason.HitLimit);
                updated = true;
            }
        }

        private static void ProcessMarketEntry(Trade trade, float candleBidClose, float candleAskClose,
            long candleCloseTimeTicks, ref bool updated)
        {
            if (trade.OrderDateTime == null ||
                (trade.OrderDateTime != null && trade.OrderDateTime.Value.Ticks <= candleCloseTimeTicks))
            {
                if (trade.TradeDirection == TradeDirection.Long)
                {
                    trade.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal) candleAskClose, trade.OrderAmount.Value);
                    updated = true;
                }
                else
                {
                    trade.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal) candleBidClose, trade.OrderAmount.Value);
                    updated = true;
                }
            }
        }

        private static void ProcessOrder(Trade trade, float candleBidLow, float candleBidHigh, float candleBidClose,
            float candleAskLow, float candleAskHigh, float candleAskClose, long candleCloseTimeTicks, ref bool updated)
        {
            var order = trade;

            if (order.OrderPrice != null)
            {
                var orderType = order.OrderType ?? OrderType.LimitEntry;
                var direction = order.TradeDirection;
                var orderPrice = (double) order.OrderPrice;

                switch (orderType)
                {
                    case OrderType.LimitEntry: // Buy below current market price or sell above current market price
                    {
                        //                ___
                        //                | |
                        //  - - - - - - - | |
                        //                | |
                        //                ---
                        if (direction == TradeDirection.Long && candleAskLow <= orderPrice && candleAskHigh >= orderPrice)
                        {
                            order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal) orderPrice, trade.OrderAmount.Value);
                            updated = true;
                        }
                        // - - - - - - - -
                        //                ___
                        //                | |
                        //                | |
                        //                ---
                        else if (direction == TradeDirection.Long && candleAskHigh <= orderPrice)
                        {
                            order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)candleAskHigh, trade.OrderAmount.Value);
                            updated = true;
                        }
                        //                ___
                        //                | |
                        //  - - - - - - - | |
                        //                | |
                        //                ---
                        else if (direction == TradeDirection.Short && candleBidLow <= orderPrice && candleBidHigh >= orderPrice)
                        {
                            order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal) orderPrice, trade.OrderAmount.Value);
                            updated = true;
                        }
                        //                ___
                        //                | |
                        //                | |
                        //                ---
                        // - - - - - - - -
                        else if (direction == TradeDirection.Short && candleBidLow >= orderPrice)
                        {
                            order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal) candleBidLow, trade.OrderAmount.Value);
                            updated = true;
                        }

                        break;
                    }

                    case OrderType.StopEntry: // Buy above current price or sell below current market price
                    {
                        //                ___
                        //                | |
                        //  - - - - - - - | |
                        //                | |
                        //                ---
                        if (order.TradeDirection == TradeDirection.Long && candleAskLow <= orderPrice && candleAskHigh >= orderPrice)
                        {
                            order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal) orderPrice, trade.OrderAmount.Value);
                            updated = true;
                        }
                        //                ___
                        //                | |
                        //                | |
                        //                ---
                        // - - - - - - - -
                        else if (direction == TradeDirection.Long && candleAskLow >= orderPrice)
                        {
                            order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)candleAskLow, trade.OrderAmount.Value);
                            updated = true;
                        }
                        //                ___
                        //                | |
                        //  - - - - - - - | |
                        //                | |
                        //                ---
                        else if (order.TradeDirection == TradeDirection.Short && candleBidLow <= orderPrice && candleBidHigh >= orderPrice)
                        {
                            order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal) orderPrice, trade.OrderAmount.Value);
                            updated = true;
                        }
                        // - - - - - - - -
                        //                ___
                        //                | |
                        //                | |
                        //                ---
                        else if (direction == TradeDirection.Short && candleBidHigh <= orderPrice)
                        {
                            order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal) candleBidHigh, trade.OrderAmount.Value);
                            updated = true;
                        }

                        break;
                    }
                }
            }

            if (trade.EntryPrice == null && order.OrderExpireTime != null &&
                candleCloseTimeTicks >= order.OrderExpireTime.Value.Ticks)
            {
                order.SetExpired(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc));
                updated = true;
            }
        }
    }
}