using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Binance.Net;
using Binance.Net.Objects;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Core.Extensions;
using TraderTools.Core.Helpers;

namespace TraderTools.Brokers.Binance
{
    public class BinanceBroker : IBroker
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private BinanceClient _client;
        private bool disposedValue = false;

        public string Name => "Binance";

        public ConnectStatus Status { get; set; }
        public BrokerKind Kind => BrokerKind.Trade;

        public List<TickData> GetTickData(IBroker broker, string market, DateTime utcStart, DateTime utcEnd)
        {
            throw new NotImplementedException();
        }

        public List<MarketDetails> GetMarketDetailsList()
        {
            return new List<MarketDetails>();
        }

        public void Connect()
        {
            _client = new BinanceClient(new BinanceClientOptions()
            {
                ApiCredentials = new ApiCredentials(_apiKey, _apiSecret),
                AutoTimestamp = true
            });


            // Get server time to sync local time with server
            _client.GetServerTime();

            Status = ConnectStatus.Connected;
            Log.Info("Binance connected");
        }

        /*public List<string> GetSymbols()
        {
            var info = _client.GetExchangeInfo();
            return info.Data.Symbols.Select(x => x.Name).ToList();
        }*/

        /*private void UpdateDailyPrices(DateTime start, string priceAsset, List<Candle> candles)
        {
            var limit = 500;
            var now = DateTime.UtcNow;
            CallResult<BinanceKline[]> klines = null;

            while (klines == null || klines.Data.Length == limit)
            {
                if (klines != null)
                {
                    start = klines.Data.Max(x => x.CloseTime);
                }

                klines = _client.GetKlines(priceAsset, KlineInterval.OneDay, start, now, limit);

                foreach (var kline in klines.Data)
                {
                    var candle = CreateCandle(kline, priceAsset);

                    if (candles.All(x => x.CloseTime != candle.CloseTime))
                    {
                        candles.Add(candle);
                    }
                }
            }
        }*/

        private Dictionary<(string Market, Timeframe timeframe), DateTime> _recentUpdated = new Dictionary<(string Market, Timeframe timeframe), DateTime>();
        private string _apiKey;
        private string _apiSecret;

        public Candle? GetSingleCandle(string market, Timeframe timeframe, DateTime date)
        {
            this.WaitForStatus(ConnectStatus.Connected);
            var interval = GetKlineInterval(timeframe);

            var klines = _client.GetKlines(market, interval, date.AddSeconds(-(int)timeframe * 2), date, 1);
            if (klines.Data == null || klines.Data.Length == 0)
            {
                Log.Error($"Unable to get {interval} candles for {market}");
                return null;
            }

            return CreateCandle(klines.Data.OrderByDescending(x => x.CloseTime).First(), market);
        }

        public bool UpdateCandles(List<Candle> candles, string market, Timeframe timeframe, DateTime start, Action<string> progressUpdate)
        {
            var limit = 500;
            var to = DateTime.UtcNow;
            var updated = false;
            CallResult<BinanceKline[]> klines = null;

            if (_recentUpdated.ContainsKey((market, timeframe)))
            {
                if ((DateTime.UtcNow - _recentUpdated[(market, timeframe)]).TotalSeconds < 60)
                {
                    return false;
                }
            }

            _recentUpdated[(market, timeframe)] = DateTime.UtcNow;

            Log.Debug($"Updating Binance candles for {market} {timeframe}");

            this.WaitForStatus(ConnectStatus.Connected);

            var interval = GetKlineInterval(timeframe);


            while (klines == null || klines.Data.Length == limit)
            {
                if (klines != null)
                {
                    start = klines.Data.Max(x => x.OpenTime).AddMinutes(-10);
                }

                klines = _client.GetKlines(market, interval, start, to, limit);

                if (klines.Data == null)
                {
                    Log.Error($"Unable to get {interval} candles for {market}");
                    return false;
                }

                var newCandles = new List<Candle>();
                foreach (var kline in klines.Data)
                {
                    var candle = CreateCandle(kline, market);
                    //candle.Timeframe = (int)timeframe;

                    if (candles.All(x => x.CloseTimeTicks != candle.CloseTimeTicks))
                    {
                        newCandles.Add(candle);
                        updated = true;
                    }
                }

                var existingCandleLookup = new Dictionary<long, Candle>();
                candles.ForEach(x => existingCandleLookup[x.OpenTimeTicks] = x);

                foreach (var candle in newCandles.OrderBy(x => x.OpenTimeTicks))
                {
                    if (existingCandleLookup.TryGetValue(candle.OpenTimeTicks, out var existingCandle))
                    {
                        var index = candles.IndexOf(existingCandle);
                        candles.RemoveAt(index);
                        candles.Add(candle);
                        updated = true;
                    }
                    else
                    {
                        candles.Add(candle);
                        existingCandleLookup[candle.OpenTimeTicks] = candle;
                        updated = true;
                    }
                }
            }

            return updated;
        }

