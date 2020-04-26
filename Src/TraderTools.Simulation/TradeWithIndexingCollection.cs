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

        public int CachedTradesCount { get; set; } = 0;

        public void AddClosedTrade(Trade t)
        {
            var tradeWithIndexing = new TradeWithIndexing
            {
                Trade = t
            };

            AddToList(tradeWithIndexing, ref _firstClosed);
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
                if (_firstOpen == null) yield break;

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
                if (_firstOrder == null) yield break;

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
                if (_firstClosed == null) yield break;

                var v = _firstClosed;
                while (v != null)
                {
                    var next = v.Next; // Take copy of next here in-case it is removed from the list
                    yield return v;
                    v = next;
                }
            }
        }
    }
}