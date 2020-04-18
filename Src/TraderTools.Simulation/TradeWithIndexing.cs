using TraderTools.Basics;

namespace TraderTools.Simulation
{
    public class TradeWithIndexing
    {
        public Trade Trade { get; set; }
        public int LimitIndex { get; set; } = -1;
        public int StopIndex { get; set; } = -1;
        public int OrderIndex { get; set; } = -1;
        public TradeWithIndexing Next { get; set; }
        public TradeWithIndexing Prev { get; set; }
    }
}