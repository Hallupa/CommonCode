using TraderTools.Basics.Extensions;

namespace TraderTools.Basics
{
    public struct CandleAndIndicators
    {
        public Candle Candle { get; private set; }

        public CandleAndIndicators(Candle candle,
            int signalsCount)
        {
            Candle = candle;
            Indicators = new SignalAndValue[signalsCount];
        }

        public CandleAndIndicators(
            long openTimeTicks,
            long closeTimeTicks,
            float openBid,
            float highBid,
            float lowBid,
            float closeBid,
            float openAsk,
            float highAsk,
            float lowAsk,
            float closeAsk,
            byte isComplete,
            int signalsCount)
        {
            Candle = new Candle
            {
                OpenTimeTicks = openTimeTicks,
                CloseTimeTicks = closeTimeTicks,
                OpenBid = openBid,
                HighBid = highBid,
                LowBid = lowBid,
                CloseBid = closeBid,
                OpenAsk = openAsk,
                HighAsk = highAsk,
                LowAsk = lowAsk,
                CloseAsk = closeAsk,
                IsComplete = isComplete
            };
            Indicators = new SignalAndValue[signalsCount];
        }

        public SignalAndValue[] Indicators { get; set; }

        public SignalAndValue this[Indicator indicator]
        {
            get
            {
                return Indicators[(int)indicator];
            }
        }

        public void Set(Indicator indicator, SignalAndValue signalValue)
        {
            if (Indicators == null)
            {
                Indicators = new SignalAndValue[13];
            }

            Indicators[(int)indicator] = signalValue;
        }

        public override string ToString()
        {
            return $"{Candle.OpenTime()} {Candle.CloseTime()} OpenBid:{Candle.OpenBid} CloseBid:{Candle.CloseBid} HighBid:{Candle.HighBid} LowBid:{Candle.LowBid} IsComplete:{Candle.IsComplete}";
        }
    }
}