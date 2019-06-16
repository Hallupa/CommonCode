using System;

namespace TraderTools.Basics.Extensions
{
    public static class TradeDetailsExtensions
    {
        public static void SimulateTrade(this TradeDetails trade, ICandle candle, out bool updated)
        {
            trade.SimulateTrade(candle.Low, candle.High, candle.Close, candle.OpenTime(), candle.CloseTime(), out updated);
        }

        public static void SimulateTrade(this TradeDetails trade, double candleLow, double candleHigh,
            double candleClose, DateTime candleOpenTime, DateTime candleCloseTime, out bool updated)
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

                    openTrade.SetClose(candleOpenTime, stopPrice, TradeCloseReason.HitStop);
                    updated = true;
                }
                else if (openTrade.StopPrice != null && openTrade.TradeDirection.Value == TradeDirection.Short && candleHigh >= (double)openTrade.StopPrice.Value)
                {
                    var stopPrice = Math.Max((decimal)candleLow, openTrade.StopPrice.Value);
                    openTrade.SetClose(candleOpenTime, stopPrice, TradeCloseReason.HitStop);
                    updated = true;
                }
                else if (openTrade.LimitPrice != null && openTrade.TradeDirection.Value == TradeDirection.Long && candleHigh >= (double)openTrade.LimitPrice.Value)
                {
                    var stopPrice = Math.Max((decimal)candleLow, openTrade.StopPrice.Value);
                    openTrade.SetClose(candleOpenTime, stopPrice, TradeCloseReason.HitLimit);
                    updated = true;
                }
                else if (openTrade.LimitPrice != null && openTrade.TradeDirection.Value == TradeDirection.Short && candleLow <= (double)openTrade.LimitPrice.Value)
                {
                    var stopPrice = Math.Min((decimal)candleHigh, openTrade.StopPrice.Value);
                    openTrade.SetClose(candleOpenTime, stopPrice, TradeCloseReason.HitLimit);
                    updated = true;
                }
            }

            // Try to fill order
            if (trade.EntryPrice == null && trade.OrderPrice != null && ((candleOpenTime <= trade.OrderDateTime && candleCloseTime >= trade.OrderDateTime) || candleOpenTime >= trade.OrderDateTime))
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
                                    updated = updated | SetEntry(order, orderPrice, candleCloseTime);
                                }
                                // - - - - - - - -
                                //                ___
                                //                | |
                                //                | |
                                //                ---
                                else if (direction == TradeDirection.Long && candleHigh <= orderPrice)
                                {
                                    updated = updated | SetEntry(order, candleHigh, candleCloseTime);
                                }
                                //                ___
                                //                | |
                                //                | |
                                //                ---
                                // - - - - - - - -
                                else if (direction == TradeDirection.Short && candleLow >= orderPrice)
                                {
                                    updated = updated | SetEntry(order, candleLow, candleCloseTime);
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
                                    updated = updated | SetEntry(order, orderPrice, candleCloseTime);
                                }
                                //                ___
                                //                | |
                                //                | |
                                //                ---
                                // - - - - - - - -
                                else if (direction == TradeDirection.Long && candleLow >= orderPrice)
                                {
                                    updated = updated | SetEntry(order, candleLow, candleCloseTime);
                                }
                                // - - - - - - - -
                                //                ___
                                //                | |
                                //                | |
                                //                ---
                                else if (direction == TradeDirection.Short && candleHigh <= orderPrice)
                                {
                                    updated = updated | SetEntry(order, candleHigh, candleCloseTime);
                                }

                                break;
                            }
                    }
                }
                else if (order.OrderPrice == null)
                {
                    order.SetEntry(candleOpenTime, (decimal)candleClose);
                    updated = true;
                }

                if (trade.EntryPrice == null && order.OrderExpireTime != null && candleCloseTime >= order.OrderExpireTime)
                {
                    order.SetExpired(candleOpenTime);
                    updated = true;
                }
            }

            updated = false;
        }

        private static bool SetEntry(TradeDetails trade, double price, DateTime dateTime)
        {
            trade.SetEntry(dateTime, (decimal)price);
            return true;
        }
    }
}