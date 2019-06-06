using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using com.fxcm.report;
using fxcore2;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;

namespace TraderTools.Brokers.FXCM
{
    public class FxcmBroker : IDisposable, IBroker
    {
        private class MarketDetails
        {
            public int Digits { get; set; }
            public double PointSize { get; set; }
            public int MinLotSize { get; set; }
            public double ContractMultiplier { get; set; }
            public string Currency { get; set; }
        }

        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private O2GSession _session;
        private bool disposedValue = false;
        private O2SessionStatus _sessionStatus;
        private SessionStatusListener _sessionStatusListener;
        private Random _rnd;
        private string _user;
        private string _password;
        private Dictionary<string, MarketDetails> _instrumentDetails = new Dictionary<string, MarketDetails>();

        public decimal GetGBPPerPip(
            decimal amount, string market, DateTime date,
            IBrokersCandlesService candleService, IBroker broker, bool updateCandles)
        {
            var marketDetails = _instrumentDetails[market];
            decimal price = 0M;

            // If market contains GBP, then use the market for the price
            if (market.Contains("GBP"))
            {
                price = (decimal)candleService.GetFirstCandleThatClosesBeforeDateTime(market, broker, Timeframe.D1, date, updateCandles).Open;

                if (market.StartsWith("GBP"))
                {
                    price = 1M / price;
                }
            }
            else
            {
                // Try to get GBP candle, if it exists
                var marketForPrice = !market.Contains("/") ? $"GBP/{marketDetails.Currency}" : $"GBP/{market.Split('/')[1]}";

                if (!_instrumentDetails.ContainsKey(marketForPrice))
                {
                    marketForPrice = $"{marketForPrice.Split('/')[1]}/{marketForPrice.Split('/')[0]}";
                }

                if (marketForPrice == "GBP/GBP")
                {
                    price = 1M;
                }
                else
                {
                    // Get candle price, if it exists
                    if (_instrumentDetails.ContainsKey(marketForPrice))
                    {
                        price = (decimal)candleService.GetFirstCandleThatClosesBeforeDateTime(marketForPrice, broker, Timeframe.D1, date, updateCandles).Open;
                    }
                    else
                    {
                        // Otherwise, try to get the USD candle and convert back to GBP
                        // Try to get $ candle and convert to £
                        var usdCandle = candleService.GetFirstCandleThatClosesBeforeDateTime($"USD/{market.Split('/')[1]}", broker, Timeframe.D1, date, updateCandles);
                        var gbpUSDCandle = candleService.GetFirstCandleThatClosesBeforeDateTime("GBP/USD", broker, Timeframe.D1, date, updateCandles);
                        price = (decimal)gbpUSDCandle.Open / (decimal)usdCandle.Open;
                    }
                }

                if (marketForPrice.StartsWith("GBP"))
                {
                    price = 1M / price;
                }

            }

            return amount * (decimal)marketDetails.ContractMultiplier * (decimal)marketDetails.PointSize * price;
        }

        public decimal GetOnePipInDecimals(string market)
        {
            return (decimal)_instrumentDetails[market].PointSize;
        }

        private void UpdateInstrumentDetails()
        {
            var loginRules = _session.getLoginRules();
            var offersResponse = loginRules.getTableRefreshResponse(O2GTableType.Offers);
            var factory = _session.getResponseReaderFactory();
            var accountsResponse = loginRules.getTableRefreshResponse(O2GTableType.Accounts);
            var accountsReader = factory.createAccountsTableReader(accountsResponse);
            var tradingSettingsProvider = loginRules.getTradingSettingsProvider();
            var account = accountsReader.getRow(0);
            var tableManager = GetTableManager();
            var readerFactory = _session.getResponseReaderFactory();
            var response = loginRules.getTableRefreshResponse(O2GTableType.Offers);
            var responseReader = readerFactory.createOffersTableReader(response);

            for (int i = 0; i < responseReader.Count; i++)
            {
                var offerRow = responseReader.getRow(i);

                _instrumentDetails.Add(offerRow.Instrument, new MarketDetails
                {
                    ContractMultiplier = offerRow.ContractMultiplier,
                    PointSize = offerRow.PointSize,
                    MinLotSize = tradingSettingsProvider.getMinQuantity(offerRow.Instrument, account),
                    Digits = offerRow.Digits,
                    Currency = offerRow.ContractCurrency
                });
            }
        }

        public FxcmBroker()
        {
            _rnd = new Random();

            _session = O2GTransport.createSession();
            _sessionStatusListener = new SessionStatusListener(_session, "", "");
            _session.useTableManager(O2GTableManagerMode.Yes, null);
            _session.subscribeSessionStatus(_sessionStatusListener);
        }

        public void SetUsernamePassword(string user, string password)
        {
            _user = user;
            _password = password;
        }

