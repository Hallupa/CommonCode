﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

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

    public class ChartLine
    {
        public DateTime DateTimeUTC1 { get; set; }
        public DateTime DateTimeUTC2 { get; set; }
        public decimal Price1 { get; set; }
        public decimal Price2 { get; set; }
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

    public class DatePrice
    {
        public DatePrice(DateTime date, decimal? price)
        {
            Date = date;
            Price = price;
        }

        public DatePrice()
        {
        }

        public DateTime Date { get; set; }
        public decimal? Price { get; set; }
    }

    public class TradeDetails : INotifyPropertyChanged
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

        public TradeDetails()
        {
        }

        public static TradeDetails CreateOrder(string broker, decimal entryOrder, DateTime entryOrderTime, OrderKind orderKind,
            TradeDirection direction, decimal amount, string market, DateTime? orderExpireTime,
            decimal? stop, decimal? limit, Timeframe timeframe, string strategies, string comments, int custom1,
            int custom2, int custom3, int custom4, bool alert, OrderType orderType, ITradeDetailsAutoCalculatorService tradeCalculatorService)
        {
            var trade = new TradeDetails();
            trade.SetOrder(entryOrderTime, entryOrder, market, timeframe, direction, amount, orderExpireTime);
            if (stop != null) trade.AddStopPrice(entryOrderTime, stop.Value);
            if (limit != null) trade.AddLimitPrice(entryOrderTime, limit.Value);
            trade.Timeframe = timeframe;
            trade.Broker = broker;
            trade.Alert = alert;
            trade.Comments = comments;
            trade.Strategies = strategies;
            trade.Custom1 = custom1;
            trade.Custom2 = custom2;
            trade.Custom3 = custom3;
            trade.Custom4 = custom4;
            trade.OrderKind = orderKind;
            trade.OrderType = orderType;
            tradeCalculatorService.AddTrade(trade);
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
            get => _strategies;
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
        
        public int Custom1 { get; set; }
        
        public int Custom2 { get; set; }
        
        public int Custom3 { get; set; }
        
        public int Custom4 { get; set; }
        
        public int Custom5 { get; set; }

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
                OnPropertyChanged();
                OnPropertyChanged("Status");
            }
        }

        public List<DatePrice> OrderPrices
        {
            get => _orderPrices;
            set
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
            set
            {
                _stopPrices = value; 
                OnPropertyChanged();
            }
        }

        public List<DatePrice> LimitPrices
        {
            get => _limitPrices;
            set
            {
                _limitPrices = value; 
                OnPropertyChanged();
            }
        }

        public decimal? RiskAmount { get; set; }
        
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
                OnPropertyChanged();
            }
        }

        public decimal? RMultiple
        {
            get => _rMultiple;
            set
            {
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
                OnPropertyChanged();
            }
        }

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

        public decimal? LimitPrice
        {
            get => _limitPrice;
            set
            {
                _limitPrice = value; 
                OnPropertyChanged();
            }
        }

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
            Timeframe timeframe, TradeDirection tradeDirection, decimal orderAmount, DateTime? expires)
        {
            OrderDateTime = dateTime;
            AddOrderPrice(dateTime, price);
            Timeframe = timeframe;
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

        public void AddStopPrice(DateTime date, decimal? price)
        {
            if (StopPrices.Count > 0 && StopPrices.Last().Price == price)
            {
                return;
            }

            StopPrices.Add(new DatePrice(date, price));
            StopPrices = StopPrices.OrderBy(x => x.Date).ToList();
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

            OrderPrices.Add(new DatePrice(date, price));
            OrderPrices = OrderPrices.OrderBy(x => x.Date).ToList();
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

            LimitPrices.Add(new DatePrice(date, price));
            LimitPrices = LimitPrices.OrderBy(x => x.Date).ToList();
        }

        public void ClearLimitPrices()
        {
            LimitPrices.Clear();
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

                var stop = StopPrices.First();
                ret.Append("Initial stop price: ");
                ret.Append($"{stop.Date}UTC {stop.Price:0.0000} ({initialStopInPips:0}pips)");
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