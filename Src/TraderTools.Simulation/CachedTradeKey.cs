using TraderTools.Basics;

namespace TraderTools.Simulation
{
    public struct CachedTradeKey
    {
        public float Price { get; set; } // Either entry price or order price based on whether OrderType is set
        public long DateTimeTicks { get; set; }
        public byte TradeDirection { get; set; }
        public long OrderExpireTimeTicks { get; set; }
        public float LimitPrice { get; set; }
        public float StopPrice { get; set; }
        public byte OrderType { get; set; } // If this  is set, it is an entry order rather than market order

        public override int GetHashCode()
        {
            var hashCode = DateTimeTicks.GetHashCode() ^ Price.GetHashCode();
            return hashCode;
        }

        public static CachedTradeKey Create(Trade t)
        {
            return new CachedTradeKey
            {
                Price = t.OrderType != null ? (float)t.OrderPrice.Value : (float)t.EntryPrice.Value,
                DateTimeTicks = t.OrderType != null ? t.OrderDateTime.Value.Ticks : t.EntryDateTime.Value.Ticks,
                StopPrice = t.StopPrice != null ? (float)t.StopPrice : -1,
                LimitPrice = t.LimitPrice != null ? (float)t.LimitPrice : -1,
                OrderType = t.OrderType != null ? (byte)t.OrderType : (byte)0,
                OrderExpireTimeTicks = t.OrderExpireTime?.Ticks ?? -1,
                TradeDirection = (byte)t.TradeDirection.Value
            };
        }

        public static CachedTradeKey Create(CachedTradeResult t)
        {
            return new CachedTradeKey
            {
                Price = !t.OrderPrice.Equals(-1) ? t.OrderPrice : t.EntryPrice,
                DateTimeTicks = !t.OrderDateTimeTicks.Equals(-1) ? t.OrderDateTimeTicks : t.EntryDateTimeTicks,
                StopPrice = t.StopPrice,
                LimitPrice = t.LimitPrice,
                OrderType = t.OrderType,
                OrderExpireTimeTicks = t.OrderExpireTimeTicks,
                TradeDirection = t.TradeDirection
            };
        }
    }
}