        public void Connect()
        {
            Log.Info("FXCM connecting");

            _sessionStatusListener.Reset();
            _session.login(_user, _password, "http://www.fxcorporate.com/Hosts.jsp", "Real");
            if (_sessionStatusListener.WaitEvents() && _sessionStatusListener.Connected)
            {
                Log.Info("FXCM Connected");
            }
            else if (!_sessionStatusListener.Connected)
            {
                Log.Error("Unable to connect to FXCM");
            }

            UpdateInstrumentDetails();
        }

        public ConnectStatus Status
        {
            get
            {
                var currentStatus = _session?.getChartSessionStatus();

                switch (currentStatus)
                {
                    case null:
                        return ConnectStatus.Disconnected;

                    case O2GChartSessionStatusCode.Connected:
                        return ConnectStatus.Connected;

                    case O2GChartSessionStatusCode.Connecting:
                        return ConnectStatus.Connecting;

                    case O2GChartSessionStatusCode.Disconnecting:
                        return ConnectStatus.Disconnecting;
                }

                return ConnectStatus.Disconnected;
            }
        }

        public BrokerKind Kind => BrokerKind.SpreadBet;

        public string Name => "FXCM";

        private O2GTableManager GetTableManager()
        {
            var tableManager = _session.getTableManager();
            var managerStatus = tableManager.getStatus();
            while (managerStatus == O2GTableManagerStatus.TablesLoading)
            {
                Thread.Sleep(50);
                managerStatus = tableManager.getStatus();
            }

            if (managerStatus == O2GTableManagerStatus.TablesLoadFailed)
            {
                throw new Exception("Cannot refresh all tables of table manager");
            }

            return tableManager;
        }

        public bool UpdateAccount(IBrokerAccount account, IBrokersCandlesService candlesService)
        {
            var tableManager = GetTableManager();

            // Get open trades
            var updated = GetOpenTrades(account, candlesService, tableManager, out var openTrades);

            updated = GetOrders(account, candlesService, tableManager, out var orders) || updated;

            // Update trades from reports API
            updated = GetReportTrades(account, candlesService) || updated;

            // Set any open trades to closed that aren't in the open list
            foreach (var trade in account.Trades.Where(t =>
                t.OrderDateTime != null && t.EntryPrice == null && t.CloseDateTime == null))
            {
                if (!openTrades.Contains(trade) && !orders.Contains(trade))
                {
                    if (trade.OrderExpireTime != null && trade.OrderExpireTime <= DateTime.UtcNow)
                    {
                        trade.CloseDateTime = trade.OrderExpireTime;
                        trade.CloseReason = TradeCloseReason.HitExpiry;
                    }
                    else
                    {
                        trade.CloseDateTime = DateTime.UtcNow;
                        trade.CloseReason = TradeCloseReason.OrderClosed;
                    }
                }
            }

            UpdateDepositsWithdrawals(account);

            return updated;
        }

