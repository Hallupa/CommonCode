using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TraderTools.Basics;

namespace TraderTools.Simulation
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CachedTradeResult
    {
        public float OrderPrice { get; set; }
        public float EntryPrice { get; set; }
        public long OrderDateTimeTicks { get; set; }
        public long EntryDateTimeTicks { get; set; }
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
                OrderPrice = t.OrderType != null ? (float)t.OrderPrice.Value : -1,
                EntryPrice = t.EntryPrice != null ? (float)t.EntryPrice.Value : -1,
                OrderDateTimeTicks = t.OrderDateTime?.Ticks ?? -1,
                EntryDateTimeTicks = t.EntryDateTime?.Ticks ?? -1,
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
            t.ClosePrice = !ClosePrice.Equals(-1) ? (decimal?)ClosePrice : null;
            t.CloseReason = (TradeCloseReason)CloseReason;
            t.EntryPrice = !EntryPrice.Equals(-1) ? (decimal?)EntryPrice : null;
            t.RMultiple = !RMultiple.Equals(float.MinValue) ? (decimal?)RMultiple : null;
            t.EntryDateTime = !EntryDateTimeTicks.Equals(-1) ? (DateTime?)new DateTime(EntryDateTimeTicks) : null;
        }
    }
}