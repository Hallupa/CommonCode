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
            if (trade.EntryPrice == null && trade.OrderPrice != null)
            {
                var order = trade;

                if (order.OrderPrice != null)
                {
                    if (order.TradeDirection == TradeDirection.Long && candleLow <= (double)order.OrderPrice)
                    {
                        var entryPrice = Math.Min((decimal)candleHigh, order.OrderPrice.Value);
                        order.SetEntry(candleOpenTime, entryPrice);
                        updated = true;
                    }
                    else if (order.TradeDirection == TradeDirection.Short && candleHigh >= (double)order.OrderPrice)
                    {
                        var entryPrice = Math.Max((decimal)candleLow, order.OrderPrice.Value);
                        order.SetEntry(candleOpenTime, entryPrice);
                        updated = true;
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
    }
}