        private static KlineInterval GetKlineInterval(Timeframe timeframe)
        {
            KlineInterval interval;

            switch (timeframe)
            {
                case Timeframe.D1:
                    interval = KlineInterval.OneDay;
                    break;
                case Timeframe.H1:
                    interval = KlineInterval.OneHour;
                    break;
                case Timeframe.H2:
                    interval = KlineInterval.TwoHour;
                    break;
                case Timeframe.H4:
                    interval = KlineInterval.FourHour;
                    break;
                case Timeframe.M1:
                    interval = KlineInterval.OneMinute;
                    break;
                case Timeframe.M15:
                    interval = KlineInterval.FifteenMinutes;
                    break;
                default:
                    throw new ApplicationException($"Binance unable to update candles for interval {timeframe}");
            }

            return interval;
        }

        public bool UpdateAccount(IBrokerAccount brokerAccount, IBrokersCandlesService brokerCandles, Action<string> updateProgressAction)
        {
            var limit = 500;

            var info = _client.GetExchangeInfo();

            if (!info.Success)
            {
                Log.Error("Unable to get Binance exchange info");
                return false;
            }

            var symbols = info.Data.Symbols.ToList();
            var trades = new List<Trade>();

            var producerConsumer = new ProducerConsumer<BinanceSymbol>(1, symbol =>
            {
                long? fromId = 1;
                CallResult<BinanceTrade[]> binanceTrades = null;

                updateProgressAction($"Updating trades for {symbol.Name}");

                while (fromId == 1 || (binanceTrades != null && binanceTrades.Data.Length == limit))
                {
                    Log.Debug($"Getting trades for: {symbol.Name}");
                    binanceTrades = _client.GetMyTrades(symbol.Name, null, null, limit, fromId);

                    if (!binanceTrades.Success)
                    {
                        Log.Error($"Unable to get Binance trades - ${binanceTrades.Error.Message}");
                        return ProducerConsumerActionResult.Stop;
                    }

                    if (binanceTrades.Data.Length == 0)
                    {
                        break;
                    }

                    fromId = (int) binanceTrades.Data[binanceTrades.Data.Length - 1].Id;

                    lock (trades)
                    {
                        foreach (var trade in binanceTrades.Data)
                        {
                            trades.Add(CreateTrade(trade, symbol.Name, symbol.BaseAsset));
                            // var existingTrade = brokerAccount.Trades.FirstOrDefault(t => t.Id == trade.Id.ToString());

                            /*if (existingTrade == null)
                            {
                                brokerAccount.Trades.Add(CreateTrade(trade, symbol.Name, symbol.BaseAsset));
                            }*/
                        }
                    }
                }

                return ProducerConsumerActionResult.Success;
            });

            foreach (var symbol in symbols)
            {
                producerConsumer.Add(symbol);
            }

            producerConsumer.SetProducerCompleted();
            producerConsumer.Start();
            producerConsumer.WaitUntilConsumersFinished();
            if (producerConsumer.IsCanceled)
            {
                Log.Error($"Unable to get Binance trades");
                return false;
            }

            // todo update existing broker account trades

            updateProgressAction($"Updating deposits/withdrawals");
            UpdateDepositsWithdrawals(brokerAccount);
            return true;
        }

