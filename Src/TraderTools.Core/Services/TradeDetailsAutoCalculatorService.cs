using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Core.Broker;

namespace TraderTools.Core.Services
{
    [Export(typeof(ITradeDetailsAutoCalculatorService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class TradeDetailsAutoCalculatorService : ITradeDetailsAutoCalculatorService
    {
        private List<TradeDetails> _calculatingTrades = new List<TradeDetails>();

        [Import] private IBrokersCandlesService candlesService;
        [Import] private BrokersService _brokersService;
        [Import] private IMarketDetailsService _marketsService;

        public void AddTrade(TradeDetails trade)
        {
            // In-case the trade is already setup, remove then re-add notifications
            trade.PropertyChanged -= TradeOnPropertyChanged;
            trade.PropertyChanged += TradeOnPropertyChanged;
            TradeOnPropertyChanged(trade, new PropertyChangedEventArgs(string.Empty));
        }

        public void RemoveTrade(TradeDetails trade)
        {
            trade.PropertyChanged -= TradeOnPropertyChanged;
        }

        private void TradeOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var trade = (TradeDetails)sender;

            lock (_calculatingTrades)
            {
                if (_calculatingTrades.Contains(trade))
                {
                    return;
                }

                _calculatingTrades.Add(trade);
            }

            try
            {
                RecalculateTrade(trade);
            }
            finally
            {
                lock (_calculatingTrades)
                {
                    _calculatingTrades.Remove(trade);
                }
            }
        }

        private void RecalculateTrade(TradeDetails trade)
        {
            var startTime = trade.OrderDateTime ?? trade.EntryDateTime;
            var broker = _brokersService.Brokers.FirstOrDefault(x => x.Name == trade.Broker);

            BrokerAccount brokerAccount = null;
            if (broker != null)
            {
                _brokersService.AccountsLookup.TryGetValue(broker, out brokerAccount);
            }

            if (startTime == null)
            {
                trade.StopInPips = null;
                trade.InitialStopInPips = null;
                trade.InitialStop = null;
                trade.LimitInPips = null;
                trade.InitialLimitInPips = null;
                trade.InitialLimit = null;
                trade.RiskPercentOfBalance = null;
                trade.RiskAmount = null;
                trade.RMultiple = null;
                return;
            }

            // Initial stop is stop at entry point or order point
            if (trade.StopPrices.Count > 0)
            {
                DatePrice entryOrOrderStop = null;

                // Get entry or order stop price
                if (trade.EntryDateTime == null)
                {
                    entryOrOrderStop = trade.StopPrices[0];
                }
                else
                {
                    entryOrOrderStop = trade.StopPrices[0];
                    for (var i = 1; i < trade.StopPrices.Count; i++)
                    {
                        if (trade.StopPrices[i].Date > trade.EntryDateTime.Value) break;
                        entryOrOrderStop = trade.StopPrices[i];
                    }
                }

                var stop = entryOrOrderStop;
                
                // Update initial stop pips
                if (trade.EntryPrice != null || trade.OrderPrice != null)
                {
                    var price = trade.EntryPrice ?? trade.OrderPrice.Value;

                    if (stop.Price != null)
                    {
                        var stopInPips = Math.Abs(
                            _marketsService.GetPriceInPips(trade.Broker, stop.Price.Value, trade.Market) -
                            _marketsService.GetPriceInPips(trade.Broker, price, trade.Market));
                        trade.InitialStopInPips = stopInPips;
                        trade.InitialStop = entryOrOrderStop.Price;
                    }
                    else
                    {
                        trade.InitialStopInPips = null;
                        trade.InitialStop = null;
                    }

                    // Update current stop
                    stop = trade.StopPrices.Last();
                    if (stop.Price != null)
                    {
                        var stopInPips = Math.Abs(
                            _marketsService.GetPriceInPips(trade.Broker, stop.Price.Value, trade.Market) -
                            _marketsService.GetPriceInPips(trade.Broker, price, trade.Market));
                        trade.StopInPips = stopInPips;
                    }
                    else
                    {
                        trade.StopInPips = null;
                    }
                }
                else
                {
                    trade.StopInPips = null;
                    trade.InitialStopInPips = null;
                    trade.InitialStop = null;
                }

                trade.StopPrice = stop.Price;
            }
            else
            {
                trade.StopInPips = null;
                trade.StopPrice = null;
                trade.InitialStopInPips = null;
                trade.InitialStop = null;
            }

            // Update limit pips
            if (trade.LimitPrices.Count > 0)
            {
                DatePrice entryOrOrderLimit = null;

                // Get entry or order limit price
                if (trade.EntryDateTime == null)
                {
                    entryOrOrderLimit = trade.LimitPrices[0];
                }
                else
                {
                    entryOrOrderLimit = trade.LimitPrices[0];
                    for (var i = 1; i < trade.LimitPrices.Count; i++)
                    {
                        if (trade.LimitPrices[i].Date > trade.EntryDateTime.Value) break;
                        entryOrOrderLimit = trade.LimitPrices[i];
                    }
                }

                // Update initial limit
                if (trade.EntryPrice != null || trade.OrderPrice != null)
                {
                    var price = trade.EntryPrice ?? trade.OrderPrice.Value;
                    var limit = entryOrOrderLimit;
                    var limitInPips = Math.Abs(
                        _marketsService.GetPriceInPips(trade.Broker, limit.Price.Value, trade.Market) -
                        _marketsService.GetPriceInPips(trade.Broker, price, trade.Market));
                    trade.InitialLimitInPips = limitInPips;

                    // Update current limit
                    limit = trade.LimitPrices.Last();
                    if (limit.Price != null)
                    {
                        limitInPips = Math.Abs(
                            _marketsService.GetPriceInPips(trade.Broker, limit.Price.Value, trade.Market) -
                            _marketsService.GetPriceInPips(trade.Broker, price, trade.Market));
                        trade.LimitInPips = limitInPips;
                        trade.LimitPrice = limit.Price;
                    }
                    else
                    {
                        trade.LimitInPips = null;
                        trade.LimitPrice = null;
                    }
                }
                else
                {
                    trade.LimitInPips = null;
                    trade.LimitPrice = null;
                }

                trade.InitialLimit = entryOrOrderLimit.Price;

            }
            else
            {
                trade.LimitInPips = null;
                trade.LimitPrice = null;
                trade.InitialLimitInPips = null;
            }

            // Update price/pip
            if (trade.EntryQuantity != null && trade.EntryDateTime != null)
            {
                if (broker != null)
                {
                    trade.PricePerPip = candlesService.GetGBPPerPip(_marketsService, broker, trade.Market,
                        trade.EntryQuantity.Value, trade.EntryDateTime.Value, true);
                }
                else
                {
                    trade.PricePerPip = null;
                }
            }

            // Update risk
            if (trade.InitialStopInPips == null || trade.PricePerPip == null)
            {
                trade.RiskPercentOfBalance = null;
                trade.RiskAmount = null;
                trade.RiskPercentOfBalance = null;
            }
            else
            {
                trade.RiskAmount = trade.PricePerPip.Value * trade.InitialStopInPips.Value;

                if (brokerAccount != null)
                {
                    var balance = brokerAccount.GetBalance(trade.StartDateTime);
                    if (balance != 0.0M)
                    {
                        trade.RiskPercentOfBalance = (trade.RiskAmount * 100M) / brokerAccount.GetBalance(startTime);
                    }
                    else
                    {
                        trade.RiskPercentOfBalance = null;
                    }
                }
            }

            // Update RMultiple
            if (trade.RiskAmount != null && trade.RiskAmount.Value != 0M && trade.Profit != null)
            {
                trade.RMultiple = trade.Profit / trade.RiskAmount;
            }
            else if (trade.EntryPrice != null && trade.ClosePrice != null && trade.InitialStop != null)
            {
                var stopPrice = trade.InitialStop.Value;
                var risk = Math.Abs(stopPrice - trade.EntryPrice.Value);
                if (trade.TradeDirection == TradeDirection.Long)
                {
                    var gainOrLoss = Math.Abs(trade.ClosePrice.Value - trade.EntryPrice.Value);
                    trade.RMultiple = (gainOrLoss / risk) * (trade.ClosePrice.Value > trade.EntryPrice.Value ? 1 : -1);
                }
                else
                {
                    var gainOrLoss = Math.Abs(trade.ClosePrice.Value - trade.EntryPrice.Value);
                    trade.RMultiple = (gainOrLoss / risk) * (trade.ClosePrice.Value > trade.EntryPrice.Value ? -1 : 1);
                }
            }
            else
            {
                trade.RMultiple = null;
            }
        }
    }
}