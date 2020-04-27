using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using TraderTools.Basics;

namespace TraderTools.Simulation
{
    public struct CachedTradeKey
    {
        public float Price { get; set; } // Either entry price or order price based on whether OrderType is set
        public long DateTimeTicks { get; set; }
        public byte TradeDirection { get; set; }
        public long OrderExpireTimeTicks { get; set; }
        public float LimitPrice { get; set; }
        public float StopPrice { get; set; }
        public byte OrderType { get; set; } // If this  is set, it is an entry order rather than market order

        public override int GetHashCode()
        {
            var hashCode = DateTimeTicks.GetHashCode() ^ Price.GetHashCode();
            return hashCode;
        }

        public static CachedTradeKey Create(Trade t)
        {
            return new CachedTradeKey
            {
                Price = t.OrderType != null ? (float)t.OrderPrice.Value : (float)t.EntryPrice.Value,
                DateTimeTicks = t.OrderType != null ? t.OrderDateTime.Value.Ticks : t.EntryDateTime.Value.Ticks,
                StopPrice = (float)t.StopPrice,
                LimitPrice = (float)t.LimitPrice,
                OrderType = t.OrderType != null ? (byte)t.OrderType : (byte)0,
                OrderExpireTimeTicks = t.OrderExpireTime?.Ticks ?? -1,
                TradeDirection = (byte)t.TradeDirection.Value
            };
        }

        public static CachedTradeKey Create(CachedTradeResult t)
		{
			return new CachedTradeKey
			{
				Price = t.Price,
				DateTimeTicks = t.DateTimeTicks,
				StopPrice = t.StopPrice,
				LimitPrice = t.LimitPrice,
				OrderType = t.OrderType,
                OrderExpireTimeTicks = t.OrderExpireTimeTicks,
				TradeDirection = t.TradeDirection
			};
		}
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CachedTradeResult
    {
        public float Price { get; set; } // Either entry price or order price based on whether OrderType is set
        public long DateTimeTicks { get; set; }
        public byte TradeDirection { get; set; }
        public long OrderExpireTimeTicks { get; set; }
        public float LimitPrice { get; set; }
        public float StopPrice { get; set; }
        public byte OrderType { get; set; } // If this  is set, it is an entry order rather than market order
        public long CloseDateTimeTicks { get; set; }
        public float ClosePrice { get; set; }
        public byte CloseReason { get; set; }
        public float RMultiple { get; set; }
		
        public static byte[] ToBytes(List<CachedTradeResult> tradeResults)
        {
            var tradeResultsArray = tradeResults.ToArray();
            var size = Marshal.SizeOf(typeof(CachedTradeResult)) * tradeResultsArray.Length;
            var bytes = new byte[size];
            var gcHandle = GCHandle.Alloc(tradeResultsArray, GCHandleType.Pinned);
            var ptr = gcHandle.AddrOfPinnedObject();
            Marshal.Copy(ptr, bytes, 0, size);
            gcHandle.Free();

            return bytes;
        }

        public static CachedTradeResult[] FromBytes(byte[] data)
        {
            int structSize = Marshal.SizeOf(typeof(CachedTradeResult));
            var ret = new CachedTradeResult[data.Length / structSize]; // Array of structs we want to push the bytes into
            var handle2 = GCHandle.Alloc(ret, GCHandleType.Pinned);// get handle to that array
            Marshal.Copy(data, 0, handle2.AddrOfPinnedObject(), data.Length);// do the copy
            handle2.Free();// cleanup the handle

            return ret;
        }

        public static CachedTradeResult Create(Trade t)
        {
            return new CachedTradeResult
            {
                Price = t.OrderType != null ? (float)t.OrderPrice.Value : -1,
                DateTimeTicks = t.OrderType != null ? t.OrderDateTime.Value.Ticks : t.EntryDateTime.Value.Ticks,
                StopPrice = t.StopPrice != null ? (float)t.StopPrice.Value : -1,
                LimitPrice = t.LimitPrice != null ? (float)t.LimitPrice.Value : -1,
                OrderType = t.OrderType != null ? (byte)t.OrderType.Value : (byte)0,
                OrderExpireTimeTicks = t.OrderExpireTime?.Ticks ?? -1,
                TradeDirection = (byte)t.TradeDirection.Value,
                CloseDateTimeTicks = t.CloseDateTime.Value.Ticks,
                RMultiple = t.RMultiple != null ? (float)t.RMultiple : float.MinValue,
                CloseReason = (byte)t.CloseReason.Value,
                ClosePrice = t.ClosePrice != null ? (float)t.ClosePrice.Value : -1
            };
        }

        public void UpdateTrade(Trade t)
        {
            t.CloseDateTime = new DateTime(CloseDateTimeTicks);
            t.ClosePrice = ClosePrice != -1 ? (decimal?)ClosePrice : null;
            t.CloseReason = (TradeCloseReason)CloseReason;
            t.RMultiple = RMultiple != float.MinValue ? (decimal?)RMultiple : null;
        }
    }

    public interface ITradeCacheService
    {
        void AddTrades(string market, IEnumerable<Trade> trades);
        CachedTradeResult? GetCachedTrade(Trade t);
        void SaveTrades();
        void LoadTrades(string market);
    }

    [Export(typeof(ITradeCacheService))]
    [PartCreationPolicy(CreationPolicy.Shared)]

    public class TradeCacheService : ITradeCacheService
    {
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
            return Path.Combine(_dataDirectoryService.MainDirectory, "TradeCache", $"Trades-{market.Replace("/", "")}.json");
        }

        public void SaveTrades()
        {
			lock(_marketsRequiringSaving)
			{
				lock(_cachedTrades)
				{
					foreach(var market in _marketsRequiringSaving)
					{
						var marketCachedTrades = _cachedTrades[market];
						var bytes = CachedTradeResult.ToBytes(marketCachedTrades.Values.ToList());
						var path = GetPath(market);
						File.WriteAllBytes(path, bytes);
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