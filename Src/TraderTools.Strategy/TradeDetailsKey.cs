using System;
using TraderTools.Basics;

namespace TraderTools.Strategy
{
    public struct TradeDetailsKey
    {
        public TradeDetailsKey(DateTime orderTime, string market, Timeframe timeframe, decimal orderPrice,
            decimal? initialStop, decimal? initialLimit, int custom1, int custom2, int custom3, int custom4,
            int custom5, TradeDirection direction, DateTime expiryDate)
        {
            OrderTime = orderTime;
            Market = market;
            Timeframe = timeframe;
            OrderPrice = orderPrice;
            InitialStop = initialStop;
            InitialLimit = initialLimit;
            Custom1 = custom1;
            Custom2 = custom2;
            Custom3 = custom3;
            Custom4 = custom4;
            Custom5 = custom5;
            Direction = direction;
            ExpiryDate = expiryDate;
        }

        public DateTime OrderTime { get; set; }
        public string Market { get; set; }
        public Timeframe Timeframe { get; set; }
        public decimal OrderPrice { get; set; }
        public decimal? InitialStop { get; set; }
        public decimal? InitialLimit { get; set; }
        public int Custom1 { get; set; }
        public int Custom2 { get; set; }
        public int Custom3 { get; set; }
        public int Custom4 { get; set; }
        public int Custom5 { get; set; }
        public TradeDirection Direction { get; set; }
        public DateTime ExpiryDate { get; set; }

        public override int GetHashCode()
        {
            var ret = OrderTime.GetHashCode() 
                      ^ Market.GetHashCode() 
                      ^ Timeframe.GetHashCode()
                      ^ OrderPrice.GetHashCode()
                      ^ InitialStop.GetHashCode()
                      ^ ExpiryDate.GetHashCode()
                      ^ Custom1.GetHashCode()
                      ^ Custom2.GetHashCode()
                      ^ Custom3.GetHashCode()
                      ^ Custom4.GetHashCode()
                      ^ Custom5.GetHashCode()
                      ^ Direction.GetHashCode();

            if (InitialLimit != null)
            {
                ret = ret ^ InitialLimit.GetHashCode();
            }

            return ret;
        }

        public override bool Equals(object ob)
        {
            if (ob is TradeDetailsKey t)
            {
                return t.OrderTime == OrderTime &&
                       t.Timeframe == Timeframe &&
                       t.Market == Market &&
                       t.OrderPrice == OrderPrice &&
                       t.InitialStop == InitialStop &&
                       t.InitialLimit == InitialLimit &&
                       t.ExpiryDate == ExpiryDate &&
                       t.Custom1 == Custom1 &&
                       t.Custom2 == Custom2 &&
                       t.Custom3 == Custom3 &&
                       t.Custom4 == Custom4 &&
                       t.Custom5 == Custom5 &&
                       t.Direction == Direction;
            }
            else
            {
                return false;
            }
        }

        public static TradeDetailsKey Create(Trade trade)
        {
            return new TradeDetailsKey
            {
                OrderTime = trade.OrderDateTime.Value,
                Market = trade.Market,
                Timeframe = trade.Timeframe.Value,
                InitialStop = trade.InitialStop,
                InitialLimit = trade.InitialLimit,
                OrderPrice = trade.OrderPrice ?? decimal.MinValue,
                ExpiryDate = trade.OrderExpireTime ?? DateTime.MinValue,
                Custom1 = trade.Custom1,
                Custom2 = trade.Custom2,
                Custom3 = trade.Custom3,
                Custom4 = trade.Custom4,
                Custom5 = trade.Custom5,
                Direction = trade.TradeDirection.Value

            };
        }
    }
}