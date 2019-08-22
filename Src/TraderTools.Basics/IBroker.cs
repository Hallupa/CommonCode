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
        List<Trade> Trades { get; set; }

        decimal GetBalance(DateTime? dateTimeUtc = null);

        List<DepositWithdrawal> DepositsWithdrawals { get; set; }
    }

    public interface IBroker
    {
        string Name { get; }
        bool UpdateAccount(IBrokerAccount account, IBrokersCandlesService candlesService, IMarketDetailsService marketsService, Action<string> updateProgressAction);
        bool UpdateCandles(List<Candle> candles, string market, Timeframe timeframe, DateTime start);
        void Connect();
        ConnectStatus Status { get; }
        BrokerKind Kind { get; }
        List<TickData> GetTickData(IBroker broker, string market, DateTime utcStart, DateTime utcEnd);
        List<MarketDetails> GetMarketDetailsList();
        Candle? GetSingleCandle(string market, Timeframe timeframe, DateTime date);
    }
}