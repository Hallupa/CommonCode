using System;
using TraderTools.Basics;

namespace TraderTools.Core.Trading
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class NotExpectedTradeAttribute : Attribute
    {
        public NotExpectedTradeAttribute(
            string market, int year, int month, int day, int orderHourUtc,
            Timeframe timeframe)
        {
            Market = market;
            Year = year;
            Month = month;
            Day = day;
            OrderHourUtc = orderHourUtc;
            Timeframe = timeframe;
        }

        public string Market { get; }
        public int Year { get; }
        public int Month { get; }
        public int Day { get; }
        public int OrderHourUtc { get; }
        public Timeframe Timeframe { get; }
    }
}