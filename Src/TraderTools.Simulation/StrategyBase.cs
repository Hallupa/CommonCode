using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Indicators;

namespace TraderTools.Simulation
{
    public abstract class StrategyBase
    {
        public static readonly string[] Majors = { "EUR/USD", "USD/JPY", "GBP/USD", "USD/CHF", "AUD/USD", "USD/CAD", "NZD/USD" };
        public static readonly string[] Minors = { "EUR/CHF", "EUR/GBP", "EUR/JPY", "CHF/JPY", "GBP/CHF", "EUR/AUD", "EUR/CAD", "AUD/CAD", "AUD/JPY", "CAD/JPY", "NZD/JPY", "GBP/CAD", "GBP/NZD", "GBP/AUD", "AUD/NZD", "AUD/CHF", "EUR/NZD", "NZD/CHF", "CAD/CHF", "NZD/CAD" };
        public static readonly string[] MajorIndices = { "US30", "UK100", "NAS100", "GER30", "AUS200", "SPX500" };

        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private Timeframe _smallestCandleTimeframe;
        private TimeframeLookup<List<(IIndicator Indicator, IndicatorValues IndicatorValues)>> _indicators = new TimeframeLookup<List<(IIndicator Indicator, IndicatorValues IndicatorValues)>>();

        private decimal _riskEquityPercent = 0.5M;

        public DateTime? StartTime { get; private set; }

        public DateTime? EndTime { get; private set; }

        public bool Initialised { get; private set; }

        public void SetInitialised()
        {
            Initialised = true;
        }

        public void SetMarkets(params string[] markets)
        {
            if (Initialised) throw new ApplicationException("Cannot set markets after strategy is initialised");
            Markets = markets;
        }

        public static string[] GetDefaultMarkets()
        {
            return Majors.Concat(Minors).Concat(MajorIndices).ToArray();
        }

        public string[] Markets { get; private set; }

        protected void AddMajors()
        {
            var current = Markets != null ? Markets.ToList() : new List<string>();
            SetMarkets(current.Union(Majors).ToArray());
        }

        protected void AddMinors()
        {
            var current = Markets != null ? Markets.ToList() : new List<string>();
            SetMarkets(current.Union(Minors).ToArray());
        }

        protected void AddMajorIndices()
        {
            var current = Markets != null ? Markets.ToList() : new List<string>();
            SetMarkets(current.Union(MajorIndices).ToArray());
        }
        protected void SetTimeframes(params Timeframe[] timeframes)
        {
            if (Initialised) throw new ApplicationException("Cannot set timeframes after strategy is initialised");

            Timeframes = timeframes.OrderBy(x => x).ToArray();
            _smallestCandleTimeframe = Timeframes.First();
        }

        public Timeframe[] Timeframes { get; private set; }

        public TradeWithIndexingCollection Trades { get; private set; }

        protected void SetRiskEquityPercent(decimal riskEquityPercent)
        {
            RiskEquityPercent = riskEquityPercent;
        }

        public decimal RiskEquityPercent
        {
            get => _riskEquityPercent;
            private set
            {
                if (Initialised) throw new ApplicationException("Cannot set equite risk amount after strategy is initialised");
                _riskEquityPercent = value;
            }
        }

        public void UpdateIndicators(List<Timeframe> timeframesCandleAdded)
        {
            foreach (var tf in timeframesCandleAdded)
            {
                if (_indicators[tf] == null) continue;

                var addedCandle = Candles[tf].Last();
                foreach (var x in _indicators[tf])
                {
                    var signalAndValue = x.Indicator.Process(addedCandle);
                    if (signalAndValue.IsFormed)
                    {
                        x.IndicatorValues.Values.Add((addedCandle.CloseTimeTicks, signalAndValue.Value));
                        x.IndicatorValues.Value = signalAndValue.Value;
                        x.IndicatorValues.HasValue = true;
                    }
                    else
                    {
                        x.IndicatorValues.Values.Add((addedCandle.CloseTimeTicks, null));
                        x.IndicatorValues.Value = 0F;
                        x.IndicatorValues.HasValue = false;
                    }
                }
            }
        }

        protected IndicatorValues EMA(Timeframe timeframe, int length)
        {
            var indicator = new ExponentialMovingAverage(length);
            return AddIndicator(timeframe, indicator);
        }

        protected IndicatorValues UpperBollingerBand(Timeframe timeframe, float std = 2.0F)
        {
            var indicator = new BollingerBand(std, 20);
            return AddIndicator(timeframe, indicator);
        }

        protected IndicatorValues LowerBollingerBand(Timeframe timeframe, float std = -2.0F)
        {
            var indicator = new BollingerBand(std, 20);
            return AddIndicator(timeframe, indicator);
        }

        protected IndicatorValues ATR(Timeframe timeframe)
        {
            var indicator = new AverageTrueRange();
            return AddIndicator(timeframe, indicator);
        }

        protected IndicatorValues ADR(Timeframe timeframe)
        {
            var indicator = new AverageDayRange();
            return AddIndicator(timeframe, indicator);
        }

        protected IndicatorValues RSI(Timeframe timeframe)
        {
            var indicator = new RelativeStrengthIndex();
            return AddIndicator(timeframe, indicator);
        }

        public void SetStartDate(int year, int month, int day)
        {
            if (Initialised) throw new ApplicationException("Cannot set start time after strategy is initialised");
            StartTime = new DateTime(year, month, day);
        }

        public void SetEndDate(int year, int month, int day)
        {
            if (Initialised) throw new ApplicationException("Cannot set end time after strategy is initialised");
            EndTime = new DateTime(year, month, day);
        }

