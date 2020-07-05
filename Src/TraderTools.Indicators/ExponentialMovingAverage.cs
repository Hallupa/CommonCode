﻿using System.Collections.Generic;
using System.Linq;
using TraderTools.Basics;

namespace TraderTools.Indicators
{
    public class ExponentialMovingAverage : LengthIndicator
    {
        private float _prevFinalValue;
        private float _multiplier = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExponentialMovingAverage"/>.
        /// </summary>
        public ExponentialMovingAverage()
            : this(32)
        {
        }

        public ExponentialMovingAverage(int length)
        {
            Length = length;
            _multiplier = 2.0F / (Length + 1);
            _prevFinalValue = 0;
        }

        public override string Name => $"EMA{Length}";

        public override SignalAndValue Process(Candle candle)
        {
            var newValue = candle.CloseBid;

            if (!IsFormed)
            {
                if (candle.IsComplete == 1)
                {
                    Buffer.Add(newValue);

                    _prevFinalValue = Buffer.Sum() / Length;

                    return new SignalAndValue(_prevFinalValue, IsFormed);
                }

                return new SignalAndValue((Buffer.Skip(1).Sum() + newValue) / Length, IsFormed);
            }
            else
            {
                var curValue = (newValue - _prevFinalValue) * _multiplier + _prevFinalValue;

                if (candle.IsComplete == 1) _prevFinalValue = curValue;

                return new SignalAndValue(curValue, IsFormed);
            }
        }
    }
}