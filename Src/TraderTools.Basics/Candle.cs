using System;
using System.Runtime.InteropServices;
using TraderTools.Basics.Extensions;

namespace TraderTools.Basics
{
    public struct BasicCandleAndIndicators : ICandle
    {
        public BasicCandleAndIndicators(ICandle candle,
            int signalsCount)
        {
            HighBid = (float)candle.HighBid;
            LowBid = (float)candle.LowBid;
            CloseBid = (float)candle.CloseBid;
            OpenBid = (float)candle.OpenBid;
            HighAsk = (float)candle.HighAsk;
            LowAsk = (float)candle.LowAsk;
            CloseAsk = (float)candle.CloseAsk;
            OpenAsk = (float)candle.OpenAsk;
            CloseTimeTicks = candle.CloseTimeTicks;
            OpenTimeTicks = candle.OpenTimeTicks;
            IsComplete = candle.IsComplete;
            Indicators = new SignalAndValue[signalsCount];
        }

        public BasicCandleAndIndicators(
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
            OpenTimeTicks = openTimeTicks;
            CloseTimeTicks = closeTimeTicks;
            OpenBid = openBid;
            HighBid = highBid;
            LowBid = lowBid;
            CloseBid = closeBid;
            OpenAsk = openAsk;
            HighAsk = highAsk;
            LowAsk = lowAsk;
            CloseAsk = closeAsk;
            IsComplete = isComplete;
            Indicators = new SignalAndValue[signalsCount];
        }

        public long OpenTimeTicks { get; set; }
        public long CloseTimeTicks { get; set; }

        public float OpenBid { get; set; }
        public float HighBid { get; set; }
        public float LowBid { get; set; }
        public float CloseBid { get; set; }
        public float OpenAsk { get; set; }
        public float HighAsk { get; set; }
        public float LowAsk { get; set; }
        public float CloseAsk { get; set; }
        public byte IsComplete { get; set; }
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
            return $"{this.OpenTime()} {this.CloseTime()} OpenBid:{OpenBid} CloseBid:{CloseBid} HighBid:{HighBid} LowBid:{LowBid} IsComplete:{IsComplete}";
        }
    }

    public interface ICandle
    {
        float HighBid { get; set; }
        float LowBid { get; set; }
        float CloseBid { get; set; }
        float OpenBid { get; set; }
        float HighAsk { get; set; }
        float LowAsk { get; set; }
        float CloseAsk { get; set; }
        float OpenAsk { get; set; }
        long OpenTimeTicks { get; set; }
        long CloseTimeTicks { get; set; }
        byte IsComplete { get; set; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Candle : ICandle
    {
        public long OpenTimeTicks { get; set; }
        public long CloseTimeTicks { get; set; }
        public float OpenBid { get; set; }
        public float CloseBid { get; set; }
        public float HighBid { get; set; }
        public float LowBid { get; set; }
        public float OpenAsk { get; set; }
        public float CloseAsk { get; set; }
        public float HighAsk { get; set; }
        public float LowAsk { get; set; }
        public byte IsComplete { get; set; }

        public override string ToString()
        {
            return $"OpenTime: {new DateTime(OpenTimeTicks)} CloseTime: {new DateTime(CloseTimeTicks)} Open bid: {OpenBid} Close bid: {CloseBid}";
        }
    }
}