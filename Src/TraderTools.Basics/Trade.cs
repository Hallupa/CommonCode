using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Hallupa.Library;
using Newtonsoft.Json;
using TraderTools.Basics.Extensions;

namespace TraderTools.Basics
{
    public enum TradeCloseReason
    {
        HitStop,
        HitLimit,
        HitExpiry,
        ManualClose,
        OrderClosed
    }

    public enum OrderKind
    {
        Market,
        EntryPrice
    }

    public enum OrderType
    {
        /// <summary>
        /// Buy below current market price or sell above current market price.
        /// </summary>
        LimitEntry,

        /// <summary>
        /// Buy above current price or sell below current market price.
        /// </summary>
        StopEntry
    }

    public class Trade : INotifyPropertyChanged
    {
        #region Fields
        private decimal? _currentStopPrice = null;
        private decimal? _pricePerPip;
        private decimal? _entryPrice;
        private decimal? _closePrice;
        private decimal? _netProfitLoss;
        private Timeframe? _timeframe;
        private TradeDirection? _tradeDirection;
        private DateTime? _closeDateTime;
        private TradeCloseReason? _closeReason;
        private decimal? _orderPrice;
        private DateTime? _orderDateTime;
        private DateTime? _orderExpireTime;
        private OrderKind _orderKind;
        private DateTime? _entryDateTime;
        private string _comments;
        private string _strategies;
        private decimal? _stopInPips;
        private decimal? _limitInPips;
        private decimal? _initialStopInPips;
        private decimal? _initialLimitInPips;
        private decimal? _rMultiple;
        private decimal? _initialStop;
        private decimal? _initialLimit;
        private decimal? _stopPrice;
        private decimal? _limitPrice;
        private decimal? _grossProfitLoss;

        #endregion

        public Trade()
        {
        }

        public static Trade CreateOrder(string broker, decimal entryOrder, Candle latestCandle,
            TradeDirection direction, decimal amount, string market, DateTime? orderExpireTime,
            decimal? stop, decimal? limit, ITradeDetailsAutoCalculatorService tradeCalculatorService)
        {
            var orderDateTime = latestCandle.CloseTime();

            var trade = new Trade();
            trade.SetOrder(orderDateTime, entryOrder, market, direction, amount, orderExpireTime);
            if (stop != null) trade.AddStopPrice(orderDateTime, stop.Value);
            if (limit != null) trade.AddLimitPrice(orderDateTime, limit.Value);
            trade.Broker = broker;
            trade.OrderKind = OrderKind.EntryPrice;

            if (direction == Basics.TradeDirection.Long)
            {
                trade.OrderType = (float)entryOrder <= latestCandle.CloseAsk ? Basics.OrderType.LimitEntry : Basics.OrderType.StopEntry;
            }
            else
            {
                trade.OrderType = (float)entryOrder <= latestCandle.CloseBid ? Basics.OrderType.StopEntry : Basics.OrderType.LimitEntry;
            }
            return trade;
        }

        public static Trade CreateMarketEntry(string broker, decimal entryPrice, DateTime entryTime,
            TradeDirection direction, decimal amount, string market,
            decimal? stop, decimal? limit, ITradeDetailsAutoCalculatorService tradeCalculatorService,
            Timeframe? timeframe = null, string strategies = null, string comments = null, bool alert = false, CalculateOptions calculateOptions = CalculateOptions.Default)
        {
            var trade = new Trade { Broker = broker };
            if (stop != null) trade.AddStopPrice(entryTime, stop.Value);
            if (limit != null) trade.AddLimitPrice(entryTime, limit.Value);
            trade.Market = market;
            trade.TradeDirection = direction;
            trade.EntryPrice = entryPrice;
            trade.EntryDateTime = entryTime;
            trade.EntryQuantity = amount;
            trade.Timeframe = timeframe;
            trade.Alert = alert;
            trade.Comments = comments;
            trade.Strategies = strategies;
            trade.CalculateOptions = calculateOptions;
            return trade;
        }

        public string Comments
        {
            get => _comments;
            set
            {
                _comments = value;
                OnPropertyChanged();
            }
        }

        public string Strategies
        {
            get => _strategies ?? (_strategies = string.Empty);
            set
            {
                _strategies = value;
                OnPropertyChanged();
            }
        }

        public DateTime? EntryDateTime
        {
            get => _entryDateTime;
            set
            {
                _entryDateTime = value;
                OnPropertyChanged();
                OnPropertyChanged("EntryDateTimeLocal");
            }
        }

