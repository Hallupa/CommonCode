using System;

namespace TraderTools.Basics.Extensions
{
    public static class TradeDetailsExtensions
    {
        public static void SimulateTrade(this TradeDetails trade, ICandle candle, out bool updated)
        {
            trade.SimulateTrade(candle.Low, candle.High, candle.Close, candle.OpenTimeTicks, candle.CloseTimeTicks, out updated);
        }

        public static void SimulateTrade(this TradeDetails trade, double candleLow, double candleHigh,
            double candleClose, long candleOpenTimeTicks, long candleCloseTimeTicks, out bool updated)
        {
            updated = false;

            if (trade.CloseDateTime != null)
            {
                updated = false;
                return;
            }

            // Try to close trade
            if (trade.EntryPrice != null && trade.CloseReason == null)
            {
                var openTrade = trade;

                if (openTrade.StopPrice != null && openTrade.TradeDirection.Value == TradeDirection.Long && candleLow <= (double)openTrade.StopPrice.Value)
                {
                    var stopPrice = Math.Min((decimal)candleHigh, openTrade.StopPrice.Value);

                    openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), stopPrice, TradeCloseReason.HitStop);
                    updated = true;
                }
                else if (openTrade.StopPrice != null && openTrade.TradeDirection.Value == TradeDirection.Short && candleHigh >= (double)openTrade.StopPrice.Value)
                {
                    var stopPrice = Math.Max((decimal)candleLow, openTrade.StopPrice.Value);
                    openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), stopPrice, TradeCloseReason.HitStop);
                    updated = true;
                }
                else if (openTrade.LimitPrice != null && openTrade.TradeDirection.Value == TradeDirection.Long && candleHigh >= (double)openTrade.LimitPrice.Value)
                {
                    var limitPrice = Math.Max((decimal)candleLow, openTrade.LimitPrice.Value);
                    openTrade.SetClose(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), limitPrice, TradeCloseReason.HitLimit);
                    updated = true;
                }
                else if (openTrade.LimitPrice != null && openTrade.TradeDirection.Value == TradeDirection.Short && candleLow <= (double)openTrade.LimitPrice.Value)
                {
                    var limitPrice = Math.Min((decimal)candleHigh, openTrade.LimitPrice.Value);
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
                                if (candleLow <= orderPrice && candleHigh >= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)orderPrice, trade.OrderAmount.Value);
                                    updated = true;
                                }
                                // - - - - - - - -
                                //                ___
                                //                | |
                                //                | |
                                //                ---
                                else if (direction == TradeDirection.Long && candleHigh <= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)candleHigh, trade.OrderAmount.Value);
                                    updated = true;
                                }
                                //                ___
                                //                | |
                                //                | |
                                //                ---
                                // - - - - - - - -
                                else if (direction == TradeDirection.Short && candleLow >= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)candleLow, trade.OrderAmount.Value);
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
                                if (candleLow <= orderPrice && candleHigh >= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)orderPrice, trade.OrderAmount.Value);
                                    updated = true;
                                }
                                //                ___
                                //                | |
                                //                | |
                                //                ---
                                // - - - - - - - -
                                else if (direction == TradeDirection.Long && candleLow >= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)candleLow, trade.OrderAmount.Value);
                                    updated = true;
                                }
                                // - - - - - - - -
                                //                ___
                                //                | |
                                //                | |
                                //                ---
                                else if (direction == TradeDirection.Short && candleHigh <= orderPrice)
                                {
                                    order.SetEntry(new DateTime(candleCloseTimeTicks, DateTimeKind.Utc), (decimal)candleHigh, trade.OrderAmount.Value);
                                    updated = true;
                                }

                                break;
                            }
                    }
                }
                else if (order.OrderPrice == null)
                {
                    order.SetEntry(new DateTime(candleOpenTimeTicks, DateTimeKind.Utc), (decimal)candleClose, trade.OrderAmount.Value);
                    updated = true;
                }

                if (trade.EntryPrice == null && order.OrderExpireTime != null && candleCloseTimeTicks >= order.OrderExpireTime.Value.Ticks)
                {
                    order.SetExpired(new DateTime(candleOpenTimeTicks, DateTimeKind.Utc));
                    updated = true;
                }
            }
        }
    }
}