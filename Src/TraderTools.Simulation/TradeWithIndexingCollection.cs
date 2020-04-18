using System;
using System.Collections.Generic;
using System.Linq;
using TraderTools.Basics;

namespace TraderTools.Simulation
{
    public class LinkedItem<T>
    {
        public T Next { get; set; }
        public T Prev { get; set; }
    }

    public class TradeWithIndexingCollection
    {
        private TradeWithIndexing _firstOrder;
        private TradeWithIndexing _firstOpen;
        private TradeWithIndexing _firstClosed;

        public void AddOpenTrade(Trade t)
        {
            var tradeWithIndexing = new TradeWithIndexing
            {
                Trade = t
            };

            AddToList(tradeWithIndexing, ref _firstOpen);

        }

        public void AddOrderTrade(Trade t)
        {
            var tradeWithIndexing = new TradeWithIndexing
            {
                Trade = t
            };

            AddToList(tradeWithIndexing, ref _firstOrder);
        }

        public void MoveOrderToOpen(TradeWithIndexing t)
        {
            RemoveFromList(t, ref _firstOrder);
            AddToList(t, ref _firstOpen);
        }

        public void MoveOrderToClosed(TradeWithIndexing t)
        {
            RemoveFromList(t, ref _firstOrder);
            AddToList(t, ref _firstClosed);
        }

        public void MoveOpenToClose(TradeWithIndexing t)
        {
            RemoveFromList(t, ref _firstOpen);
            AddToList(t, ref _firstClosed);
        }

        private void AddToList(TradeWithIndexing t, ref TradeWithIndexing first)
        {
            t.Prev = null;
            t.Next = first;
            if (first != null) first.Prev = t;
            first = t;
        }

        private void RemoveFromList(TradeWithIndexing t, ref TradeWithIndexing first)
        {
            if (first == t)
            {
                first = t.Next;
                if (first != null) first.Prev = null;
            }
            else
            {
                t.Prev.Next = t.Next;
                if (t.Next != null) t.Next.Prev = t.Prev;
            }
        }

        public IEnumerable<TradeWithIndexing> OpenTrades
        {
            get
            {
                var v = _firstOpen;
                while (v != null)
                {
                    var next = v.Next; // Take copy of next here in-case it is removed from the list
                    yield return v;
                    v = next;
                }
            }
        }

        public IEnumerable<TradeWithIndexing> OrderTrades
        {
            get
            {
                var v = _firstOrder;
                while (v != null)
                {
                    var next = v.Next; // Take copy of next here in-case it is removed from the list
                    yield return v;
                    v = next;
                }
            }
        }

        public IEnumerable<TradeWithIndexing> AllTrades => OrderTrades.Concat(OpenTrades).Concat(ClosedTrades);

        public IEnumerable<TradeWithIndexing> ClosedTrades
        {
            get
            {
                var v = _firstClosed;
                while (v != null)
                {
                    var next = v.Next; // Take copy of next here in-case it is removed from the list
                    yield return v;
                    v = next;
                }
            }
        }

        /*
         *
             public class TradeWithIndexingCollection
    {
        private LinkedList<TradeWithIndexing> _orders = new LinkedList<TradeWithIndexing>();
        private LinkedList<TradeWithIndexing> _open = new LinkedList<TradeWithIndexing>();
        private LinkedList<TradeWithIndexing> _closed = new LinkedList<TradeWithIndexing>();

        public List<TradeWithIndexing> Trades = new List<TradeWithIndexing>();

        private int _openTradesStartIndex = 0;
        private int _orderTradesStartIndex = 0;
        private const int ReorderIntervalMilliSeoncds = 500;
        private DateTime? _lastReorderTime = null;


        public void AddTrade(Trade t)
        {
            var tradeWithIndexing = new TradeWithIndexing
            {
                Trade = t
            };

            var z = new LinkedList<TradeWithIndexing>();
            var p = z.AddAfter(null);

            

            Trades.Add(tradeWithIndexing);

            if (_lastReorderTime == null)
            {
                _lastReorderTime = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - _lastReorderTime.Value).TotalMilliseconds > ReorderIntervalMilliSeoncds)
            {
                Trades = ClosedTrades.Concat(OpenTrades).Concat(OrderTrades).ToList();
                _openTradesStartIndex = 0;
                _orderTradesStartIndex = 0;
                _lastReorderTime = DateTime.UtcNow;
            }
        }

        public IEnumerable<TradeWithIndexing> OpenTrades
        {
            get
            {
                var foundStart = false;
                for (var i = _openTradesStartIndex; i < Trades.Count; i++)
                {
                    var t = Trades[i];
                    if (t.Trade.EntryDateTime != null && t.Trade.CloseDateTime == null)
                    {
                        if (!foundStart)
                        {
                            foundStart = true;
                            _openTradesStartIndex = i;
                        }

                        yield return Trades[i];
                    }
                }
            }
        }

        public IEnumerable<TradeWithIndexing> OrderTrades
        {
            get
            {
                var foundStart = false;
                for (var i = _orderTradesStartIndex; i < Trades.Count; i++)
                {
                    var t = Trades[i];
                    if (t.Trade.EntryDateTime == null && t.Trade.CloseDateTime == null && t.Trade.OrderPrice != null)
                    {
                        if (!foundStart)
                        {
                            foundStart = true;
                            _orderTradesStartIndex = i;
                        }

                        yield return Trades[i];
                    }
                }
            }
        }

        public IEnumerable<TradeWithIndexing> ClosedTrades
        {
            get
            {
                for (var i = 0; i < Trades.Count; i++)
                {
                    var t = Trades[i];
                    if (t.Trade.CloseDateTime != null)
                    {
                        yield return Trades[i];
                    }
                }
            }
        }
         */
    }
}