        public Guid UniqueId { get; set; } = Guid.NewGuid();
        
        public string Id { get; set; }
        
        public string Broker { get; set; }
        
        public decimal? Commission { get; set; }
        
        public string CommissionAsset { get; set; }
        
        public string OrderId { get; set; }

        public OrderKind OrderKind
        {
            get => _orderKind;
            set
            {
                _orderKind = value;
                OnPropertyChanged();
            }
        }

        public decimal? EntryPrice
        {
            get => _entryPrice;
            set
            {
                _entryPrice = value;
                TradeCalculator.UpdateStopPips(this);
                TradeCalculator.UpdateInitialStopPips(this);
                TradeCalculator.UpdateLimitPips(this);
                TradeCalculator.UpdateInitialLimitPips(this);
                TradeCalculator.UpdateRMultiple(this);
                OnPropertyChanged();
                OnPropertyChanged("Status");
            }
        }

        public decimal? ClosePrice
        {
            get => _closePrice;
            set
            {
                _closePrice = value;
                TradeCalculator.UpdateRMultiple(this);
                OnPropertyChanged();
                OnPropertyChanged("Status");
            }
        }

        public decimal? EntryQuantity { get; set; }

        public decimal? GrossProfitLoss
        {
            get => _grossProfitLoss;
            set
            {
                _grossProfitLoss = value;
                TradeCalculator.UpdateRMultiple(this);
                OnPropertyChanged();
                OnPropertyChanged("Profit");
            }
        }

        public decimal? CommissionValue
        {
            get => _commissionValue;
            set
            {
                _commissionValue = value;
                OnPropertyChanged();
            }
        }

        public string CommissionValueCurrency
        {
            get => _commissionValueCurrency;
            set
            {
                _commissionValueCurrency = value;
                OnPropertyChanged();
            }
        }

        public decimal? EntryValue
        {
            get => _entryValue;
            set
            {
                _entryValue = value;
                OnPropertyChanged();
            }
        }

        public string EntryValueCurrency
        {
            get => _entryValueCurrency;
            set
            {
                _entryValueCurrency = value;
                OnPropertyChanged();
            }
        }

        public decimal? NetProfitLoss
        {
            get => _netProfitLoss;
            set
            {
                _netProfitLoss = value;
                TradeCalculator.UpdateRMultiple(this);
                OnPropertyChanged();
                OnPropertyChanged("Profit");
            }
        }

        public decimal? Profit
        {
            get { return NetProfitLoss ?? GrossProfitLoss; }
        }

        public decimal? Rollover { get; set; }

        public decimal? PricePerPip
        {
            get => _pricePerPip;
            set
            {
                _pricePerPip = value;
                OnPropertyChanged();
            }
        }

        public string Market { get; set; }
        
        public string BaseAsset { get; set; }
        
        public bool Alert { get; set; }

        public string CustomText1 { get; set; }

        public Timeframe? Timeframe
        {
            get => _timeframe;
            set
            {
                _timeframe = value;
                OnPropertyChanged();
            }
        }

        public TradeDirection? TradeDirection
        {
            get => _tradeDirection;
            set
            {
                _tradeDirection = value;
                OnPropertyChanged();
            }
        }

        public DateTime? CloseDateTime
        {
            get => _closeDateTime;
            set
            {
                _closeDateTime = value;
                OnPropertyChanged();
                OnPropertyChanged("CloseDateTimeLocal");
                OnPropertyChanged("Status");
            }
        }

        [JsonIgnore]
        public CalculateOptions CalculateOptions { get; set; } = CalculateOptions.Default;

        public TradeCloseReason? CloseReason
        {
            get => _closeReason;
            set
            {
                _closeReason = value;
                OnPropertyChanged();
                OnPropertyChanged("Status");
            }
        }


        public DateTime? OrderDateTime
        {
            get => _orderDateTime;
            set
            {
                _orderDateTime = value;
                OnPropertyChanged();
            }
        }

        public OrderType? OrderType
        {
            get => _orderType;
            set
            {
                _orderType = value;
                OnPropertyChanged();
            }
        }

        public decimal? OrderPrice
        {
            get => _orderPrice;
            set
            {
                _orderPrice = value;
                OrderPriceFloat = (float)value;

                TradeCalculator.UpdateStopPips(this);
                TradeCalculator.UpdateInitialStopPips(this);
                TradeCalculator.UpdateLimitPips(this);
                TradeCalculator.UpdateInitialLimitPips(this);

                OnPropertyChanged();
                OnPropertyChanged("Status");
            }
        }

