using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using TraderTools.Basics;

namespace TraderTools.Simulation
{
    [Export(typeof(ITradeCacheService))]
    [PartCreationPolicy(CreationPolicy.Shared)]

    public class TradeCacheService : ITradeCacheService
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly IDataDirectoryService _dataDirectoryService;
        private readonly Dictionary<string, Dictionary<CachedTradeKey, CachedTradeResult>> _cachedTrades = new Dictionary<string, Dictionary<CachedTradeKey, CachedTradeResult>>();
		private readonly HashSet<string> _marketsRequiringSaving = new HashSet<string>();

        [ImportingConstructor]
        public TradeCacheService(IDataDirectoryService dataDirectoryService)
        {
            _dataDirectoryService = dataDirectoryService;
            var directory = Path.Combine(_dataDirectoryService.MainDirectory, "TradeCache");
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }
		
		private string GetPath(string market)
		{
            return Path.Combine(_dataDirectoryService.MainDirectory, "TradeCache", $"Trades-{market.Replace("/", "")}.dat");
        }

        public void SaveTrades()
        {
			lock(_marketsRequiringSaving)
			{
				lock(_cachedTrades)
				{
					foreach(var market in _marketsRequiringSaving)
					{
                        Log.Info($"Saving cached trades for {market}");
						var marketCachedTrades = _cachedTrades[market];
						var bytes = CachedTradeResult.ToBytes(marketCachedTrades.Values.ToList());
						var path = GetPath(market);
						File.WriteAllBytes(path, bytes);
                        Log.Info($"Saved cached trades for {market}");
					}
                }
				
				_marketsRequiringSaving.Clear();
			}
        }
		
		public void LoadTrades(string market)
		{
            lock (_cachedTrades)
            {
                if (_cachedTrades.ContainsKey(market)) return;
            }

            var path = GetPath(market);
			if (!File.Exists(path)) return;
			

            Log.Info($"Loading cached trades for {market}");
			var bytes = File.ReadAllBytes(path);

            Dictionary<CachedTradeKey, CachedTradeResult> marketCachedTrades;
            lock (_cachedTrades)
			{
				_cachedTrades.TryGetValue(market, out marketCachedTrades);		
				if (marketCachedTrades == null)
				{
					marketCachedTrades = new Dictionary<CachedTradeKey, CachedTradeResult>();
					_cachedTrades[market] = marketCachedTrades;
				}
			}
			
			var tradeResults = CachedTradeResult.FromBytes(bytes);
			foreach(var tradeResult in tradeResults)
			{
                marketCachedTrades[CachedTradeKey.Create(tradeResult)] = tradeResult;
			}

            Log.Info($"Loaded cached trades for {market}");
        }

        public void AddTrades(string market, IEnumerable<Trade> trades)
        {
            Dictionary<CachedTradeKey, CachedTradeResult> marketCachedTrades;
            var updated = false;

            lock (_cachedTrades)
            {
                _cachedTrades.TryGetValue(market, out marketCachedTrades);
                if (marketCachedTrades == null)
                {
                    marketCachedTrades = new Dictionary<CachedTradeKey, CachedTradeResult>();

                    _cachedTrades[market] = marketCachedTrades;
                }
            }

            foreach (var t in trades)
            {
                if (t.CloseDateTime == null) throw new ApplicationException("Only trades that are complete can be cached");
                if (t.UpdateMode != TradeUpdateMode.Unchanging) throw new ApplicationException("Only trades that are untouched can be cached");

                var key = CachedTradeKey.Create(t);
                if (!marketCachedTrades.ContainsKey(key))
                {
                    marketCachedTrades[key] = CachedTradeResult.Create(t);
                    updated = true;
                }
            }

            if (updated)
            {
                lock (_marketsRequiringSaving)
                {
                    _marketsRequiringSaving.Add(market);
                }
            }
        }

        public CachedTradeResult? GetCachedTrade(Trade t)
        {
            lock (_cachedTrades)
            {
                _cachedTrades.TryGetValue(t.Market, out var marketCachedTrades);

                if (marketCachedTrades != null)
                {
                    var key = CachedTradeKey.Create(t);
                    if (marketCachedTrades.TryGetValue(key, out var cachedTradeResult))
                        return cachedTradeResult;
                }
            }

            return null;
        }
    }
}