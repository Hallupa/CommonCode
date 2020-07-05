using TraderTools.Basics;

namespace TraderTools.Indicators
{
    public class AverageDayRange : IIndicator
    {
        public AverageDayRange()
        {
            MovingAverage = new WilderMovingAverage();
        }

        public WilderMovingAverage MovingAverage { get; }

        /// <summary>
        /// Whether the indicator is set.
        /// </summary>
        public bool IsFormed { get; set; }

        public string Name => "ADR";

        public SignalAndValue Process(Candle candle)
        {
            IsFormed = MovingAverage.IsFormed;

            return MovingAverage.Process(new Candle
            {
                CloseBid = candle.HighAsk - candle.LowAsk,
                IsComplete = candle.IsComplete
            });
        }
    }
}