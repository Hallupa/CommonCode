using TraderTools.Basics;

namespace TraderTools.Simulation
{
    public class StopTrailIndicatorAttribute : UpdateTradeStrategyAttribute
    {
        private readonly Timeframe _timeframe;
        private readonly Indicator _indicator;
        private int _hashcode;

        public StopTrailIndicatorAttribute(Timeframe timeframe, Indicator indicator)
        {
            _timeframe = timeframe;
            _indicator = indicator;
            _hashcode = $"StopTrail {_timeframe} {_indicator}".GetHashCode();
        }

        public override void UpdateTrade(UpdateTradeParameters p)
        {
            StopHelper.TrailIndicator(p.Trade, _timeframe, _indicator, p.TimeframeCurrentCandles, p.TimeTicks);
        }

        public override int GetUpdateTradeStrategyHashCode()
        {
            return _hashcode;
        }
    }
}