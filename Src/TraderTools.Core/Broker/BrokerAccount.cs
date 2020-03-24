using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Windows.Forms;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Core.Services;

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

        public List<Trade> Trades { get; set; } = new List<Trade>();

        [JsonIgnore]
        [Import] private IDataDirectoryService _dataDirectoryService;

        private Subject<BrokerAccountUpdated> _brokerAccountUpdatedSubject = new Subject<BrokerAccountUpdated>();

        public List<DepositWithdrawal> DepositsWithdrawals { get; set; } = new List<DepositWithdrawal>();
        #endregion

        public IObservable<BrokerAccountUpdated> AccountUpdatedObservable => _brokerAccountUpdatedSubject.AsObservable();

        public BrokerAccount()
        {
            DependencyContainer.ComposeParts(this);
        }

        public static BrokerAccount LoadAccount(
            IBroker broker,
            ITradeDetailsAutoCalculatorService tradeCalculatorService,
            IDataDirectoryService dataDirectoryService)
        {
            var accountPath = Path.Combine(dataDirectoryService.MainDirectoryWithApplicationName, "BrokerAccounts", $"{broker.Name}_Account.json");

            if (!File.Exists(accountPath))
            {
                return null;
            }

            var account = JsonConvert.DeserializeObject<BrokerAccount>(File.ReadAllText(accountPath));
            DependencyContainer.ComposeParts(account);

            foreach (var trade in account.Trades)
            {
                trade.Initialise();

                if (trade.DataVersion == 0)
                {
                    tradeCalculatorService.AddTrade(trade);
                    trade.DataVersion = Trade.CurrentDataVersion;
                }
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

        public void SaveAccount()
        {
            var mainPath = Path.Combine(_dataDirectoryService.MainDirectoryWithApplicationName, "BrokerAccounts");

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

        public void UpdateBrokerAccount(
            IBroker broker,
            IBrokersCandlesService candleService,
            IMarketDetailsService marketsService,
            ITradeDetailsAutoCalculatorService tradeCalculateService,
            UpdateOption option = UpdateOption.OnlyIfNotRecentlyUpdated)
        {
            void UpdateProgressAction(string txt)
            {
            }

            UpdateBrokerAccount(broker, candleService, marketsService, tradeCalculateService, UpdateProgressAction, option);
        }

        public void UpdateBrokerAccount(
            IBroker broker,
            IBrokersCandlesService candleService,
            IMarketDetailsService marketsService,
            ITradeDetailsAutoCalculatorService tradeCalculateService,
            Action<string> updateProgressAction,
            UpdateOption option = UpdateOption.OnlyIfNotRecentlyUpdated)
        {
            if (option == UpdateOption.OnlyIfNotRecentlyUpdated && (AccountLastUpdated != null && (DateTime.UtcNow - AccountLastUpdated.Value).TotalHours < 24))
            {
                return;
            }

            Log.Info($"Updating {broker.Name} account");

            foreach (var t in Trades)
            {
                tradeCalculateService.RemoveTrade(t);
            }

            try
            {
                broker.UpdateAccount(this, candleService, marketsService, updateProgressAction, AccountLastUpdated);
            }
            catch (Exception ex)
            {
                Log.Error("Unable to update account", ex);
                MessageBox.Show($"Unable to update account - {ex.Message}", "Unable to update account", MessageBoxButtons.OK);
            }

            AccountLastUpdated = DateTime.UtcNow;

            foreach (var trade in Trades)
            {
                trade.Initialise();
                tradeCalculateService.AddTrade(trade);
            }

            Log.Info($"Completed updating {broker.Name} trades");
            _brokerAccountUpdatedSubject.OnNext(new BrokerAccountUpdated(this));
        }

        public void SetTrades(List<Trade> trades, ITradeDetailsAutoCalculatorService tradeCalculateService, IBroker broker)
        {
            AccountLastUpdated = DateTime.UtcNow;

            foreach (var t in Trades)
            {
                tradeCalculateService.RemoveTrade(t);
            }

            Trades.Clear();
            Trades.AddRange(trades);

            foreach (var trade in Trades)
            {
                trade.Initialise();
                tradeCalculateService.AddTrade(trade);
            }

            Log.Info($"Completed updating {broker.Name} trades");
            _brokerAccountUpdatedSubject.OnNext(new BrokerAccountUpdated(this));
        }

        public void RemoveTrade(Trade tradeDetails)
        {
            Trades.Remove(tradeDetails);
            _brokerAccountUpdatedSubject.OnNext(new BrokerAccountUpdated(this));
        }
    }
}