        private bool GetOpenTrades(IBrokerAccount account, IBrokersCandlesService candlesService,
            O2GTableManager tableManager, out List<TradeDetails> openTradesFound)
        {
            O2GTableIterator iterator;
            openTradesFound = new List<TradeDetails>();
            var openTrades = tableManager.getTable(O2GTableType.Trades);

            iterator = new O2GTableIterator();
            var addedOrUpdatedOpenTrade = false;

            while (openTrades.getNextGenericRow(iterator, out var row))
            {
                var tradeRow = (O2GTradeTableRow)row;
                var trade = account.Trades.FirstOrDefault(x => x.Id == tradeRow.TradeID);

                if (trade == null)
                {
                    trade = new TradeDetails();
                    account.Trades.Add(trade);
                    trade.Market = tradeRow.Instrument;
                    trade.Broker = "FXCM";
                    trade.Id = tradeRow.TradeID;
                    trade.EntryDateTime = DateTime.SpecifyKind(tradeRow.OpenTime, DateTimeKind.Utc);
                    trade.EntryPrice = (decimal)tradeRow.OpenRate;

                    if (!tradeRow.Limit.Equals(0D))
                    {
                        trade.AddLimitPrice(trade.EntryDateTime.Value, (decimal)tradeRow.Limit);
                        this.UpdateTradeStopLimitPips(trade);
                    }

                    if (!tradeRow.Stop.Equals(0D))
                    {
                        trade.AddStopPrice(trade.EntryDateTime.Value, (decimal)tradeRow.Stop);
                        this.UpdateTradeStopLimitPips(trade);
                    }

                    trade.EntryQuantity = tradeRow.Amount;
                    trade.GrossProfitLoss = (decimal)tradeRow.GrossPL;
                    trade.TradeDirection = tradeRow.BuySell == "S" ? TradeDirection.Short : TradeDirection.Long;
                    trade.PricePerPip = GetGBPPerPip(trade.EntryQuantity.Value, trade.Market, trade.EntryDateTime.Value,
                        candlesService, this, true);

                    addedOrUpdatedOpenTrade = true;
                }
                else
                {
                    if (trade.EntryDateTime != DateTime.SpecifyKind(tradeRow.OpenTime, DateTimeKind.Utc))
                    {
                        trade.EntryDateTime = DateTime.SpecifyKind(tradeRow.OpenTime, DateTimeKind.Utc);
                        addedOrUpdatedOpenTrade = true;
                    }

                    if (trade.GrossProfitLoss != (decimal)tradeRow.GrossPL)
                    {
                        trade.GrossProfitLoss = (decimal)tradeRow.GrossPL;
                        addedOrUpdatedOpenTrade = true;
                    }

                    if (trade.EntryPrice != (decimal)tradeRow.OpenRate)
                    {
                        trade.EntryPrice = (decimal)tradeRow.OpenRate;
                        addedOrUpdatedOpenTrade = true;
                    }

                    if (trade.EntryQuantity != (decimal)tradeRow.Amount)
                    {
                        trade.EntryQuantity = (decimal)tradeRow.Amount;
                        trade.PricePerPip = GetGBPPerPip(trade.EntryQuantity.Value, trade.Market, trade.EntryDateTime.Value,
                            candlesService, this, true);

                        addedOrUpdatedOpenTrade = true;
                    }

                    if (trade.TradeDirection != (tradeRow.BuySell == "S" ? TradeDirection.Short : TradeDirection.Long))
                    {
                        trade.TradeDirection = tradeRow.BuySell == "S" ? TradeDirection.Short : TradeDirection.Long;
                        addedOrUpdatedOpenTrade = true;
                    }

                    if (!tradeRow.Limit.Equals(0))
                    {
                        if (trade.LimitPrices.Count == 0 ||
                            trade.LimitPrices[trade.LimitPrices.Count - 1].Price != (decimal)tradeRow.Limit)
                        {
                            trade.AddLimitPrice(DateTime.UtcNow, (decimal)tradeRow.Limit);
                            this.UpdateTradeStopLimitPips(trade);
                            addedOrUpdatedOpenTrade = true;
                        }
                    }

                    if (!tradeRow.Stop.Equals(0))
                    {
                        if (trade.StopPrices.Count == 0 ||
                            trade.StopPrices[trade.StopPrices.Count - 1].Price != (decimal)tradeRow.Stop)
                        {
                            trade.AddStopPrice(DateTime.UtcNow, (decimal)tradeRow.Stop);
                            this.UpdateTradeStopLimitPips(trade);
                            addedOrUpdatedOpenTrade = true;
                        }
                    }
                }

                openTradesFound.Add(trade);
            }

            return addedOrUpdatedOpenTrade;
        }

