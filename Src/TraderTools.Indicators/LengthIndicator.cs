﻿using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Indicators
{
    public abstract class LengthIndicator : IIndicator
    {
        private int _length = 1;

        public virtual bool IsFormed => Buffer.Count >= Length;

        public abstract string Name { get; }

        public abstract SignalAndValue Process(Candle candle);

        protected List<float> Buffer { get; } = new List<float>();

        public int Length
        {
            get => _length;
            protected set
            {
                _length = value;
            }
        }
    }
}