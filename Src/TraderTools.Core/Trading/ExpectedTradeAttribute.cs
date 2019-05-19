using System;
using TraderTools.Basics;
using TraderTools.Core.Services;

namespace TraderTools.Core.Trading
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class StrategyDetailsAttribute : Attribute
    {
        public StrategyDetailsAttribute(string tagName, bool groupDayTrades = false)
        {
            TagName = tagName;
            GroupDayTrades = groupDayTrades;
        }
        public string TagName { get; }
        public bool GroupDayTrades { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ExpectedTradeAttribute : Attribute
    {
        public ExpectedTradeAttribute(
            string market, int year, int month, int day, int orderHourUtc,
            Timeframe timeframe, int pips, double orderPrice, TradeDirection direction, double triggerCandleClose)
        {
            Market = market;
            Year = year;
            Month = month;
            Day = day;
            OrderHourUtc = orderHourUtc;
            Timeframe = timeframe;
            Pips = pips;
            OrderPrice = orderPrice;
            Direction = direction;
            TriggerCandleClose = triggerCandleClose;
        }

        public string Market { get; }
        public int Year { get; }
        public int Month { get; }
        public int Day { get; }
        public int OrderHourUtc { get; }
        public Timeframe Timeframe { get; }
        public int Pips { get; }
        public double OrderPrice { get; }
        public TradeDirection Direction { get; }
        public double TriggerCandleClose { get; }
    }
}