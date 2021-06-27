using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private Func<decimal> _getBalanceFunc;
        private Func<decimal> _getTotalValueFunc;
        private Dictionary<string, TimeframeLookup<List<(IIndicator Indicator, IndicatorValues IndicatorValues)>>> _indicators 
            = new Dictionary<string, TimeframeLookup<List<(IIndicator Indicator, IndicatorValues IndicatorValues)>>>();
        public DateTime? StartTime { get; private set; }

        public DateTime? EndTime { get; private set; }

        public decimal Commission { get; private set; }

        public bool Initialised { get; private set; }

        public string Broker { get; private set; } = "FXCM";

        public decimal Balance => _getBalanceFunc();
        public decimal TotalValue => _getTotalValueFunc();
        private Action<TradeWithIndexing, Trade, Candle> _tradeUpdatedAction;

        public void SetInitialised(
            Func<decimal> getBalanceFunc,
            Func<decimal> getTotalValueFunc,
            Action<TradeWithIndexing, Trade, Candle> tradeUpdatedAction)
        {
            Initialised = true;
            _getBalanceFunc = getBalanceFunc;
            _getTotalValueFunc = getTotalValueFunc;
            _tradeUpdatedAction = tradeUpdatedAction;
        }

        public void SetMarkets(params string[] markets)
        {
            if (Initialised) throw new ApplicationException("Cannot set markets after strategy is initialised");
            Markets = markets;
        }

        public void SetBroker(string name)
        {
            if (Initialised) throw new ApplicationException("Cannot set broker after strategy is initialised");
            Broker = name;
        }

        public void SetCommission(decimal amount)
        {
            if (Initialised) throw new ApplicationException("Cannot set commission after strategy is initialised");
            Commission = amount;
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
        
        public void UpdateIndicators(List<AddedCandleTimeframe> timeframesCandleAdded)
        {
            foreach (var c in timeframesCandleAdded)
            {
                if (!_indicators.ContainsKey(c.Market) || _indicators[c.Market][c.Timeframe] == null) continue;

                var addedCandle = Candles[c.Market][c.Timeframe].Last();
                foreach (var x in _indicators[c.Market][c.Timeframe])
                {
                    var signalAndValue = x.Indicator.Process(addedCandle);
                    x.IndicatorValues.AddValue(signalAndValue, addedCandle);
                }
            }
        }

        protected IndicatorValues EMA(string market, Timeframe timeframe, int length)
        {
            var indicator = new ExponentialMovingAverage(length);
            return AddIndicator(market, timeframe, indicator);
        }

        protected IndicatorValues StochRSI(string market, Timeframe timeframe, int length = 14)
        {
            var indicator = new StochasticRelativeStrengthIndex(length);
            return AddIndicator(market, timeframe, indicator);
        }

        protected IndicatorValues UpperBollingerBand(string market, Timeframe timeframe, float std = 2.0F)
        {
            var indicator = new BollingerBand(std, 20);
            return AddIndicator(market, timeframe, indicator);
        }

        protected IndicatorValues LowerBollingerBand(string market, Timeframe timeframe, float std = -2.0F)
        {
            var indicator = new BollingerBand(std, 20);
            return AddIndicator(market, timeframe, indicator);
        }

        protected IndicatorValues ATR(string market, Timeframe timeframe)
        {
            var indicator = new AverageTrueRange();
            return AddIndicator(market, timeframe, indicator);
        }

        protected IndicatorValues ADR(string market, Timeframe timeframe)
        {
            var indicator = new AverageDayRange();
            return AddIndicator(market, timeframe, indicator);
        }

        protected IndicatorValues RSI(string market, Timeframe timeframe)
        {
            var indicator = new RelativeStrengthIndex();
            return AddIndicator(market, timeframe, indicator);
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

        private IndicatorValues AddIndicator(string market, Timeframe timeframe, IIndicator indicator)
        {
            if (Initialised) throw new ApplicationException("Cannot add indicator after strategy is initialised");

            var values = new IndicatorValues();

            if (!_indicators.ContainsKey(market))
            {
                _indicators[market] = new TimeframeLookup<List<(IIndicator Indicator, IndicatorValues IndicatorValues)>>();
            }

            var x = _indicators[market];
            if (x[timeframe] == null) x[timeframe] = new List<(IIndicator Indicator, IndicatorValues IndicatorValues)>();
            x[timeframe].Add((indicator, values));

            return values;

        }

        public void SetSimulationParameters(
            TradeWithIndexingCollection trades,
            Dictionary<string, TimeframeLookup<List<Candle>>> currentCandles)
        {
            Trades = trades;
            Candles = currentCandles;
        }

        public Dictionary<string, TimeframeLookup<List<Candle>>> Candles { get; private set; }

        protected void Log(string txt)
        {
            _log.Info(txt);
        }

        public abstract void ProcessCandles(List<AddedCandleTimeframe> addedCandleTimeframes);

        protected Trade MarketLong(string market, decimal amount, decimal? stop = null, decimal? limit = null)
        {
            var candle = Candles[market][_smallestCandleTimeframe][Candles[market][_smallestCandleTimeframe].Count - 1];
            var entryPrice = candle.CloseAsk != 0 ? candle.CloseAsk : candle.CloseBid; // Take the ask price unless it is unavailable

            var trade = TradeFactory.CreateMarketEntry(
                Broker, (decimal)entryPrice, candle.CloseTime(), TradeDirection.Long, amount, market, stop, limit,
                calculateOptions: CalculateOptions.ExcludePipsCalculations);

            _tradeUpdatedAction(null, trade, candle);

            return trade;
        }

        protected void CloseTrade(Trade t)
        {
            foreach (var indexedTrade in Trades.OpenTrades)
            {
                if (indexedTrade.Trade == t)
                {
                    CloseTrade(indexedTrade);
                    return;
                }
            }

            foreach (var indexedTrade in Trades.OrderTradesAsc)
            {
                if (indexedTrade.Trade == t)
                {
                    CloseTrade(indexedTrade);
                    return;
                }
            }
        }

        protected void CloseTrade(TradeWithIndexing t)
        {
            if (t.Trade.CloseDateTime == null)
            {
                var candle = Candles[t.Trade.Market][_smallestCandleTimeframe][Candles[t.Trade.Market][_smallestCandleTimeframe].Count - 1];
                t.Trade.SetClose(new DateTime(candle.CloseTimeTicks, DateTimeKind.Utc), (decimal)candle.CloseBid, TradeCloseReason.ManualClose);

                _tradeUpdatedAction(t, t.Trade, candle);
            }
        }

        /*  private int GetLotSize(string market, decimal maxRiskEquity, DateTime dateTimeUtc, decimal entryOrOrder, decimal stop)
          {
              var gbpPerPip = _brokerCandlesService.GetGBPPerPip(
                      _marketDetailsService,
                      _brokersService.GetBroker(Broker),
                      market,
                      1,
                      dateTimeUtc,
                      false);
  
              var stopPips = _marketDetailsService.GetPriceInPips(Broker, Math.Abs(entryOrOrder - stop), market);
  
              var lotSize = maxRiskEquity / (gbpPerPip * stopPips);
  
              var ret = (int)lotSize;
  
              if (ret <= _marketDetailsService.GetMarketDetails(Broker, market).MinLotSize)
              {
                  return 0;
              }
  
              return ret;
          }*/

        protected void UpdateStopsInOpenTradesTrailIndicator(IndicatorValues indicatorValues)
        {
            foreach (var t in Trades.OpenTrades)
            {
                var candle = Candles[t.Trade.Market][_smallestCandleTimeframe][Candles[t.Trade.Market][_smallestCandleTimeframe].Count - 1];
                StopHelper.TrailIndicatorValues(t.Trade, candle, indicatorValues);
            }
        }

        protected Trade MarketLong(string market, float amount, float stop, float? limit = null)
        {
            return MarketLong(market, (decimal)amount, (decimal)stop, (decimal?)limit);
        }

        protected Trade OrderShort(string market, decimal price, decimal stop, decimal? limit = null, DateTime? expire = null)
        {
            int? lotSize = 1;
            var candle = Candles[market][_smallestCandleTimeframe][Candles[market][_smallestCandleTimeframe].Count - 1];
            var trade = TradeFactory.CreateOrder(
                Broker, price, candle, TradeDirection.Short, lotSize.Value, market, expire, stop,
                limit, CalculateOptions.ExcludePipsCalculations);

            _tradeUpdatedAction(null, trade, candle);

            return trade;
        }

        protected Trade OrderShort(string market, float price, float stop, float? limit = null, DateTime? expire = null)
        {
            return OrderShort(market, (decimal)price, (decimal)stop, (decimal?)limit, expire);
        }

        protected Trade OrderLong(string market, decimal amount, decimal price, decimal? stop = null, decimal? limit = null, DateTime? expire = null)
        {
            var candle = Candles[market][_smallestCandleTimeframe][Candles[market][_smallestCandleTimeframe].Count - 1];
            var trade = TradeFactory.CreateOrder(
                Broker, price, candle, TradeDirection.Long, amount, market, expire, stop,
                limit, CalculateOptions.ExcludePipsCalculations);

            _tradeUpdatedAction(null, trade, candle);

            return trade;
        }

        protected Trade OrderLong(string market, float amount, float price, float? stop = null, float? limit = null, DateTime? expire = null)
        {
            return OrderLong(market, (decimal)amount, (decimal)price, (decimal?)stop, (decimal?)limit, expire);
        }

        protected Trade MarketShort(string market, decimal amount, decimal stop, decimal? limit = null)
        {
            var candle = Candles[market][_smallestCandleTimeframe][Candles[market][_smallestCandleTimeframe].Count - 1];
            var entryPrice = candle.CloseBid != 0 ? candle.CloseBid : candle.CloseAsk; // Take the bid price unless it is unavailable

            var trade = TradeFactory.CreateMarketEntry(
                Broker, (decimal)entryPrice, candle.CloseTime(), TradeDirection.Short, amount, market, stop, limit,
                calculateOptions: CalculateOptions.ExcludePipsCalculations);

            _tradeUpdatedAction(null, trade, candle);

            return trade;
        }

        protected Trade MarketShort(string market, float amount, float stop, float? limit = null)
        {
            return MarketShort(market, (decimal)amount, (decimal)stop, (decimal?)limit);
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