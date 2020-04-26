using System;
using TraderTools.Basics.Extensions;

namespace TraderTools.Basics
{
    public class TradeFactory
    {
        public static Trade CreateOrder(string broker, decimal entryOrder, Candle latestCandle,
            TradeDirection direction, decimal amount, string market, DateTime? orderExpireTime,
            decimal? stop, decimal? limit, CalculateOptions calculateOptions = CalculateOptions.Default)
        {
            var orderDateTime = latestCandle.CloseTime();

            var trade = new Trade { CalculateOptions = calculateOptions };

            trade.SetOrder(orderDateTime, entryOrder, market, direction, amount, orderExpireTime);
            if (stop != null) trade.AddStopPrice(orderDateTime, stop.Value);
            if (limit != null) trade.AddLimitPrice(orderDateTime, limit.Value);
            trade.Broker = broker;

            if (direction == Basics.TradeDirection.Long)
            {
                trade.OrderType = (float)entryOrder <= latestCandle.CloseAsk ? Basics.OrderType.LimitEntry : Basics.OrderType.StopEntry;
            }
            else
            {
                trade.OrderType = (float)entryOrder <= latestCandle.CloseBid ? Basics.OrderType.StopEntry : Basics.OrderType.LimitEntry;
            }
            return trade;
        }

        public static Trade CreateMarketEntry(string broker, decimal entryPrice, DateTime entryTime,
            TradeDirection direction, decimal amount, string market,
            decimal? stop, decimal? limit, ITradeDetailsAutoCalculatorService tradeCalculatorService,
            Timeframe? timeframe = null, string strategies = null, string comments = null, bool alert = false, CalculateOptions calculateOptions = CalculateOptions.Default, TradeUpdateMode updateMode = TradeUpdateMode.Default)
        {
            var trade = new Trade { Broker = broker, CalculateOptions = calculateOptions };

            if (stop != null) trade.AddStopPrice(entryTime, stop.Value);
            if (limit != null) trade.AddLimitPrice(entryTime, limit.Value);
            trade.Market = market;
            trade.TradeDirection = direction;
            trade.EntryPrice = entryPrice;
            trade.EntryDateTime = entryTime;
            trade.EntryQuantity = amount;
            trade.Timeframe = timeframe;
            trade.Alert = alert;
            trade.Comments = comments;
            trade.Strategies = strategies;
            trade.UpdateMode = updateMode;
            return trade;
        }
    }
}