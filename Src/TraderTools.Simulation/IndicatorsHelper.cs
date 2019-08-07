using System;
using System.Collections.Generic;
using TraderTools.Basics;
using TraderTools.Indicators;

namespace TraderTools.Simulation
{
    public static class IndicatorsHelper
    {
        public static TimeframeLookup<List<(Indicator, IIndicator)>> CreateIndicators(TimeframeLookup<Indicator[]> timeframeIndicators)
        {
            var ret = new TimeframeLookup<List<(Indicator, IIndicator)>>();

            foreach (var timeframeIndicator in timeframeIndicators)
            {
                if (timeframeIndicator.Value == null) continue;

                ret[timeframeIndicator.Key] = new List<(Indicator, IIndicator)>();

                foreach (var indicator in timeframeIndicator.Value)
                {
                    IIndicator indicatorInstance = null;
                    switch (indicator)
                    {
                        case Indicator.EMA8:
                            indicatorInstance = new ExponentialMovingAverage(8);
                            break;
                        case Indicator.EMA21:
                            indicatorInstance = new ExponentialMovingAverage(21);
                            break;
                        case Indicator.EMA25:
                            indicatorInstance = new ExponentialMovingAverage(25);
                            break;
                        case Indicator.EMA50:
                            indicatorInstance = new ExponentialMovingAverage(50);
                            break;
                        case Indicator.MACD:
                            indicatorInstance = new MovingAverageConvergenceDivergence();
                            break;
                        case Indicator.MACDSignal:
                            indicatorInstance = new MovingAverageConvergenceDivergenceSignal();
                            break;
                        case Indicator.Range20:
                            indicatorInstance = new PriceRange(20);
                            break;
                        case Indicator.Range25:
                            indicatorInstance = new PriceRange(25);
                            break;
                        case Indicator.Range30:
                            indicatorInstance = new PriceRange(30);
                            break;
                        case Indicator.Range40:
                            indicatorInstance = new PriceRange(40);
                            break;
                        case Indicator.Range50:
                            indicatorInstance = new PriceRange(50);
                            break;
                        case Indicator.Range60:
                            indicatorInstance = new PriceRange(60);
                            break;
                        case Indicator.ATR:
                            indicatorInstance = new AverageTrueRange();
                            break;
                        default:
                            throw new ApplicationException("Indicator not found");
                    }

                    ret[timeframeIndicator.Key].Add((indicator, indicatorInstance));
                }
            }

            return ret;
        }
    }
}
