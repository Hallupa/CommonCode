using System;
using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Test
{
    public class FakeCandlesService : IBrokersCandlesService
    {
        public List<ICandle> GetCandles(IBroker broker, string market, Timeframe timeframe, bool updateCandles, DateTime? minOpenTimeUtc = null,
            DateTime? maxCloseTimeUtc = null, bool cacheData = true)
        {
            return new List<ICandle>
            {
                new Candle
                {
                    Open = 1,
                    High = 1,
                    Low = 1,
                    Close = 1,
                    CloseTimeTicks = 1
                }
            };
        }

        public void UpdateCandles(IBroker broker, string market, Timeframe timeframe)
        {
            throw new NotImplementedException();
        }

        public void UnloadCandles(string market, Timeframe timeframe, IBroker broker)
        {
            throw new NotImplementedException();
        }

        public MarketDetails GetMarketDetails(string broker, string market)
        {
            throw new NotImplementedException();
        }

        public void AddMarketDetails(MarketDetails marketDetails)
        {
            throw new NotImplementedException();
        }
        
        public bool HasMarketDetails(string broker, string market)
        {
            throw new NotImplementedException();
        }

        public void SaveMarketDetailsList()
        {
            throw new NotImplementedException();
        }
    }
}