        public static string GetAssetMarket(string asset)
        {
            if (asset == "GAS")
            {
                return "GASBTC";
            }

            var priceAsset = asset != "ETH" ? $"{asset}ETH" : "ETHUSDT";
            if (priceAsset == "BTCETH")
            {
                priceAsset = "ETHBTC";
            }

            return priceAsset;
        }

        private Trade CreateTrade(BinanceTrade binanceTrade, string asset, string baseAsset)
        {
            return new Trade
            {
                Id = binanceTrade.Id.ToString(),
                Broker = "Binance",
                Commission = binanceTrade.Commission,
                CommissionAsset = binanceTrade.CommissionAsset,
                OrderId = binanceTrade.OrderId.ToString(),
                EntryPrice = binanceTrade.Price,
                EntryQuantity = binanceTrade.Quantity,
                TradeDirection = binanceTrade.IsBuyer ? (TradeDirection?)TradeDirection.Long : (TradeDirection?)TradeDirection.Short,
                EntryDateTime = binanceTrade.Time,
                Market = asset,
                BaseAsset = baseAsset
            };
        }

        private Candle CreateCandle(BinanceKline kline, string asset)
        {
            return new Candle
            {
                CloseTimeTicks = kline.CloseTime.Ticks,
                IsComplete = kline.CloseTime <= DateTime.UtcNow ? (byte)1 : (byte)0,
                CloseAsk = (float)kline.Close,
                HighAsk = (float)kline.High,
                LowAsk = (float)kline.Low,
                OpenTimeTicks = kline.OpenTime.Ticks,
                OpenAsk = (float)kline.Open,
                //Volume = (double)kline.Volume,
                //TradeCount = kline.TradeCount
            };
        }

        private bool UpdateDepositsWithdrawals(IBrokerAccount account)
        {
            foreach (var d in account.DepositsWithdrawals)
            {
                d.Broker = "Binance";
            }

            var deposits = _client.GetDepositHistory();
            var withdrawals = _client.GetWithdrawHistory();
            var updated = false;

            if (!deposits.Success)
            {
                Log.Error($"Unable to get binance deposits - {deposits.Error.Message}");
            }

            if (deposits.Data != null)
            {
                foreach (var deposit in deposits.Data.List)
                {
                    var dto = new DepositWithdrawal
                    {
                        Asset = deposit.Asset,
                        Time = deposit.InsertTime,
                        Amount = deposit.Amount,
                        Id = deposit.TransactionId,
                        Broker = "Binance"
                    };

                    if (account.DepositsWithdrawals.All(x => x.Id != deposit.TransactionId))
                    {
                        account.DepositsWithdrawals.Add(dto);
                        updated = true;
                    }
                }
            }

            if (!withdrawals.Success)
            {
                Log.Error($"Unable to get binance withdrawals - {withdrawals.Error.Message}");
            }

            if (withdrawals.Data != null)
            {
                foreach (var withdrawal in withdrawals.Data.List)
                {
                    var dto = new DepositWithdrawal
                    {
                        Asset = withdrawal.Asset,
                        Time = withdrawal.ApplyTime,
                        Amount = -withdrawal.Amount,
                        Id = withdrawal.TransactionId,
                        Broker = "Binance"
                    };

                    if (account.DepositsWithdrawals.All(x => x.Id != withdrawal.TransactionId))
                    {
                        account.DepositsWithdrawals.Add(dto);
                        updated = true;
                    }
                }
            }

            return updated;
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                _client?.Dispose();

                disposedValue = true;
            }
        }

        ~BinanceBroker()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        public void SetUsernamePassword(string apiKey, string apiSecret)
        {
            _apiKey = apiKey;
            _apiSecret = apiSecret;
        }
    }
}