        private bool GetOrders(IBrokerAccount account, IBrokersCandlesService candlesService,
            O2GTableManager tableManager, out List<TradeDetails> orders)
        {
            orders = new List<TradeDetails>();
            var iterator = new O2GTableIterator();
            var offers = tableManager.getTable(O2GTableType.Offers);
            var offersLookup = new Dictionary<string, O2GOfferTableRow>();
            while (offers.getNextGenericRow(iterator, out var row))
            {
                var offerRow = (O2GOfferTableRow)row;
                offersLookup.Add(offerRow.OfferID, offerRow);
            }

            var openTrades = tableManager.getTable(O2GTableType.Orders);

            // Get offer from offer Id to get instrument
            var addedOrUpdatedOpenTrade = false;

            // Get all orders
            var tradeRows = new Dictionary<string, List<O2GOrderTableRow>>();
            while (openTrades.getNextGenericRow(iterator, out var row))
            {
                var tradeRow = (O2GOrderTableRow)row;
                if (!tradeRows.ContainsKey(tradeRow.TradeID))
                {
                    tradeRows[tradeRow.TradeID] = new List<O2GOrderTableRow>();
                }

                tradeRows[tradeRow.TradeID].Add(tradeRow);
            }

            // Create trades
            foreach (var kvp in tradeRows)
            {
                var orderOrder = kvp.Value.FirstOrDefault(x => x.Type == "SE" || x.Type == "LE");
                var orderOffer = orderOrder != null ? offersLookup[orderOrder.OfferID] : null;
                var stopOrder = kvp.Value.FirstOrDefault(x => x.Type == "S");
                var stopOffer = stopOrder != null ? offersLookup[stopOrder.OfferID] : null;
                var limitOrder = kvp.Value.FirstOrDefault(x => x.Type == "L");
                var limitOffer = limitOrder != null ? offersLookup[limitOrder.OfferID] : null;

                if (orderOrder == null)
                {
                    continue;
                }

                var time = DateTime.SpecifyKind(orderOrder.StatusTime, DateTimeKind.Utc);
                var expiry = DateTime.SpecifyKind(orderOrder.ExpireDate, DateTimeKind.Utc);
                var instrument = orderOffer.Instrument;
                var orderPrice = orderOrder.Rate;
                var buySell = orderOrder.BuySell;
                decimal? stop = GetStopPrice(stopOrder, instrument, (decimal)orderPrice, buySell);
                decimal? limit = GetLimitPrice(limitOrder, instrument, (decimal)orderPrice, buySell);
                var amount = orderOrder.Amount;
                var actualExpiry = orderOrder.ExpireDate.Year >= 1950 ? (DateTime?)expiry : null;

                var trade = account.Trades.FirstOrDefault(x => x.Id == orderOrder.TradeID);
                if (trade == null)
                {
                    trade = new TradeDetails();
                    account.Trades.Add(trade);
                    trade.Broker = "FXCM";
                    trade.OrderAmount = orderOrder.Amount;
                    trade.Id = orderOrder.TradeID;
                    trade.Market = instrument;
                    trade.OrderPrice = (decimal)orderPrice;
                    trade.OrderDateTime = time;
                    trade.OrderExpireTime = actualExpiry;
                    trade.PricePerPip = GetGBPPerPip(orderOrder.Amount, trade.Market, time,
                        candlesService, this, true);

                    if (stop != null)
                    {
                        trade.AddStopPrice(time, stop);
                        this.UpdateTradeStopLimitPips(trade);
                    }

                    if (limit != null)
                    {
                        trade.AddLimitPrice(time, limit);
                        this.UpdateTradeStopLimitPips(trade);
                    }

                    trade.TradeDirection = buySell == "B" ? TradeDirection.Long : TradeDirection.Short;
                    trade.OrderAmount = amount;
                    addedOrUpdatedOpenTrade = true;
                }
                else
                {
                    if (trade.OrderPrice != (decimal)orderPrice)
                    {
                        trade.OrderPrice = (decimal)orderPrice;
                        addedOrUpdatedOpenTrade = true;
                    }

                    if (trade.OrderAmount != orderOrder.Amount)
                    {
                        trade.OrderAmount = orderOrder.Amount;
                        trade.PricePerPip = GetGBPPerPip(trade.OrderAmount.Value, trade.Market, trade.OrderDateTime.Value,
                            candlesService, this, true);
                        addedOrUpdatedOpenTrade = true;
                    }

                    if (trade.OrderDateTime != time)
                    {
                        trade.OrderDateTime = time;
                        addedOrUpdatedOpenTrade = true;
                    }

                    if (trade.OrderExpireTime != actualExpiry)
                    {
                        trade.OrderExpireTime = actualExpiry;
                        addedOrUpdatedOpenTrade = true;
                    }

                    if (stop != null && trade.StopPrice != stop)
                    {
                        trade.ClearStopPrices();
                        trade.AddStopPrice(time, stop);
                        this.UpdateTradeStopLimitPips(trade);
                        addedOrUpdatedOpenTrade = true;
                    }

                    if (limit != null && trade.LimitPrice != limit)
                    {
                        trade.ClearLimitPrices();
                        trade.AddLimitPrice(time, limit);
                        this.UpdateTradeStopLimitPips(trade);
                        addedOrUpdatedOpenTrade = true;
                    }
                }

                orders.Add(trade);
            }

            return addedOrUpdatedOpenTrade;
        }

        private decimal? GetStopPrice(O2GOrderTableRow stop, string instrument, decimal orderPrice, string buySell)
        {
            if (stop == null)
            {
                return null;
            }

            decimal? ret;

            if (!stop.Rate.Equals(0.0))
            {
                ret = (decimal)stop.Rate;
            }
            else
            {
                ret = (decimal)orderPrice +
                      (buySell == "B"
                          ? -this.GetPriceFromPips(Math.Abs((decimal)stop.PegOffset), instrument)
                          : this.GetPriceFromPips(Math.Abs((decimal)stop.PegOffset), instrument));
            }

            return ret;
        }

        private decimal? GetLimitPrice(O2GOrderTableRow limit, string instrument, decimal orderPrice, string buySell)
        {
            if (limit == null)
            {
                return null;
            }

            decimal? ret;

            if (!limit.Rate.Equals(0.0))
            {
                ret = (decimal)limit.Rate;
            }
            else
            {
                ret = (decimal)orderPrice +
                      (buySell == "B"
                          ? this.GetPriceFromPips(Math.Abs((decimal)limit.PegOffset), instrument)
                          : -this.GetPriceFromPips(Math.Abs((decimal)limit.PegOffset), instrument));
            }

            return ret;
        }

