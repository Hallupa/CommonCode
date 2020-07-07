using System;
using TraderTools.Basics;

namespace TraderTools.Core.Trading
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ExpectedTradesFileAttribute : Attribute
    {
        public ExpectedTradesFileAttribute(string path)
        {
            Path = path;
        }
        public string Path { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ExpectedTradeAttribute : Attribute
    {
        public ExpectedTradeAttribute(string market, string openTimeUTC, string closeTimeUTC, decimal entryPrice, decimal closePrice, TradeDirection direction)
        {
            Market = market;
            OpenTimeUTC = DateTime.Parse(openTimeUTC);
            CloseTimeUTC = DateTime.Parse(closeTimeUTC);
            EntryPrice = entryPrice;
            ClosePrice = closePrice;
            Direction = direction;
        }

        public decimal ClosePrice { get; }

        public TradeDirection Direction { get; }

        public decimal EntryPrice { get; }

        public DateTime CloseTimeUTC { get; }

        public DateTime OpenTimeUTC { get; }

        public string Market { get; }
    }
}