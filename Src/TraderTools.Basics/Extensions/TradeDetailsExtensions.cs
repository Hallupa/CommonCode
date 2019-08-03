using System;

namespace TraderTools.Basics.Extensions
{
    public static class TradeDetailsExtensions
    {
        public static void SimulateTrade(this Trade trade, ICandle candle, out bool updated)
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

            if (trade.CloseDateTime != null)
            {
                updated = false;
                return;
            }

            if (trade.TradeDirection == null)
            {
                updated = false;
                return;
            }

			// Ask = buy price, Bid = sell price
			
            // Try to close trade
            if (trade.EntryPrice != null && trade.CloseReason == null)
            {
                var openTrade = trade;

                if (openTrade.StopPrice != null && openTrade.TradeDirection.Value == TradeDirection.Long && candleBidLow <= (double)openTrade.StopPrice.Value)
                {
                    var stopPrice = Math.Min((decimal)candleBidHigh, openTrade.StopPrice.Value);

                    openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), stopPrice, TradeCloseReason.HitStop);
                    updated = true;
                }
                else if (openTrade.StopPrice != null && openTrade.TradeDirection.Value == TradeDirection.Short && candleAskHigh >= (double)openTrade.StopPrice.Value)
                {
                    var stopPrice = Math.Max((decimal)candleAskLow, openTrade.StopPrice.Value);
                    openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), stopPrice, TradeCloseReason.HitStop);
                    updated = true;
                }
                else if (openTrade.LimitPrice != null && openTrade.TradeDirection.Value == TradeDirection.Long && candleBidHigh >= (double)openTrade.LimitPrice.Value)
                {
                    var limitPrice = Math.Max((decimal)candleBidLow, openTrade.LimitPrice.Value);
                    openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), limitPrice, TradeCloseReason.HitLimit);
                    updated = true;
                }
                else if (openTrade.LimitPrice != null && openTrade.TradeDirection.Value == TradeDirection.Short && candleAskLow <= (double)openTrade.LimitPrice.Value)
                {
                    var limitPrice = Math.Min((decimal)candleAskHigh, openTrade.LimitPrice.Value);
                    openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), limitPrice, TradeCloseReason.HitLimit);
                    updated = true;
                }
            }

            // Try to fill order
            if (trade.EntryPrice == null && trade.OrderPrice != null 
                                         && ((candleOpenTimeTicks <= trade.OrderDateTime.Value.Ticks
                                            && candleCloseTimeTicks >= trade.OrderDateTime.Value.Ticks) || candleOpenTimeTicks >= trade.OrderDateTime.Value.Ticks))
            {
                var order = trade;

                if (order.OrderPrice != null)
                {
                    var orderType = order.OrderType ?? OrderType.LimitEntry;
                    var direction = order.TradeDirection;
                    var orderPrice = (double)order.OrderPrice;

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
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)orderPrice, trade.OrderAmount.Value);
                                    updated = true;
                                }
								//                ___
                                //                | |
                                //  - - - - - - - | |
                                //                | |
                                //                ---
                                else if (direction == TradeDirection.Short && candleBidLow <= orderPrice && candleBidHigh >= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)orderPrice, trade.OrderAmount.Value);
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
                                //                | |
                                //                ---
                                // - - - - - - - -
                                else if (direction == TradeDirection.Short && candleBidLow >= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)candleBidLow, trade.OrderAmount.Value);
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
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)orderPrice, trade.OrderAmount.Value);
                                    updated = true;
                                }
                                //                ___
                                //                | |
                                //  - - - - - - - | |
                                //                | |
                                //                ---
                                else if(order.TradeDirection == TradeDirection.Short && candleBidLow <= orderPrice && candleBidHigh >= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)orderPrice, trade.OrderAmount.Value);
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
                                // - - - - - - - -
                                //                ___
                                //                | |
                                //                | |
                                //                ---
                                else if (direction == TradeDirection.Short && candleBidHigh <= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)candleBidHigh, trade.OrderAmount.Value);
                                    updated = true;
                                }

                                break;
                            }
                    }
                }
                else if (order.OrderPrice == null)
                {
                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), 
                        (decimal)(order.TradeDirection == TradeDirection.Long ? candleAskClose : candleBidClose), trade.OrderAmount.Value);
                    updated = true;
                }

                if (trade.EntryPrice == null && order.OrderExpireTime != null && candleCloseTimeTicks >= order.OrderExpireTime.Value.Ticks)
                {
                    order.SetExpired(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc));
                    updated = true;
                }
            }
        }
    }
}