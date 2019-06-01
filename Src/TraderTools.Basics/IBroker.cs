using System;
using System.Collections.Generic;

namespace TraderTools.Basics
{
    public enum ConnectStatus
    {
        None,
        Connecting,
        Connected,
        Disconnecting,
        Disconnected
    }

    public enum BrokerKind
    {
        SpreadBet,
        Trade
    }

    public interface IBrokerAccount
    {
        List<TradeDetails> Trades { get; set; }

        decimal GetBalance(DateTime? dateTimeUtc = null);

        List<DepositWithdrawal> DepositsWithdrawals { get; set; }
    }

    public interface IBroker
    {
        string Name { get; }
        bool UpdateAccount(IBrokerAccount account, IBrokersCandlesService candlesService);
        bool UpdateCandles(List<ICandle> candles, string market, Timeframe timeframe, DateTime start);
        void Connect();
        ConnectStatus Status { get; }
        BrokerKind Kind { get; }
        decimal GetOnePipInDecimals(string market);
        List<TickData> GetTickData(IBroker broker, string market, DateTime utcStart, DateTime utcEnd);

        decimal GetGBPPerPip(decimal amount, string market, DateTime date, IBrokersCandlesService candleService, IBroker broker, bool updateCandles);
    }
}