        /// <summary>
        /// Float value used for simulation
        /// </summary>
        [JsonIgnore]
        public float? OrderPriceFloat { get; private set; }

        public List<DatePrice> OrderPrices
        {
            get => _orderPrices;
            private set
            {
                _orderPrices = value;
                OnPropertyChanged();
            }
        }

        public DateTime? OrderExpireTime
        {
            get => _orderExpireTime;
            set
            {
                _orderExpireTime = value;
                OnPropertyChanged();
            }
        }

        public decimal? OrderAmount { get; set; }

        public List<DatePrice> StopPrices
        {
            get => _stopPrices;
            private set
            {
                _stopPrices = value; 
                OnPropertyChanged();
            }
        }

        public List<DatePrice> LimitPrices
        {
            get => _limitPrices;
            private set
            {
                _limitPrices = value; 
                OnPropertyChanged();
            }
        }

        public decimal? RiskAmount
        {
            get => _riskAmount;
            set
            {
                _riskAmount = value;
                TradeCalculator.UpdateRMultiple(this);
            }
        }

        public decimal? RiskPercentOfBalance { get; set; }
        public DateTime? EntryDateTimeLocal => EntryDateTime != null ? (DateTime?)EntryDateTime.Value.ToLocalTime() : null;
        public DateTime? StartDateTimeLocal => OrderDateTime != null ? (DateTime?)OrderDateTime.Value.ToLocalTime() : EntryDateTimeLocal;
        public DateTime? StartDateTime => OrderDateTime != null ? (DateTime?)OrderDateTime.Value : EntryDateTime;

        public decimal? InitialStop
        {
            get => _initialStop;
            set
            {
                _initialStop = value;
                TradeCalculator.UpdateRMultiple(this);
                OnPropertyChanged();
            }
        }

        public decimal ProfitLatestDay
        {
            get
            {
                var candlesService = DependencyContainer.Container.GetExportedValue<IBrokersCandlesService>();
                var brokersService = DependencyContainer.Container.GetExportedValue<IBrokersService>();
                var marketDetailsService = DependencyContainer.Container.GetExportedValue<IMarketDetailsService>();
                var broker = brokersService.Brokers.FirstOrDefault(x => x.Name == Broker);

                if (broker != null)
                {
                    var marketDetails = marketDetailsService.GetMarketDetails(broker.Name, Market);

                    var now = DateTime.UtcNow;
                    var endDate = CloseDateTime != null
                        ? new DateTime(CloseDateTime.Value.Year, CloseDateTime.Value.Month, CloseDateTime.Value.Day, 23,
                            59, 59, DateTimeKind.Utc)
                        : new DateTime(now.Year, now.Month, now.Day, 23, 59, 59, DateTimeKind.Utc);
                    var startDate = new DateTime(endDate.Year, endDate.Month, endDate.Day, 0, 0, 0, DateTimeKind.Utc);
                    return this.GetTradeProfit(endDate, Basics.Timeframe.D1, candlesService, marketDetails, broker,
                               false)
                           - this.GetTradeProfit(startDate, Basics.Timeframe.D1, candlesService, marketDetails, broker,
                               false);
                }

                return decimal.MinValue;
            }
        }

        public decimal? RMultiple
        {
            get => _rMultiple;
            set
            {
                if (_rMultiple == value) return;

                _rMultiple = value;
                OnPropertyChanged();
            }
        }

