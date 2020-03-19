﻿using System;
using System.Collections.Generic;

namespace TraderTools.Basics
{
    public interface IBrokersCandlesService
    {
        List<Candle> GetCandles(IBroker broker, string market, Timeframe timeframe, bool updateCandles, DateTime? minOpenTimeUtc = null,
            DateTime? maxCloseTimeUtc = null, bool cacheData = true, bool forceUpdate = false, Action<string> progressUpdate = null);

        void UpdateCandles(IBroker broker, string market, Timeframe timeframe, bool forceUpdate = true);

        void UnloadCandles(string market, Timeframe timeframe, IBroker broker);

        string GetBrokerCandlesPath(IBroker broker, string market, Timeframe timeframe);
    }
}