        private bool GetReportTrades(IBrokerAccount brokerAccount, IBrokersCandlesService candlesService)
        {
            var updated = false;
            var url = "https://fxpa2.fxcorporate.com/fxpa/getreport.app/";
            var connection = "GBREAL";
            var account = _user;
            var report = "REPORT_NAME_CUSTOMER_ACCOUNT_STATEMENT";
            var startDate = "1/1/2013";
            var endDate = DateTime.UtcNow.ToString("m/d/yyyy");
            var outputFormat = "csv-web";
            var locale = "enu";
            var extra = "";

            ReportServer server = new ReportServer();
            string[] lines;

            var reader = server.GetReport(url, connection, report, _user, _password, account, locale, startDate, endDate, outputFormat, extra);
            try
            {
                using (var output = new MemoryStream())
                {
                    byte[] buff = new byte[1024];
                    int r;

                    while (true)
                    {
                        r = reader.Read(buff, 0, 1024);
                        if (r <= 0)
                        {
                            break;
                        }

                        output.Write(buff, 0, r);
                    }

                    lines = Encoding.ASCII.GetString(output.ToArray()).Split(new string[] { "\n" }, StringSplitOptions.None);
                }
            }
            finally
            {
                reader.Close();
            }

            var startLineIndex = GetLineStart(lines, "\"CLOSED TRADE LIST\"");
            var endLineIndex = GetLineStart(lines, "\"Total:");

            for (var i = startLineIndex + 2; i < endLineIndex; i += 2)
            {
                var values1 = CsvHelper.GetCsvValues(lines[i]);
                var values2 = CsvHelper.GetCsvValues(lines[i + 1]);

                if (values1[0] == "No data found for the statement period")
                {
                    break;
                }

                var id = values1[0];
                var market = values1[1];
                var quantity = values1[2];
                var date = values1[3];
                var sold = values1[4];
                var bought = values1[5];
                var grossProfitLoss = values1[6];
                var markupPips = values1[7];
                var comm = values1[8];
                var dividends = values1[9];
                var rollover = values1[10];
                var adj = values1[11];
                var netProfitLoss = values1[12];
                var condition = values1[13];

                var id2 = values2[0];
                var market2 = values2[1];
                var quantity2 = values2[2];
                var date2 = values2[3];
                var sold2 = values2[4];
                var bought2 = values2[5];
                var grossProfitLoss2 = values2[6];
                var markupPips2 = values2[7];
                var comm2 = values2[8];
                var dividends2 = values2[9];
                var rollover2 = values2[10];
                var adj2 = values2[11];
                var netProfitLoss2 = values2[12];
                var condition2 = values2[13];

                var existingTrade = brokerAccount.Trades.FirstOrDefault(t => t.Id == id);

                var trade = new TradeDetails
                {
                    Id = id,
                    Broker = "FXCM",
                    Market = market,
                    EntryQuantity = decimal.Parse(quantity),
                    EntryPrice = !string.IsNullOrEmpty(sold) ? decimal.Parse(sold) : decimal.Parse(bought),
                    EntryDateTime = DateTime.ParseExact(date, "M/d/y h:m tt", CultureInfo.InvariantCulture), // 3/28/18 7:59 PM
                    ClosePrice = !string.IsNullOrEmpty(sold2) ? decimal.Parse(sold2) : decimal.Parse(bought2),
                    CloseDateTime = DateTime.ParseExact(date2, "M/d/y h:m tt", CultureInfo.InvariantCulture),
                    TradeDirection = !string.IsNullOrEmpty(sold) ? TradeDirection.Short : TradeDirection.Long,
                    OrderKind = condition == "Mkt" ? OrderKind.Market : OrderKind.EntryPrice,
                    GrossProfitLoss = decimal.Parse(grossProfitLoss2),
                    NetProfitLoss = decimal.Parse(netProfitLoss2),
                    Rollover = decimal.Parse(rollover2)
                };

                trade.PricePerPip = GetGBPPerPip(trade.EntryQuantity.Value, trade.Market, trade.EntryDateTime.Value,
                    candlesService, this, true);

                switch (condition2)
                {
                    case "S":
                        trade.CloseReason = TradeCloseReason.HitStop;
                        break;
                    case "L":
                        trade.CloseReason = TradeCloseReason.HitLimit;
                        break;
                    case "S(t)":
                        trade.CloseReason = TradeCloseReason.HitExpiry;
                        break;
                    case "C":
                        trade.CloseReason = TradeCloseReason.ManualClose;
                        break;
                    default:
                        Debugger.Break();
                        break;
                }

                if (existingTrade == null)
                {
                    brokerAccount.Trades.Add(trade);
                    updated = true;
                }
                else
                {
                    if (trade.EntryQuantity != existingTrade.EntryQuantity)
                    {
                        existingTrade.EntryQuantity = trade.EntryQuantity;
                        updated = true;
                    }

                    if (trade.EntryDateTime != existingTrade.EntryDateTime)
                    {
                        existingTrade.EntryDateTime = trade.EntryDateTime;
                        updated = true;
                    }

                    if (trade.EntryPrice != existingTrade.EntryPrice)
                    {
                        existingTrade.EntryPrice = trade.EntryPrice;
                        updated = true;
                    }

                    if (trade.ClosePrice != existingTrade.ClosePrice)
                    {
                        existingTrade.ClosePrice = trade.ClosePrice;
                        updated = true;
                    }

                    if (trade.CloseDateTime != existingTrade.CloseDateTime)
                    {
                        existingTrade.CloseDateTime = trade.CloseDateTime;
                        updated = true;
                    }

                    if (trade.OrderKind != existingTrade.OrderKind)
                    {
                        existingTrade.OrderKind = trade.OrderKind;
                        updated = true;
                    }

                    if (trade.CloseReason != existingTrade.CloseReason)
                    {
                        existingTrade.CloseReason = trade.CloseReason;
                        updated = true;
                    }

                    if (trade.GrossProfitLoss != existingTrade.GrossProfitLoss)
                    {
                        existingTrade.GrossProfitLoss = trade.GrossProfitLoss;
                        updated = true;
                    }

                    if (trade.NetProfitLoss != existingTrade.NetProfitLoss)
                    {
                        existingTrade.NetProfitLoss = trade.NetProfitLoss;
                        updated = true;
                    }

                    if (trade.Rollover != existingTrade.Rollover)
                    {
                        existingTrade.Rollover = trade.Rollover;
                        updated = true;
                    }
                }
            }

            return updated;
        }