        public decimal? StopPrice
        {
            get => _stopPrice;
            set
            {
                _stopPrice = value;
                StopPriceFloat = (float)value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Used to speed up simulations.
        /// </summary>
        [JsonIgnore]
        public float? StopPriceFloat { get; private set; }

        public decimal? StopInPips
        {
            get => _stopInPips;
            set
            {
                _stopInPips = value;
                OnPropertyChanged();
            }
        }

        public decimal? LimitInPips
        {
            get => _limitInPips;
            set
            {
                _limitInPips = value;
                OnPropertyChanged();
            }
        }

        public bool IsAnalysed
        {
            get => _isAnalysed;
            set
            {
                _isAnalysed = value;
                OnPropertyChanged();
            }
        }

        public decimal? LimitPrice
        {
            get => _limitPrice;
            set
            {
                _limitPrice = value;
                LimitPriceFloat = (float)value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Used to speed up simulations.
        /// </summary>
        [JsonIgnore]
        public float? LimitPriceFloat { get; private set; }

        public const int CurrentDataVersion = 1;

        public int DataVersion { get; set; } = CurrentDataVersion;

        #region Calculated properties
        public DateTime? CloseDateTimeLocal
        {
            get { return CloseDateTime != null ? (DateTime?)CloseDateTime.Value.ToLocalTime() : null; }
        }

        public DateTime? OrderDateTimeLocal
        {
            get { return OrderDateTime != null ? (DateTime?)OrderDateTime.Value.ToLocalTime() : null; }
        }

        public DateTime? OrderExpireTimeLocal
        {
            get { return OrderExpireTime != null ? (DateTime?)OrderExpireTime.Value.ToLocalTime() : null; }
        }
        #endregion

        public void SetOrder(DateTime dateTime, decimal? price, string market,
            TradeDirection tradeDirection, decimal orderAmount, DateTime? expires)
        {
            OrderDateTime = dateTime;
            AddOrderPrice(dateTime, price);
            Market = market;
            TradeDirection = tradeDirection;
            OrderExpireTime = expires;
            OrderAmount = orderAmount;
        }

        public void SetEntry(DateTime dateTime, decimal price, decimal amount)
        {
            EntryDateTime = dateTime;
            EntryPrice = price;
            EntryQuantity = amount;

            if (OrderDateTime == null)
            {
                OrderDateTime = EntryDateTime;
            }
        }

        private OrderType? _orderType;
        private List<DatePrice> _limitPrices = new List<DatePrice>();
        private List<DatePrice> _stopPrices = new List<DatePrice>();
        private List<DatePrice> _orderPrices = new List<DatePrice>();
        private string _commissionValueCurrency;
        private decimal? _commissionValue;
        private decimal? _entryValue;
        private string _entryValueCurrency;
        private decimal? _riskAmount;
        private bool _isAnalysed;

        public void AddStopPrice(DateTime date, decimal? price)
        {
            if (StopPrices.Count > 0 && StopPrices.Last().Price == price)
            {
                return;
            }

            var originalStops = StopPrices.ToList();
            if (StopPrices.Count > 0 && StopPrices.Last().Date == date)
            {
                StopPrices.RemoveAt(StopPrices.Count - 1);
            }

            StopPrices.Add(new DatePrice(date, price));

            if (StopPrices.Count > 1)
            {
                StopPrices = StopPrices.OrderBy(x => x.Date).ToList();
            }

            TradeCalculator.UpdateStop(this);
            TradeCalculator.UpdateStopPips(this);

            if (originalStops.Count == 0 || originalStops[0].Price != StopPrices[0].Price)
            {
                InitialStop = StopPrices[0].Price;
                TradeCalculator.UpdateInitialStopPips(this);
            }
        }

        public void AddOrderPrice(DateTime date, decimal? price)
        {
            if (price == null)
            {
                return;
            }

            if (OrderPrices.Count > 0 && OrderPrices.Last().Price == price)
            {
                return;
            }

            if (OrderPrices.Count > 0 && OrderPrices.Last().Date == date)
            {
                OrderPrices.RemoveAt(OrderPrices.Count - 1);
            }

            OrderPrices.Add(new DatePrice(date, price));

            if (OrderPrices.Count > 1)
            {
                OrderPrices = OrderPrices.OrderBy(x => x.Date).ToList();
            }

            OrderPrice = OrderPrices[OrderPrices.Count - 1].Price;
        }

        public void ClearStopPrices()
        {
            StopPrices.Clear();
            _currentStopPrice = null;
        }

        public void RemoveStopPrice(int index)
        {
            if (index >= StopPrices.Count)
            {
                return;
            }

            StopPrices.RemoveAt(index);
            _currentStopPrice = null;
        }

        public void AddLimitPrice(DateTime date, decimal? price)
        {
            if (LimitPrices.Count > 0 && LimitPrices.Last().Price == price)
            {
                return;
            }

            if (LimitPrices.Count > 0 && LimitPrices.Last().Date == date)
            {
                LimitPrices.RemoveAt(OrderPrices.Count - 1);
            }

            LimitPrices.Add(new DatePrice(date, price));

            if (LimitPrices.Count > 1)
            {
                LimitPrices = LimitPrices.OrderBy(x => x.Date).ToList();
            }

            TradeCalculator.UpdateLimit(this);
            TradeCalculator.UpdateLimitPips(this);

            if (LimitPrices.Count == 1)
            {
                TradeCalculator.UpdateInitialLimitPips(this);
            }
        }

        public void RemoveLimitPrice(int index)
        {
            if (index >= LimitPrices.Count)
            {
                return;
            }

            LimitPrices.RemoveAt(index);
        }

        public void SetClose(DateTime dateTime, decimal? price, TradeCloseReason reason)
        {
            ClosePrice = price;
            CloseDateTime = dateTime;
            CloseReason = reason;
        }

        public void SetExpired(DateTime dateTime)
        {
            CloseDateTime = dateTime;
            CloseReason = TradeCloseReason.HitExpiry;
        }

        public decimal? InitialStopInPips
        {
            get => _initialStopInPips;
            set
            {
                _initialStopInPips = value;
                OnPropertyChanged();
            }
        }

        public decimal? InitialLimitInPips
        {
            get => _initialLimitInPips;
            set
            {
                _initialLimitInPips = value;
                OnPropertyChanged();
            }
        }

        public decimal? InitialLimit
        {
            get => _initialLimit;
            set
            {
                _initialLimit = value;
                OnPropertyChanged();
            }
        }

        public override string ToString()
        {
            var ret = new StringBuilder();

            ret.Append($"{Market} {Broker} {TradeDirection} ");

            if (OrderDateTime != null)
            {
                ret.Append($"Order: {OrderDateTime.Value}UTC {OrderAmount:0.00}@{OrderPrice:0.0000}");
            }

            if (EntryDateTime != null)
            {
                if (ret.Length > 0)
                {
                    ret.Append(" ");
                }

                ret.Append($"Entry: {EntryDateTime.Value}UTC {EntryQuantity:0.0000} @ Price: {EntryPrice:0.0000}");
            }

            if (CloseDateTime != null)
            {
                if (ret.Length > 0)
                {
                    ret.Append(" ");
                }

                ret.Append($"Close: {CloseDateTime.Value}UTC Price: {ClosePrice:0.0000} Reason: {CloseReason}");
            }

            var initialStopInPips = InitialStopInPips;
            if (initialStopInPips != null)
            {
                if (ret.Length > 0)
                {
                    ret.Append(" ");
                }

                if (StopPrices.Count > 0)
                {
                    var stop = StopPrices.First();
                    ret.Append("Initial stop price: ");
                    ret.Append($"{stop.Date}UTC {stop.Price:0.0000} ({initialStopInPips:0}pips)");
                }
                else
                {
                    ret.Append("Initial stop price: -");
                }
            }

            if (Timeframe != null)
            {
                if (ret.Length > 0)
                {
                    ret.Append(" ");
                }

                ret.Append($"Timeframe: {Timeframe}");
            }

            if (NetProfitLoss != null)
            {
                ret.Append($" NetProfit: {NetProfitLoss}");
            }

            return ret.ToString();
        }

        public string Status
        {
            get
            {
                if (OrderPrice != null && EntryPrice == null && CloseDateTime == null)
                {
                    return "Order";
                }

                if (EntryPrice != null && CloseDateTime == null)
                {
                    return "Open";
                }

                if (CloseDateTime != null)
                {
                    switch (CloseReason)
                    {
                        case TradeCloseReason.HitExpiry:
                            return "Hit expiry";
                        case TradeCloseReason.HitLimit:
                            return "Hit limit";
                        case TradeCloseReason.HitStop:
                            return "Hit stop";
                        case TradeCloseReason.OrderClosed:
                            return "Order closed";
                        case TradeCloseReason.ManualClose:
                            return "Closed";
                    }
                }

                return "";
            }
        }

        public List<ChartLine> ChartLines { get; set; }

        public void Initialise()
        {
            if (EntryDateTime != null && OrderDateTime == null)
            {
                OrderDateTime = EntryDateTime;
            }

            if (LimitPrices == null)
            {
                LimitPrices = new List<DatePrice>();
            }

            if (LimitPrices.Count == 0 && LimitPrice != null)
            {
                var date = OrderDateTime ?? EntryDateTime;

                if (date != null)
                {
                    LimitPrices.Add(new DatePrice(date.Value, LimitPrice));
                }
            }

            // Remove duplicate stops
            if (StopPrices != null)
            {
                for (var i = StopPrices.Count - 1; i >= 1; i--)
                {
                    var current = StopPrices[i];
                    var prev = StopPrices[i - 1];

                    if (current.Price == prev.Price)
                    {
                        StopPrices.RemoveAt(i);
                    }
                }
            }

            // Remove duplicate limits
            if (LimitPrices != null)
            {
                for (var i = LimitPrices.Count - 1; i >= 1; i--)
                {
                    var current = LimitPrices[i];
                    var prev = LimitPrices[i - 1];

                    if (current.Price == prev.Price)
                    {
                        LimitPrices.RemoveAt(i);
                    }
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}