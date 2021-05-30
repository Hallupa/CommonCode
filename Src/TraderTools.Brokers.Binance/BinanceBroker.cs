using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Binance.Net;
using Binance.Net.Objects.Spot;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.ExchangeInterfaces;
using CryptoExchange.Net.Objects;
using log4net;
using TraderTools.Basics;

namespace TraderTools.Brokers.Binance
{
    public class BinanceBroker : IBroker
    {
        public string Name => "Binance";
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private BinanceClient _client;
        private string _apiKey;
        private string _secretKey;
        private List<string> _symbols;

        public bool UpdateAccount(IBrokerAccount account, IBrokersCandlesService candlesService, IMarketDetailsService marketsService,
            Action<string> updateProgressAction, DateTime? lastUpdateTime, out List<Trade> addedOrUpdatedTrades)
        {
            throw new NotImplementedException();
        }

        public bool UpdateCandles(List<Candle> candles, string market, Timeframe timeframe, DateTime start, Action<string> progressUpdate)
        {
            while (true)
            {
                var retries = 3;
                WebCallResult<IEnumerable<ICommonKline>> binanceCandles = null;
                while (retries >= 0)
                {
                    binanceCandles = ((IExchangeClient) _client).GetKlinesAsync(
                        market,
                        TimeSpan.FromSeconds((int) timeframe),
                        start,
                        DateTime.UtcNow,
                        1000).Result;

                    if (binanceCandles.Error != null)
                    {
                        Log.Error($"Binance error: {binanceCandles.Error.Message}");
                    }
                    else
                    {
                        break;
                    }

                    Thread.Sleep(500);
                    retries--;

                    if (retries == 0)
                    {
                        Log.Error("Binance ran out of attempts");
                    }
                }

                foreach (var b in binanceCandles.Data)
                {
                    candles.Add(CreateCandle(b, timeframe));
                }

                if (!binanceCandles.Data.Any()) break;
                Log.Info($"Got {binanceCandles.Data.Count()} candles for {market} {timeframe}");
                start = binanceCandles.Data.Last().CommonOpenTime.AddSeconds(1);
            }

            Log.Info($"Update {market} {timeframe} complete");
            return true;
        }

        public void Connect()
        {
        }

        public List<string> GetSymbols()
        {
            if (_symbols != null) return _symbols;

            return (_symbols = ((IExchangeClient)_client).GetSymbolsAsync().Result.Data.Select(x => x.CommonName).ToList());
        }

        public BinanceBroker(string apiKey, string secretKey)
        {
            _apiKey = apiKey;
            _secretKey = secretKey;

            _client = new BinanceClient(new BinanceClientOptions
            {
                ApiCredentials = new ApiCredentials(_apiKey, _secretKey)
            });
            //_client.Ping();
        }

        public ConnectStatus Status => ConnectStatus.Connected;
        public BrokerKind Kind => BrokerKind.Trade;

        public List<TickData> GetTickData(IBroker broker, string market, DateTime utcStart, DateTime utcEnd)
        {
            throw new NotImplementedException();
        }

        public List<MarketDetails> GetMarketDetailsList()
        {
            throw new NotImplementedException();
        }

        public Candle? GetSingleCandle(string market, Timeframe timeframe, DateTime date)
        {
            throw new NotImplementedException();
        }

        private Candle CreateCandle(ICommonKline kline, Timeframe timefame)
        {
            return new Candle
            {
                CloseTimeTicks = kline.CommonOpenTime.AddSeconds((int)timefame).Ticks,
                IsComplete = (byte)1,// kline.CloseTime <= DateTime.UtcNow ? (byte)1 : (byte)0,
                CloseBid = (float)kline.CommonClose,
                HighBid = (float)kline.CommonHigh,
                LowBid = (float)kline.CommonLow,
                OpenTimeTicks = kline.CommonOpenTime.Ticks,
                OpenBid = (float)kline.CommonOpen,
                Volume = (float)kline.CommonVolume
                //TradeCount = kline.TradeCount
            };
        }
    }
}