        private static int GetLineStart(string[] lines, string text, int startIndex = 0)
        {
            for (var i = startIndex; i < lines.Length; i++)
            {
                if (lines[i].StartsWith(text))
                {
                    return i;
                }
            }

            return -1;
        }

        public List<TickData> GetTickData(IBroker broker, string market, DateTime utcStart, DateTime utcEnd)
        {
            GetHistoryPrices(market, "t1", Timeframe.T1, utcStart, utcEnd, out var bidCandles, out var askCandles, out var ret);

            return ret;
        }

        public bool UpdateCandles(List<ICandle> candles, string market, Timeframe timeframe, DateTime start)
        {
            var to = DateTime.UtcNow;
            var updated = false;

            string interval;

            switch (timeframe)
            {
                case Timeframe.D1:
                    interval = "D1";
                    break;
                case Timeframe.H1:
                    interval = "H1";
                    break;
                case Timeframe.H2:
                    interval = "H2";
                    break;
                case Timeframe.H4:
                    interval = "H4";
                    break;
                case Timeframe.H8:
                    interval = "H8";
                    break;
                case Timeframe.M1:
                    interval = "m1";
                    break;
                case Timeframe.M5:
                    interval = "m5";
                    break;
                case Timeframe.M15:
                    interval = "m15";
                    break;
                default:
                    throw new ApplicationException($"FXCM unable to update candles for interval {timeframe}");
            }

            GetHistoryPrices(market, interval, timeframe, start, to, out var bidCandles, out var askCandles, out var _);


            var existingCandleLookup = new Dictionary<long, ICandle>();
            candles.ForEach(x => existingCandleLookup[x.OpenTimeTicks] = x);

            if (bidCandles != null)
            {
                foreach (var candle in bidCandles.OrderBy(x => x.OpenTimeTicks))
                {
                    if (existingCandleLookup.TryGetValue(candle.OpenTimeTicks, out var existingCandle))
                    {
                        if (existingCandle.IsComplete != candle.IsComplete
                            || existingCandle.CloseTimeTicks != candle.CloseTimeTicks
                            || existingCandle.Close != candle.Close
                            || existingCandle.High != candle.High
                            || existingCandle.Low != candle.Low
                            || existingCandle.Open != candle.Open
                            || existingCandle.OpenTimeTicks != candle.OpenTimeTicks
                            || existingCandle.Volume != candle.Volume)
                        {
                            var index = candles.IndexOf(existingCandle);
                            candles.RemoveAt(index);
                            candles.Insert(index, candle);

                            existingCandleLookup[candle.OpenTimeTicks] = candle;
                            updated = true;
                        }
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

        private bool UpdateDepositsWithdrawals(IBrokerAccount brokerAccount)
        {
            foreach (var d in brokerAccount.DepositsWithdrawals)
            {
                d.Broker = "FXCM";
            }

            var updated = false;
            var url = "https://fxpa2.fxcorporate.com/fxpa/getreport.app/";
            var connection = "GBREAL";
            var account = _user;
            var report = "REPORT_NAME_CUSTOMER_ACCOUNT_STATEMENT";
            var startDate = "2/30/2017";
            var endDate = DateTime.UtcNow.ToString("m/d/yyyy");
            var outputFormat = "csv-web";// "pdf-web";
            var locale = "enu";
            var extra = "";

            ReportServer server = new ReportServer();
            string[] lines;

            var reader = server.GetReport(url, connection, report, _user, _password, account, locale, startDate, endDate, outputFormat, extra);
            try
            {
                using (var output = new MemoryStream())
                {
                    byte[] buff = new byte[1024];
                    int r;

                    while (true)
                    {
                        r = reader.Read(buff, 0, 1024);
                        if (r <= 0)
                        {
                            break;
                        }

                        output.Write(buff, 0, r);
                    }

                    lines = Encoding.ASCII.GetString(output.ToArray()).Split(new string[] { "\n" }, StringSplitOptions.None);
                }
            }
            finally
            {
                reader.Close();
            }

            var startLineIndex = GetLineStart(lines, "\"ACCOUNT ACTIVITY\"");
            var endLineIndex = GetLineStart(lines, "\"Total:", startLineIndex);

            for (var i = startLineIndex + 2; i < endLineIndex; i += 1)
            {
                var values1 = CsvHelper.GetCsvValues(lines[i]);

                if (values1[0] == "No data found for the statement period")
                {
                    break;
                }

                var date = values1[0];
                var code = values1[1];
                var description = values1[2];
                var accountNumber = values1[3];
                var ticket = values1[4];
                var amount = values1[5];
                var balance = values1[6];

                if (code == "Depos")
                {
                    if (brokerAccount.DepositsWithdrawals.All(x => x.Description != description))
                    {
                        var deposit = new DepositWithdrawal
                        {
                            Time = DateTime.ParseExact(date, "M/d/y h:m tt", CultureInfo.InvariantCulture),
                            Amount = decimal.Parse(amount),
                            Description = description,
                            Broker = "FXCM"
                        };
                        brokerAccount.DepositsWithdrawals.Add(deposit);
                        updated = true;
                    }
                }
            }

            return updated;
        }

        /// <summary>
        /// Request historical prices for the specified timeframe of the specified period
        /// {INSTRUMENT} - An instrument, for which you want to get historical prices.
        /// For example, EUR/USD. Mandatory argument.
        /// {TIMEFRAME} - time period which forms a single candle. Mandatory argument.
        /// For example, m1 - for 1 minute, H1 - for 1 hour.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="instrument"></param>
        /// <param name="interval"></param>
        /// <param name="dtFrom"></param>
        /// <param name="dtTo"></param>
        /// <param name="responseListener"></param>
        public void GetHistoryPrices(string instrument, string interval, Timeframe timeframe, DateTime dtFrom, DateTime dtTo, out List<Candle> bidCandles, out List<Candle> askCandles, out List<TickData> ticks)
        {
            if (_session == null)
            {
                Log.Warn($"FXCM not connected so unable to get {instrument} prices for {interval} for date {dtFrom}-{dtTo}");
                bidCandles = null;
                askCandles = null;
                ticks = null;
                return;
            }

            Log.Info($"Getting FXCM {instrument} prices for {interval} for date {dtFrom}-{dtTo}");
            var factory = _session.getRequestFactory();

            if (factory == null)
            {
                Log.Debug("Unable to connect to FXCM");
                bidCandles = null;
                askCandles = null;
                ticks = null;
                return;
            }

            var responseListener = new ResponseListener(_session);
            _session.subscribeResponse(responseListener);
            var o2gTimeframe = factory.Timeframes[interval];
            bidCandles = new List<Candle>();
            askCandles = new List<Candle>();
            ticks = new List<TickData>();

            if (o2gTimeframe == null)
            {
                throw new Exception(string.Format("Timeframe '{0}' is incorrect!", interval));
            }

            var maxReturn = 600;
            O2GRequest request = factory.createMarketDataSnapshotRequestInstrument(instrument, o2gTimeframe, maxReturn);
            DateTime dtFirst = dtTo;
            int iteration = 1;
            List<(DateTime, DateTime)> fromToList = new List<(DateTime, DateTime)>();
            Log.Debug($"Getting FCXM prices for {dtFrom}-{dtTo} for {instrument} {timeframe}");
            var reversed = false;

            do // cause there is limit for returned candles amount
            {
                if ((dtFirst - dtFrom).TotalMilliseconds < 1000)
                {
                    dtFirst = dtFrom.AddSeconds(1);
                    reversed = true;
                }

                var attempt = 1;
                var gotData = false;
                while (attempt < 10 && !gotData)
                {
                    factory.fillMarketDataSnapshotRequestTime(request, dtFrom, dtFirst.AddSeconds(1) < dtTo ? dtFirst.AddSeconds(1) : dtFirst, false); // Returns latest candles in date time range
                    fromToList.Add((dtFrom, dtFirst));

                    responseListener.SetRequestID(request.RequestID);
                    _session.sendRequest(request);
                    if (responseListener.WaitEvents())
                    {
                        gotData = true;
                        break;
                    }

                    Thread.Sleep(_rnd.Next(5000, 40000));
                }

                if (!gotData)
                {
                    throw new Exception("Response waiting timeout expired");
                }

                // shift "to" bound to oldest datetime of returned data
                var response = responseListener.GetResponse();

                if (!string.IsNullOrEmpty(responseListener.Error) && !responseListener.Error.Contains("No data found for symbol"))
                {
                    throw new ApplicationException($"Error getting {instrument} for interval {interval} timeframe {timeframe} - error: {responseListener.Error}");
                }

                if (response != null && response.Type == O2GResponseType.MarketDataSnapshot)
                {
                    O2GResponseReaderFactory readerFactory = _session.getResponseReaderFactory();
                    if (readerFactory != null)
                    {
                        O2GMarketDataSnapshotResponseReader reader = readerFactory.createMarketDataSnapshotReader(response);
                        if (reader.Count > 0)
                        {
                            if (DateTime.Compare(dtFirst, reader.getDate(0)) != 0)
                            {
                                dtFirst = reader.getDate(0);
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    ConstructCandles(instrument, _session, response, timeframe, out var tmpBidCandles, out var tmpAskCandles, out var tmpTickData);
                    Log.Debug($"FCXM got {tmpBidCandles.Count} candles for {instrument} {timeframe} (Total {bidCandles.Count}) {tmpTickData.Count} ticks (Total {ticks.Count} - {(ticks.Count > 0 ? ticks[ticks.Count - 1].Datetime.ToString() : string.Empty)} - {(ticks.Count > 0 ? ticks[0].Datetime.ToString() : string.Empty)})");

                    bidCandles.AddRange(tmpBidCandles);
                    askCandles.AddRange(tmpAskCandles);
                    ticks.AddRange(tmpTickData);
                }
                else
                {
                    break;
                }

                iteration++;
            } while (!reversed && dtFirst > dtFrom);

            Log.Info($"Got {bidCandles.Count} prices for {instrument} {interval} for date {dtFrom}-{dtTo}");
        }

        /// <summary>
        /// Print history data from response
        /// </summary>
        /// <param name="session"></param>
        /// <param name="response"></param>
        public static void ConstructCandles(string symbol, O2GSession session, O2GResponse response, Timeframe timeframe, out List<Candle> bidCandles, out List<Candle> askCandles, out List<TickData> bidTicks)
        {
            bidCandles = new List<Candle>();
            askCandles = new List<Candle>();
            bidTicks = new List<TickData>();
            O2GResponseReaderFactory factory = session.getResponseReaderFactory();

            if (factory != null)
            {
                O2GMarketDataSnapshotResponseReader reader = factory.createMarketDataSnapshotReader(response);
                for (int ii = reader.Count - 1; ii >= 0; ii--)
                {
                    if (reader.isBar)
                    {
                        var bidCandle = new Candle();
                        bidCandle.OpenTimeTicks = DateTime.SpecifyKind(reader.getDate(ii), DateTimeKind.Utc).Ticks;
                        bidCandle.CloseTimeTicks = new DateTime(bidCandle.OpenTimeTicks).AddSeconds((int)timeframe).Ticks;
                        bidCandle.IsComplete = bidCandle.CloseTimeTicks <= DateTime.UtcNow.Ticks ? (byte)1 : (byte)0;
                        bidCandle.Close = reader.getBidClose(ii);
                        bidCandle.Open = reader.getBidOpen(ii);
                        bidCandle.High = reader.getBidHigh(ii);
                        bidCandle.Low = reader.getBidLow(ii);
                        bidCandle.Timeframe = (int)timeframe;
                        bidCandle.Volume = reader.getVolume(ii);
                        bidCandles.Add(bidCandle);

                        var askCandle = new Candle();
                        askCandle.OpenTimeTicks = DateTime.SpecifyKind(reader.getDate(ii), DateTimeKind.Utc).Ticks;
                        askCandle.CloseTimeTicks = new DateTime(askCandle.OpenTimeTicks).AddSeconds((int)timeframe).Ticks;
                        askCandle.IsComplete = askCandle.CloseTimeTicks <= DateTime.UtcNow.Ticks ? (byte)1 : (byte)0;
                        askCandle.Close = reader.getAskClose(ii);
                        askCandle.Open = reader.getAskOpen(ii);
                        askCandle.High = reader.getAskHigh(ii);
                        askCandle.Low = reader.getAskLow(ii);
                        askCandle.Timeframe = (int)timeframe;
                        askCandle.Volume = reader.getVolume(ii);
                        askCandles.Add(askCandle);
                    }
                    else
                    {
                        var tick = new TickData();
                        tick.Datetime = reader.getDate(ii);
                        tick.Open = (float)reader.getBidOpen(ii);
                        tick.Close = (float)reader.getBidClose(ii);
                        tick.High = (float)reader.getBidHigh(ii);
                        tick.Low = (float)reader.getBidLow(ii);
                        bidTicks.Add(tick);
                    }
                }
            }
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                if (_session != null)
                {
                    if (_session.getChartSessionStatus() == O2GChartSessionStatusCode.Connected)
                    {
                        Disconnect();
                    }

                    _session.Dispose();
                    _session = null;
                }

                disposedValue = true;
            }
        }

        public void Disconnect()
        {
            Log.Info("FXCM disconnecting");

            if (_session != null)
            {
                _sessionStatusListener.Reset();
                _session.logout();
                _sessionStatusListener.WaitEvents();
            }

            Log.Info("FXCM disconnected");
        }

        ~FxcmBroker()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}