        private IndicatorValues AddIndicator(Timeframe timeframe, IIndicator indicator)
        {
            if (Initialised) throw new ApplicationException("Cannot add indicator after strategy is initialised");

            var values = new IndicatorValues();
            if (_indicators[timeframe] == null) _indicators[timeframe] = new List<(IIndicator Indicator, IndicatorValues IndicatorValues)>();
            _indicators[timeframe].Add((indicator, values));

            return values;

        }

        public void SetSimulationParameters(
            TradeWithIndexingCollection trades,
            TimeframeLookup<List<Candle>> currentCandles,
            MarketDetails market)
        {
            Trades = trades;
            Candles = currentCandles;
            Market = market;
        }

        public MarketDetails Market { get; private set; }

        public TimeframeLookup<List<Candle>> Candles { get; private set; }

        protected void Log(string txt)
        {
            _log.Info(txt);
        }

        public List<Trade> NewTrades { get; } = new List<Trade>();

        public abstract void ProcessCandles(List<Timeframe> newCandleTimeframes);

        protected Trade MarketLong(decimal stop, decimal? limit = null)
        {
            var lotSize = 1;
            var candle = Candles[_smallestCandleTimeframe][Candles[_smallestCandleTimeframe].Count - 1];
            var entryPrice = candle.CloseAsk;

            var trade = TradeFactory.CreateMarketEntry(
                "FXCM", (decimal)entryPrice, candle.CloseTime(), TradeDirection.Long, lotSize, Market.Name, stop, limit,
                calculateOptions: CalculateOptions.ExcludePipsCalculations);

            NewTrades.Add(trade);

            return trade;
        }

      /*  private int GetLotSize(string market, decimal maxRiskEquity, DateTime dateTimeUtc, decimal entryOrOrder, decimal stop)
        {
            var gbpPerPip = _brokerCandlesService.GetGBPPerPip(
                    _marketDetailsService,
                    _brokersService.GetBroker("FXCM"),
                    market,
                    1,
                    dateTimeUtc,
                    false);

            var stopPips = _marketDetailsService.GetPriceInPips("FXCM", Math.Abs(entryOrOrder - stop), market);

            var lotSize = maxRiskEquity / (gbpPerPip * stopPips);

            var ret = (int)lotSize;

            if (ret <= _marketDetailsService.GetMarketDetails("FXCM", market).MinLotSize)
            {
                return 0;
            }

            return ret;
        }*/

        protected void UpdateStopsInOpenTradesTrailIndicator(IndicatorValues indicatorValues)
        {
            var candle = Candles[_smallestCandleTimeframe][Candles[_smallestCandleTimeframe].Count - 1];
            foreach (var trade in Trades.OpenTrades)
            {
                StopHelper.TrailIndicatorValues(trade.Trade, candle, indicatorValues);
            }
        }

        protected Trade MarketLong(float stop, float? limit = null)
        {
            return MarketLong((decimal)stop, (decimal?)limit);
        }

        protected Trade OrderShort(decimal price, decimal stop, decimal? limit = null, DateTime? expire = null)
        {
            int? lotSize = 1;
            var candle = Candles[_smallestCandleTimeframe][Candles[_smallestCandleTimeframe].Count - 1];
            var trade = TradeFactory.CreateOrder(
                "FXCM", price, candle, TradeDirection.Short, lotSize.Value, Market.Name, expire, stop,
                limit, CalculateOptions.ExcludePipsCalculations);

            NewTrades.Add(trade);

            return trade;
        }

        protected Trade OrderShort(float price, float stop, float? limit = null, DateTime? expire = null)
        {
            return OrderShort((decimal)price, (decimal)stop, (decimal?)limit, expire);
        }

        protected Trade MarketShort(decimal stop, decimal? limit = null)
        {
            var lotSize = 1;
            var candle = Candles[_smallestCandleTimeframe][Candles[_smallestCandleTimeframe].Count - 1];
            var entryPrice = candle.CloseBid;

            var trade = TradeFactory.CreateMarketEntry(
                "FXCM", (decimal)entryPrice, candle.CloseTime(), TradeDirection.Short, lotSize, Market.Name, stop, limit,
                calculateOptions: CalculateOptions.ExcludePipsCalculations);

            NewTrades.Add(trade);

            return trade;
        }

        protected Trade MarketShort(float stop, float? limit = null)
        {
            return MarketShort((decimal)stop, (decimal?)limit);
        }

        /*private bool GetLotSize(string market, decimal entryPrice, decimal stop, decimal riskPercent, out int? lotSize)
        {
            lotSize = null;
            var balance = _account.GetBalance();
            var maxRiskAmount = riskPercent * balance;
            var stopInPips = Math.Abs(_marketDetailsService.GetPriceInPips(_broker.Name, stop, market) -
                                      _marketDetailsService.GetPriceInPips(_broker.Name, entryPrice, market));
            var marketDetails = _marketDetailsService.GetMarketDetails(_broker.Name, market);
            if (marketDetails == null || marketDetails.MinLotSize == null)
            {
                return false;
            }

            var minLotSize = marketDetails.MinLotSize.Value;
            lotSize = GetLotSize(market, maxRiskAmount, stopInPips, minLotSize);
            return true;
        }

        private int? GetLotSize(string market, decimal targetRiskGBP, decimal stopPips, int baseUnitSize)
        {
            for (var lotSize = baseUnitSize; true; lotSize += baseUnitSize)
            {
                var gbpPerPip = _candlesService.GetGBPPerPip(_marketDetailsService, _broker, market, lotSize, DateTime.UtcNow, true);
                var riskingGBP = gbpPerPip * stopPips;

                if (riskingGBP > targetRiskGBP)
                {
                    if (lotSize == baseUnitSize) return null;
                    return lotSize - baseUnitSize;
                }
            }
        }*/
    }
}