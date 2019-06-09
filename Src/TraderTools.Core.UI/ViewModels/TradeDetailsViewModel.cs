﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Windows;
using Hallupa.Library;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Core.Services;

namespace TraderTools.Core.UI.ViewModels
{
    public class TradeDetailsViewModel : INotifyPropertyChanged
    {
        #region Fields
        private readonly Action _closeWindow;
        private TradeDetails _trade;
        [Import] private BrokersService _brokersService;
        [Import] private IBrokersCandlesService _candlesService;
        private IBroker _broker;

        #endregion

        #region Constructors
        public TradeDetailsViewModel(TradeDetails trade, Action closeWindow)
        {
            _closeWindow = closeWindow;
            DependencyContainer.ComposeParts(this);

            Trade = trade;
            Date = Trade.StartDateTimeLocal != null
                ? Trade.StartDateTimeLocal.Value.ToString("dd/MM/yy HH:mm")
                : DateTime.Now.ToString("dd/MM/yy HH:mm");

            RefreshDetails();

            _broker = _brokersService.GetBroker(trade.Broker);

            AddLimitCommand= new DelegateCommand(AddLimit);
            AddStopCommand = new DelegateCommand(AddStop);
            RemoveLimitCommand = new DelegateCommand(RemoveLimit);
            RemoveStopCommand = new DelegateCommand(RemoveStop);
            SetOrderDateTimePriceCommand = new DelegateCommand(SetOrderDateTimePrice);
            DoneCommand = new DelegateCommand(o => Done());
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
        public DelegateCommand DoneCommand { get; }
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
                LimitPrices.Add(new DatePrice(limitPrice.Date.ToLocalTime(), limitPrice.Price));
            }

            StopPrices.Clear();
            foreach (var stopPrice in Trade.GetStopPrices())
            {
                StopPrices.Add(new DatePrice(stopPrice.Date.ToLocalTime(), stopPrice.Price));
            }
        }

        private void RemoveLimit(object obj)
        {
            if (SelectedLimitIndex == -1)
            {
                return;
            }

            Trade.RemoveLimitPrice(SelectedLimitIndex);
            _candlesService.UpdateTradeStopLimitPips(_broker, Trade);
            RefreshDetails();
        }

        private void RemoveStop(object obj)
        {
            if (SelectedStopIndex == -1)
            {
                return;
            }

            Trade.RemoveStopPrice(SelectedStopIndex);
            _candlesService.UpdateTradeStopLimitPips(_broker, Trade);
            RefreshDetails();
        }

        private void Done()
        {
            _closeWindow();
        }

        private void AddLimit(object obj)
        {
            var date = GetDatetime();
            var price = Trade.TradeDirection == TradeDirection.Long ? GetPrice(PipsChange.Add) : GetPrice(PipsChange.Minus);
            if (date == null || price == null)
            {
                MessageBox.Show("Invalid details", "Invalid details", MessageBoxButton.OK);
                return;
            }

            _candlesService.UpdateTradeStopLimitPips(_broker, Trade);
            Trade.AddLimitPrice(date.Value, price.Value);
            RefreshDetails();
        }

        private void AddStop(object obj)
        {
            var date = GetDatetime();
            var price = Trade.TradeDirection == TradeDirection.Long ? GetPrice(PipsChange.Minus) : GetPrice(PipsChange.Add);
            if (date == null || price == null)
            {
                MessageBox.Show("Invalid details", "Invalid details", MessageBoxButton.OK);
                return;
            }

            _candlesService.UpdateTradeStopLimitPips(_broker, Trade);
            Trade.AddStopPrice(date.Value, price.Value);
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

        private decimal? GetPrice(PipsChange pipsChange = PipsChange.Add)
        {
            if (!decimal.TryParse(Price, out var decimalPrice))
            {
                return null;
            }

            if (UsePips)
            {
                var tradeStartPrice = Trade.OrderPrice ?? Trade.EntryPrice.Value;
                var broker = _brokersService.GetBroker(Trade.Broker);
                var priceInPips = broker.GetPriceInPips(tradeStartPrice, Trade.Market);
                priceInPips += pipsChange == PipsChange.Add ? decimalPrice : -decimalPrice;

                return _candlesService.GetPriceFromPips(_broker, priceInPips, Trade.Market);
            }

            return decimalPrice;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private DateTime? GetDatetime()
        {
            if (DateTime.TryParse(Date, out var date))
            {
                return date.ToUniversalTime();
            }

            return null;
        }
    }
}