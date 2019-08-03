﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderTools.Basics
{
    public class ChartDataRunner
    {
        private List<ICandle> _lowestTimeframeCandles;
        private Timeframe _lowestTimeframe;
        private List<Timeframe> _timeframesExcludingLowest;

        public ChartDataRunner(List<(List<ICandle> Candles, Timeframe Timeframe)> allTimeframesCandles)
        {
            foreach (var candlesAndTimeframe in allTimeframesCandles)
            {
                AllCandles.Add(candlesAndTimeframe.Timeframe, candlesAndTimeframe.Candles);
                CandlesIndexes[candlesAndTimeframe.Timeframe] = 0;
                CurrentCandles[candlesAndTimeframe.Timeframe] = new List<ICandle>();
            }

            var lowestTimeframe = allTimeframesCandles.OrderBy(x => x.Timeframe).First();
            _lowestTimeframeCandles = lowestTimeframe.Candles;
            _lowestTimeframe = lowestTimeframe.Timeframe;
            _timeframesExcludingLowest = allTimeframesCandles.Select(x => x.Timeframe).Where(x => x != _lowestTimeframe).ToList();
        }

        public TimeframeLookup<int> CandlesIndexes { get; } = new TimeframeLookup<int>();
        public TimeframeLookup<List<ICandle>> AllCandles { get; }= new TimeframeLookup<List<ICandle>>();
        public TimeframeLookup<List<ICandle>> CurrentCandles { get; } = new TimeframeLookup<List<ICandle>>();
        public ICandle LatestSmallestTimeframeCandle { get; private set; }
        public bool IsComplete { get; private set; }
        public bool IsFinalCandle => CurrentCandles[Timeframe.D1].Count == AllCandles[Timeframe.D1].Count;

        public void ProgressLowestTimefameCandle()
        {
            ProgressTime(0, true);
        }

        public void ProgressTime(long progressToDateTicks, bool minOneCandleProgression = false)
        {
            while (CandlesIndexes[_lowestTimeframe] < AllCandles[_lowestTimeframe].Count && 
                   (AllCandles[_lowestTimeframe][CandlesIndexes[_lowestTimeframe]].CloseTimeTicks <= progressToDateTicks || minOneCandleProgression))
            {
                var lowestCandle = AllCandles[_lowestTimeframe][CandlesIndexes[_lowestTimeframe]];
                var endDateTicks = lowestCandle.CloseTimeTicks;
                CurrentCandles[_lowestTimeframe].Add(lowestCandle);

                if (endDateTicks > progressToDateTicks && !minOneCandleProgression) break;

                minOneCandleProgression = false;
                foreach (var timeframe in _timeframesExcludingLowest)
                {
                    var candles = AllCandles[timeframe];
                    var currentCandles = CurrentCandles[timeframe];
                    var lastCandleChecked = false;
                    var candlesAdded = false;

                    // Add in-range candles
                    while (CandlesIndexes[timeframe] < candles.Count && candles[CandlesIndexes[timeframe]].CloseTimeTicks <= endDateTicks)
                    {
                        // Remove incomplete candle if required
                        if (!lastCandleChecked)
                        {
                            lastCandleChecked = true;
                            if (currentCandles.Count > 0 && currentCandles[currentCandles.Count - 1].IsComplete == 0)
                            {
                                currentCandles.RemoveAt(currentCandles.Count - 1);
                            }
                        }

                        // Add candle
                        currentCandles.Add(candles[CandlesIndexes[timeframe]]);
                        CandlesIndexes[timeframe]++;
                        candlesAdded = true;
                    }

                    // Add/update incomplete candle if no candle was added
                    if (!candlesAdded)
                    {
                        if (currentCandles.Count > 0 && currentCandles[currentCandles.Count - 1].IsComplete == 0)
                        {
                            var candle = currentCandles[currentCandles.Count - 1];
                            candle.CloseBid = lowestCandle.CloseBid;
                            candle.CloseTimeTicks = lowestCandle.CloseTimeTicks;
                            if (lowestCandle.HighBid > candle.HighBid) candle.HighBid = lowestCandle.HighBid;
                            if (lowestCandle.LowBid < candle.LowBid) candle.LowBid = lowestCandle.LowBid;
                        }
                        else
                        {
                            currentCandles.Add(new Candle
                            {
                                OpenBid = lowestCandle.OpenBid,
                                CloseBid = lowestCandle.CloseBid,
                                CloseTimeTicks = lowestCandle.CloseTimeTicks,
                                OpenTimeTicks = lowestCandle.OpenTimeTicks,
                                HighBid = lowestCandle.HighBid,
                                LowBid = lowestCandle.LowBid,
                                IsComplete =  0,
                                Id = Guid.NewGuid()
                            });
                        }
                    }
                }

                LatestSmallestTimeframeCandle = CurrentCandles[_lowestTimeframe][CandlesIndexes[_lowestTimeframe]];
                CandlesIndexes[_lowestTimeframe]++;
            }

            IsComplete = CandlesIndexes[_lowestTimeframe] == AllCandles[_lowestTimeframe].Count;

        }
    }
}