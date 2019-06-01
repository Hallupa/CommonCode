using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using Hallupa.Library;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Core.Services;

namespace TraderTools.Core.UI.ViewModels
{
    public class TradeDetailsViewModel : INotifyPropertyChanged
    {
        #region Fields
        private TradeDetails _trade;
        [Import] private BrokersService _brokersService;
        private IBroker _broker;

        #endregion

        #region Constructors
        public TradeDetailsViewModel(TradeDetails trade)
        {
            DependencyContainer.ComposeParts(this);

            Trade = trade;
            Date = Trade.StartDateTime != null
                ? Trade.StartDateTime.Value.ToString("dd/MM/yy HH:mm")
                : DateTime.UtcNow.ToString("dd/MM/yy HH:mm");

            RefreshDetails();

            _broker = _brokersService.GetBroker(trade.Broker);

            AddLimitCommand= new DelegateCommand(AddLimit);
            AddStopCommand = new DelegateCommand(AddStop);
            RemoveLimitCommand = new DelegateCommand(RemoveLimit);
            RemoveStopCommand = new DelegateCommand(RemoveStop);
            SetOrderDateTimePriceCommand = new DelegateCommand(SetOrderDateTimePrice);
        }
        #endregion

        #region Properties

        public TradeDetails Trade
        {
            get => _trade;
            set
            {
                _trade = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<DatePrice> LimitPrices { get; } = new ObservableCollection<DatePrice>();
        public ObservableCollection<DatePrice> StopPrices { get; } = new ObservableCollection<DatePrice>();
        public DelegateCommand AddLimitCommand { get; }
        public DelegateCommand AddStopCommand { get; }
        public DelegateCommand RemoveLimitCommand { get; }
        public DelegateCommand RemoveStopCommand { get; }
        public DelegateCommand SetOrderDateTimePriceCommand { get; }
        public int SelectedLimitIndex { get; set; }
        public int SelectedStopIndex { get; set; }
        public string Date { get; set; }
        public string Price { get; set; }
        public bool UsePips { get; set; }
        #endregion

        private void RefreshDetails()
        {
            DependencyContainer.ComposeParts(this);

            LimitPrices.Clear();
            foreach (var limitPrice in Trade.GetLimitPrices())
            {
                LimitPrices.Add(new DatePrice(limitPrice.Date, limitPrice.Price));
            }

            StopPrices.Clear();
            foreach (var stopPrice in Trade.GetStopPrices())
            {
                StopPrices.Add(new DatePrice(stopPrice.Date, stopPrice.Price));
            }
        }

        private void RemoveLimit(object obj)
        {
            if (SelectedLimitIndex == -1)
            {
                return;
            }

            Trade.RemoveLimitPrice(SelectedLimitIndex);
            _broker.UpdateTradeStopLimitPips(Trade);
            RefreshDetails();
        }

        private void RemoveStop(object obj)
        {
            if (SelectedStopIndex == -1)
            {
                return;
            }

            Trade.RemoveStopPrice(SelectedStopIndex);
            _broker.UpdateTradeStopLimitPips(Trade);
            RefreshDetails();
        }

        private void AddLimit(object obj)
        {
            Trade.AddLimitPrice(GetDatetime(), Trade.TradeDirection == TradeDirection.Long ? GetPrice(PipsChange.Add) : GetPrice(PipsChange.Minus));
            _broker.UpdateTradeStopLimitPips(Trade);
            RefreshDetails();
        }

        private void AddStop(object obj)
        {
            Trade.AddStopPrice(GetDatetime(), Trade.TradeDirection == TradeDirection.Long ? GetPrice(PipsChange.Minus) : GetPrice(PipsChange.Add));
            _broker.UpdateTradeStopLimitPips(Trade);
            RefreshDetails();
        }

        private void SetOrderDateTimePrice(object obj)
        {
            Trade.OrderPrice = GetPrice();
            Trade.OrderDateTime = GetDatetime();

            // Refresh by changing Trade
            var t = Trade;
            Trade = null;
            Trade = t;
        }

        public enum PipsChange
        {
            Add,
            Minus
        }

        private decimal GetPrice(PipsChange pipsChange = PipsChange.Add)
        {
            if (UsePips)
            {
                var price = Trade.OrderPrice ?? Trade.EntryPrice.Value;
                var broker = _brokersService.GetBroker(Trade.Broker);
                var priceInPips = broker.GetPriceInPips(price, Trade.Market);
                priceInPips += pipsChange == PipsChange.Add ? decimal.Parse(Price) : -decimal.Parse(Price);

                return broker.GetPriceFromPips(priceInPips, Trade.Market);
            }

            return decimal.Parse(Price);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private DateTime GetDatetime()
        {
            var initialDate = DateTime.Parse(Date);

            return new DateTime(initialDate.Year, initialDate.Month, initialDate.Day, initialDate.Hour, initialDate.Minute,
                initialDate.Second, DateTimeKind.Utc);
        }
    }
}