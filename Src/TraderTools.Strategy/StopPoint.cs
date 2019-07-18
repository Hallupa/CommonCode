using System;
using TraderTools.Basics;

namespace TraderTools.Strategy
{
    public class StopPoint
    {
        public DateTime DateTime { get; }
        public Timeframe Timeframe { get; }

        public StopPoint(DateTime dateTime, Timeframe timeframe)
        {
            DateTime = dateTime;
            Timeframe = timeframe;
        }
    }
}