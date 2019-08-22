using System.Collections.Generic;
using System.Windows.Media;
using TraderTools.Basics;
using TraderTools.Core.Services;

namespace TraderTools.Core.Trading
{
    public interface ITimeframeColour
    {
        Color? GetColour(List<CandleAndIndicators> candles, int index, string market, Timeframe timeframe);
    }
}