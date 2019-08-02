using log4net;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Hallupa.Library;
using Newtonsoft.Json;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;

namespace TraderTools.Core.Services
{
    public struct LastUpdatedMarket
    {
        public string BrokerName { get; set; }
        public string Market { get; set; }
        public Timeframe Timeframe { get; set; }
    }

    [Export(typeof(IBrokersCandlesService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class BrokersCandlesService : IBrokersCandlesService
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static DateTime EarliestDateTime = new DateTime(2013, 1, 1);

        private Dictionary<(IBroker broker, string Market, Timeframe TimeFrame), List<ICandle>> _candlesLookup
            = new Dictionary<(IBroker broker, string Market, Timeframe TimeFrame), List<ICandle>>();

        private Dictionary<LastUpdatedMarket, DateTime> _lastUpdated = new Dictionary<LastUpdatedMarket, DateTime>();

        public BrokersCandlesService()
        {
            _savePath = Path.Combine(BrokersService.DataDirectory, "LastBrokerMarketUpdates.json");
            if (File.Exists(_savePath))
            {
                var data = JsonConvert.DeserializeObject<List<(LastUpdatedMarket, DateTime)>>(File.ReadAllText(_savePath));
                _lastUpdated = data.ToDictionary(x => x.Item1, x => x.Item2);
            }

        }

        public void UpdateCandles(IBroker broker, string market, Timeframe timeframe)
        {
            GetCandles(broker, market, timeframe, true);
        }

        private Dictionary<(IBroker Broker, string Market, Timeframe Timeframe), object> _lockLookups = new Dictionary<(IBroker Broker, string Market, Timeframe Timeframe), object>();
        private string _savePath;

        private object GetLock(IBroker broker, string market, Timeframe timeframe)
        {
            lock (_lockLookups)
            {
                if (_lockLookups.TryGetValue((broker, market, timeframe), out var l))
                {
                    return l;
                }

                var newLock = new object();
                _lockLookups[(broker, market, timeframe)] = newLock;
                return newLock;
            }
        }

        public List<ICandle> GetCandles(IBroker broker, string market, Timeframe timeframe,
            bool updateCandles, DateTime? minOpenTimeUtc = null, DateTime? maxCloseTimeUtc = null, bool cacheData = true, bool forceUpdate = false)
        {
            var lck = GetLock(broker, market, timeframe);
            var start = EarliestDateTime;

            lock (lck)
            {
                List<ICandle> candles;
                lock (_candlesLookup)
                {
                    _candlesLookup.TryGetValue((broker, market, timeframe), out candles);
                }

                if (candles == null || candles.Count == 0)
                {
                    candles = LoadBrokerCandles(broker, market, timeframe).ToList();

                    if (cacheData && candles != null)
                    {
                        lock (_candlesLookup)
                        {
                            // Re-check if candles lookup contains the key as it may have changed
                            if (!_candlesLookup.ContainsKey((broker, market, timeframe)))
                            {
                                _candlesLookup.Add((broker, market, timeframe), candles);
                            }
                        }
                    }
                }

                bool returnCandles = !updateCandles;

                if (_lastUpdated.TryGetValue(new LastUpdatedMarket
                {
                    BrokerName = broker.Name,
                    Market = market,
                    Timeframe = timeframe
                }, out var updatedDatetime))
                {
                    if (!forceUpdate && (DateTime.UtcNow - updatedDatetime).TotalSeconds < 60 * 30)
                    {
                        returnCandles = true;
                    }
                }

                // Only update candles is the latest candle wanted is later than the latest-1 candle stored (-1 because it assumes the latest candle is incomplete)
                if (!returnCandles && (maxCloseTimeUtc == null || candles.Count <= 2 || candles[candles.Count - 2].CloseTime() < maxCloseTimeUtc))
                {
                    if (candles.Any())
                    {
                        var change = timeframe == Timeframe.M1 ? -2 : -10;
                        start = new DateTime(candles[candles.Count - 1].OpenTimeTicks).AddMinutes(change);
                    }
                    
                    if (broker.UpdateCandles(candles, market, timeframe, start))
                    {
                        SaveCandles(candles, broker, market, timeframe);
                        Log.Debug($"Updated {broker.Name} {market} {timeframe} candles");
                    }

                    lock (_lastUpdated)
                    {
                        _lastUpdated[new LastUpdatedMarket
                        {
                            BrokerName = broker.Name,
                            Market = market,
                            Timeframe = timeframe
                        }] = DateTime.UtcNow;

                        File.WriteAllText(_savePath, JsonConvert.SerializeObject(_lastUpdated.Select(x => (x.Key, x.Value)).ToList()));
                    }
                }


                var ret = new List<ICandle>();
                for (var i = 0; i < candles.Count; i++)
                {
                    var candle = candles[i];
                    if ((maxCloseTimeUtc == null || candle.CloseTimeTicks <= maxCloseTimeUtc.Value.Ticks)
                        && (minOpenTimeUtc == null || candle.OpenTimeTicks >= minOpenTimeUtc.Value.Ticks))
                    {
                        ret.Add(candle);

                        if (maxCloseTimeUtc != null && candle.CloseTimeTicks > maxCloseTimeUtc.Value.Ticks)
                        {
                            break;
                        }
                    }
                }

                return ret;
            }
        }

        private static ICandle[] LoadBrokerCandles(IBroker broker, string market, Timeframe timeframe)
        {
            using (new LogRunTime($"Load and process {broker.Name} {market} {timeframe} candles"))
            {
                var candlesPath = Path.Combine(BrokersService.DataDirectory, "Candles", $"{broker.Name}_{market.Replace("/", "")}_{timeframe}.dat");

                if (File.Exists(candlesPath))
                {
                    byte[] data;

                    using (new LogRunTime($"Loaded {broker.Name} {market} {timeframe} candles data"))
                    {
                        data = File.ReadAllBytes(candlesPath);
                    }

                    int structSize = Marshal.SizeOf(typeof(Candle));
                    var ret = new Candle[data.Length / structSize]; // Array of structs we want to push the bytes into
                    var handle2 = GCHandle.Alloc(ret, GCHandleType.Pinned);// get handle to that array
                    Marshal.Copy(data, 0, handle2.AddrOfPinnedObject(), data.Length);// do the copy
                    handle2.Free();// cleanup the handle

                    return ret.Cast<ICandle>().ToArray();
                }
            }

            return new ICandle[0];
        }

        public void SaveCandles(List<ICandle> candles, IBroker broker, string market, Timeframe timeframe)
        {
            var directory = Path.Combine(BrokersService.DataDirectory, "Candles");
            var candlesTmpPath = Path.Combine(directory, $"{broker.Name}_{market.Replace("/", "")}_{timeframe}_tmp.dat");
            var candlesFinalPath = Path.Combine(directory, $"{broker.Name}_{market.Replace("/", "")}_{timeframe}.dat");

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(candlesTmpPath))
            {
                File.Delete(candlesTmpPath);
            }

            var candlesArray = candles.Cast<Candle>().ToArray();
            var size = Marshal.SizeOf(typeof(Candle)) * candlesArray.Length;
            var bytes = new byte[size];
            var gcHandle = GCHandle.Alloc(candlesArray, GCHandleType.Pinned);
            var ptr = gcHandle.AddrOfPinnedObject();
            Marshal.Copy(ptr, bytes, 0, size);
            gcHandle.Free();


            File.WriteAllBytes(candlesTmpPath, bytes);

            if (File.Exists(candlesFinalPath))
            {
                File.Delete(candlesFinalPath);
            }

            File.Move(candlesTmpPath, candlesFinalPath);

            Log.Debug($"Completed saving {broker.Name} candles");
        }

        public void UnloadCandles(string market, Timeframe timeframe, IBroker broker)
        {
            lock (_candlesLookup)
            {
                _candlesLookup.Remove((broker, market, timeframe));
            }
        }

        public void Lock()
        {
            Monitor.Enter(_lockLookups);
        }

        public void Unlock()
        {
            Monitor.Exit(_lockLookups);
        }
    }
}