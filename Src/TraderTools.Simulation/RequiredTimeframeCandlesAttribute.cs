using System;
using TraderTools.Basics;

namespace TraderTools.Simulation
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class RequiredTimeframeCandlesAttribute : Attribute
    {
        public RequiredTimeframeCandlesAttribute(Timeframe timeframe, params Indicator[] indicators)
        {
            Timeframe = timeframe;
            Indicators = indicators;
        }

        public Timeframe Timeframe { get; }
        public Indicator[] Indicators { get; }
    }
}