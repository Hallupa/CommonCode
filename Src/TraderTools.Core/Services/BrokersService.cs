﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using log4net;
using TraderTools.Basics;
using TraderTools.Core.Broker;

namespace TraderTools.Core.Services
{
    [Export(typeof(BrokersService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class BrokersService :  IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        public Dictionary<IBroker, BrokerAccount> AccountsLookup { get; private set; } = new Dictionary<IBroker, BrokerAccount>();
        private bool _disposedValue = false;
        public List<IBroker> Brokers { get; } = new List<IBroker>();

        public void AddBrokers(IEnumerable<IBroker> brokers)
        {
            foreach (var broker in brokers)
            {
                Brokers.Add(broker);
            }
        }

        public void Connect()
        {
            foreach (var broker in Brokers)
            {
                broker.Connect();
            }
        }

        public void LoadBrokerAccounts(ITradeDetailsAutoCalculatorService tradeCalculatorService, DataDirectoryService dataDirectoryService)
        {
            foreach (var broker in Brokers)
            {
                var account = BrokerAccount.LoadAccount(broker, tradeCalculatorService, dataDirectoryService);
                
                if (account == null)
                {
                    account = new BrokerAccount
                    {
                        BrokerName = broker.Name
                    };
                }

                AccountsLookup[broker] = account;
            }
        }

        public IBroker GetBroker(string broker)
        {
            return Brokers.FirstOrDefault(b => b.Name == broker);
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                }

                foreach (var broker in Brokers)
                {
                    var disposable = broker as IDisposable;
                    if (disposable != null)
                    {
                        disposable.Dispose();
                    }
                }

                Brokers.Clear();

                _disposedValue = true;
            }
        }

        ~BrokersService()
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