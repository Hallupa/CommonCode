using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TraderTools.Basics;

namespace TraderTools.Simulation
{
    public struct CachedTradeKey
    {
        public string Market { get; set; }
        public decimal Price { get; set; } // Either entry price or order price based on whether OrderType is set
        public DateTime? DateTime { get; set; }
        public TradeDirection TradeDirection { get; set; }
        public DateTime? OrderExpireTime { get; set; }
        public decimal? LimitPrice { get; set; }
        public decimal? StopPrice { get; set; }
        public OrderType? OrderType { get; set; } // If this  is set, it is an entry order rather than market order

        public override int GetHashCode()
        {
            var hashCode = DateTime.GetHashCode() ^ Price.GetHashCode();
            return hashCode;
        }
    }

    public class CachedTradeResult
    {
        public string Market { get; set; }
        public decimal Price { get; set; } // Either entry price or order price based on whether OrderType is set
        public DateTime? DateTime { get; set; }
        public TradeDirection TradeDirection { get; set; }
        public DateTime? OrderExpireTime { get; set; }
        public decimal? LimitPrice { get; set; }
        public decimal? StopPrice { get; set; }
        public OrderType? OrderType { get; set; } // If this  is set, it is an entry order rather than market order
        public DateTime CloseDateTime { get; set; }
        public decimal? ClosePrice { get; set; }
        public TradeCloseReason CloseReason { get; set; }
        public decimal? RMultiple { get; set; }

    }

    public interface ITradeCacheService
    {
        void AddTrade(Trade t);
        CachedTradeResult GetCachedTrade(Trade t);
        void SaveTrades();
    }

    [Export(typeof(ITradeCacheService))]
    [PartCreationPolicy(CreationPolicy.Shared)]

    public class TradeCacheServiceService : ITradeCacheService
    {
        private readonly IDataDirectoryService _dataDirectoryService;
        private Dictionary<CachedTradeKey, CachedTradeResult> _cachedTrades = new Dictionary<CachedTradeKey, CachedTradeResult>();
        private string _path;
        private bool _requiresSaving = false;

        [ImportingConstructor]
        public TradeCacheServiceService(IDataDirectoryService dataDirectoryService)
        {
            _dataDirectoryService = dataDirectoryService;
            _path = Path.Combine(_dataDirectoryService.MainDirectory, "TradeCache", "Trades.json");
            if (!Directory.Exists(Path.GetDirectoryName(_path)))
                Directory.CreateDirectory(Path.GetDirectoryName(_path));

            LoadTrades();
        }

        public void SaveTrades()
        {
            lock(_cachedTrades)
            {
                if (_requiresSaving)
                {
                    File.WriteAllText(_path, JsonConvert.SerializeObject(_cachedTrades.Values.ToList()));
                    _requiresSaving = false;
                }
            }
        }

        private void LoadTrades()
        {
            if (File.Exists(_path))
            {
                lock (_cachedTrades)
                {
                    var list = JsonConvert.DeserializeObject<List<CachedTradeResult>>(File.ReadAllText(_path));
                    foreach (var t in list)
                    {
                        var key = new CachedTradeKey
                        {
                            Market = t.Market,
                            Price = t.Price,
                            DateTime = t.DateTime,
                            StopPrice = t.StopPrice,
                            LimitPrice = t.LimitPrice,
                            OrderType = t.OrderType,
                            OrderExpireTime = t.OrderExpireTime,
                            TradeDirection = t.TradeDirection
                        };

                        _cachedTrades[key] = t;
                    }
                }
            }
        }

        public void AddTrade(Trade t)
        {
            if (t.CloseDateTime == null) throw new ApplicationException("Only trades that are complete can be cached");
            if (t.UpdateMode != TradeUpdateMode.Unchanging) throw new ApplicationException("Only trades that are untouched can be cached");

            var key = new CachedTradeKey
            {
                Market = t.Market,
                Price = t.OrderType != null ? t.OrderPrice.Value : t.EntryPrice.Value,
                DateTime = t.OrderType != null ? t.OrderDateTime : t.EntryDateTime,
                StopPrice = t.StopPrice,
                LimitPrice = t.LimitPrice,
                OrderType = t.OrderType,
                OrderExpireTime = t.OrderExpireTime,
                TradeDirection = t.TradeDirection.Value
            };

            lock (_cachedTrades)
            {
                if (_cachedTrades.ContainsKey(key)) return;

                _cachedTrades[key] = new CachedTradeResult
                {
                    Market = t.Market,
                    Price = t.OrderType != null ? t.OrderPrice.Value : t.EntryPrice.Value,
                    DateTime = t.OrderType != null ? t.OrderDateTime : t.EntryDateTime,
                    StopPrice = t.StopPrice,
                    LimitPrice = t.LimitPrice,
                    OrderType = t.OrderType,
                    OrderExpireTime = t.OrderExpireTime,
                    TradeDirection = t.TradeDirection.Value,
                    CloseDateTime = t.CloseDateTime.Value,
                    RMultiple = t.RMultiple,
                    CloseReason = t.CloseReason.Value,
                    ClosePrice = t.ClosePrice
                };

                _requiresSaving = true;
            }
        }

        private int _t = 0;
        private long _time = 0;
        public CachedTradeResult GetCachedTrade(Trade t)
        {
            _t++;
            var s = Environment.TickCount;
            var key = new CachedTradeKey
            {
                Market = t.Market,
                Price = t.OrderType != null ? t.OrderPrice.Value : t.EntryPrice.Value,
                DateTime = t.OrderType != null ? t.OrderDateTime : t.EntryDateTime,
                StopPrice = t.StopPrice,
                LimitPrice = t.LimitPrice,
                OrderType = t.OrderType,
                OrderExpireTime = t.OrderExpireTime,
                TradeDirection = t.TradeDirection.Value
            };

            lock (_cachedTrades)
            {
                _cachedTrades.TryGetValue(key, out var cachedTrade);

                var e = Environment.TickCount;
                _time += (e - s);
                return cachedTrade;
            }
        }
    }
}