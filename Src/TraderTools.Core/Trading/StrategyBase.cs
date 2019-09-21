using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Hallupa.Library;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
using TraderTools.Core.Services;

namespace TraderTools.Core.Trading
{
    public abstract class StrategyBase : IStrategy
    {
        [Import] protected ITradeDetailsAutoCalculatorService _calculator;
        [Import] protected IBrokersService _brokersService;
        [Import] protected IBrokersCandlesService _candlesService;
        [Import] protected IMarketDetailsService _marketDetailsService;
        [Import] protected MarketsService _marketsService;
        private IBroker _broker;
        private IBrokerAccount _account;

        protected StrategyBase()
        {
            DependencyContainer.ComposeParts(this);

            _broker = _brokersService.Brokers.First(x => x.Name == "FXCM");
            _account = _brokersService.AccountsLookup.ContainsKey(_broker) ? _brokersService.AccountsLookup[_broker] : null;
        }

        public bool UseRiskSize { get; set; } = true;

        public abstract string Name { get; }

        public abstract List<Trade> CreateNewTrades(
            MarketDetails market, TimeframeLookup<List<CandleAndIndicators>> candlesLookup, List<Trade> existingTrades, ITradeDetailsAutoCalculatorService calculatorService);

        protected Trade CreateMarketOrder(string market, TradeDirection direction, Candle currentCandle, decimal stop, decimal riskPercent, decimal? limit = null)
        {
            int? lotSize = 1000;
            var entryPrice = direction == TradeDirection.Long ? currentCandle.CloseAsk : currentCandle.CloseBid;

            if (UseRiskSize)
            {
                if (!GetLotSize(market, (decimal)entryPrice, stop, riskPercent, out lotSize)) return null;
            }

            if (lotSize == null) return null;

            var trade = Trade.CreateMarketEntry(
                "FXCM", (decimal)entryPrice, currentCandle.CloseTime(), direction, lotSize.Value, market, stop, limit,
                _calculator);

            return trade;
        }

        protected Trade CreateOrder(
            string market, DateTime? expiryDateTime, decimal entryPrice, TradeDirection direction, decimal currentPrice, DateTime currentDateTime,
            decimal? limit, decimal stop, decimal riskPercent)
        {
            int? lotSize = 1000;

            if (UseRiskSize)
            {
                if (!GetLotSize(market, entryPrice, stop, riskPercent, out lotSize)) return null;
            }

            if (lotSize == null) return null;

            var trade = Trade.CreateOrder(
                "FXCM",
                entryPrice,
                currentDateTime,
                OrderKind.EntryPrice,
                direction,
                (decimal)lotSize.Value,
                market,
                expiryDateTime,
                stop,
                limit,
                Timeframe.D1,
                string.Empty, 
                null, 
                0,
                0,
                0,
                0,
                false,
                (direction == TradeDirection.Long && entryPrice < currentPrice) || (direction == TradeDirection.Short && entryPrice > currentPrice) ? OrderType.LimitEntry : OrderType.StopEntry,
                _calculator);

            return trade;
        }

        private bool GetLotSize(string market, decimal entryPrice, decimal stop, decimal riskPercent, out int? lotSize)
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

        private  int? GetLotSize(string market, decimal targetRiskGBP, decimal stopPips, int baseUnitSize)
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
        }
    }
}