using System;
using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.Test.Fakes
{
    public class FakeMarketDetailsService : IMarketDetailsService
    {
        public Basics.MarketDetails GetMarketDetails(string broker, string market)
        {
            throw new NotImplementedException();
        }

        public List<Basics.MarketDetails> GetAllMarketDetails()
        {
            throw new NotImplementedException();
        }

        public void AddMarketDetails(Basics.MarketDetails marketDetails)
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