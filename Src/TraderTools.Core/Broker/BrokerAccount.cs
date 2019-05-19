using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using log4net;
using TraderTools.Basics;
using TraderTools.Core.Services;
using TraderTools.Core.Trading;

namespace TraderTools.Core.Broker
{
    public class BrokerAccountUpdated
    {
        public BrokerAccountUpdated(BrokerAccount account)
        {
            Account = account;
        }

        public BrokerAccount Account { get; }
    }

    public class BrokerAccount : IBrokerAccount
    {
        #region Fields
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public string BrokerName { get; set; }

        public DateTime? AccountLastUpdated { get; set; }

        public List<TradeDetails> Trades { get; set; } = new List<TradeDetails>();

        private Subject<BrokerAccountUpdated> _brokerAccountUpdatedSubject = new Subject<BrokerAccountUpdated>();

        public List<DepositWithdrawal> DepositsWithdrawals { get; set; } = new List<DepositWithdrawal>();
        #endregion

        public IObservable<BrokerAccountUpdated> AccountUpdatedObservable => _brokerAccountUpdatedSubject.AsObservable();

        public static BrokerAccount LoadAccount(string dataPath, string brokerName)
        {
            var accountPath = Path.Combine(dataPath, "BrokerAccounts", $"{brokerName}_Account.json");

            if (!File.Exists(accountPath))
            {
                return null;
            }

            var account = JsonConvert.DeserializeObject<BrokerAccount>(File.ReadAllText(accountPath));

            foreach (var trade in account.Trades)
            {
                trade.Initialise();
            }

            return account;
        }

        public decimal GetBalance(DateTime? dateTimeUtc = null)
        {
            var depositWithdrawTotal = DepositsWithdrawals
                .Where(x => dateTimeUtc == null || x.Time <= dateTimeUtc)
                .Sum(x => x.Amount);

            if (dateTimeUtc == null)
            {
                return Trades.Where(x => x.NetProfitLoss != null).Sum(x => x.NetProfitLoss.Value) + depositWithdrawTotal;
            }

            var openTradesTotal = Trades
                .Where(x => x.EntryDateTime != null && x.CloseDateTime == null && x.NetProfitLoss != null)
                .Sum(x => x.NetProfitLoss.Value);
            var closedTradesTotal = Trades
                .Where(x => x.CloseDateTime != null && x.NetProfitLoss != null && x.CloseDateTime.Value <= dateTimeUtc)
                .Sum(x => x.NetProfitLoss.Value);
            return depositWithdrawTotal + openTradesTotal + closedTradesTotal;
        }

        public void SaveAccount(string dataPath)
        {
            var mainPath = Path.Combine(dataPath, "BrokerAccounts");

            if (!Directory.Exists(mainPath))
            {
                Directory.CreateDirectory(mainPath);
            }

            // Rename backups
            int maxBackups = 50;
            for (var i = maxBackups; i >= 1; i--)
            {
                var accountBackupPath = Path.Combine(mainPath, $"{BrokerName}_Account_{i}.json");

                if (i == maxBackups && File.Exists(accountBackupPath))
                {
                    File.Delete(accountBackupPath);
                }

                if (i != maxBackups && File.Exists(accountBackupPath))
                {
                    var accountNewBackupPath = Path.Combine(mainPath, $"{BrokerName}_Account_{i + 1}.json");
                    File.Move(accountBackupPath, accountNewBackupPath);
                }
            }

            var accountFinalPath = Path.Combine(mainPath, $"{BrokerName}_Account.json");
            if (File.Exists(accountFinalPath))
            {
                var accountBackupPath = Path.Combine(mainPath, $"{BrokerName}_Account_1.json");
                File.Move(accountFinalPath, accountBackupPath);
            }

            var accountTmpPath = Path.Combine(mainPath, $"{BrokerName}_Account_tmp.json");
            File.WriteAllText(accountTmpPath, JsonConvert.SerializeObject(this));

            File.Copy(accountTmpPath, accountFinalPath);

            File.Delete(accountTmpPath);
        }

        public enum UpdateOption
        {
            OnlyIfNotRecentlyUpdated,
            ForceUpdate
        }

        public void UpdateBrokerAccount(IBroker broker, IBrokersCandlesService candleService, UpdateOption option = UpdateOption.OnlyIfNotRecentlyUpdated)
        {
            if (option == UpdateOption.OnlyIfNotRecentlyUpdated && (AccountLastUpdated != null && (DateTime.UtcNow - AccountLastUpdated.Value).TotalHours < 24))
            {
                return;
            }

            Log.Info($"Updating {broker.Name} account");

            broker.UpdateAccount(this);

            AccountLastUpdated = DateTime.UtcNow;

            foreach (var trade in Trades)
            {
                trade.Initialise();
                trade.UpdatePricePerPip(broker, candleService, true);
                trade.UpdateRisk(this);
            }

            Log.Info($"Completed updating {broker.Name} trades");
            _brokerAccountUpdatedSubject.OnNext(new BrokerAccountUpdated(this));
        }

        public void RemoveTrade(TradeDetails tradeDetails)
        {
            Trades.Remove(tradeDetails);
            _brokerAccountUpdatedSubject.OnNext(new BrokerAccountUpdated(this));
        }
    }
}