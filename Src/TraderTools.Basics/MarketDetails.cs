namespace TraderTools.Basics
{
    public class MarketDetails
    {
        public MarketDetails()
        {
        }

        public MarketDetails(
            string broker, string name, string currency, decimal? pointSize, int? digits,
            int? minLotSize, decimal? contractMultiplier)
        {
            Broker = broker;
            Name = name;
            Currency = currency;
            PointSize = pointSize;
            Digits = digits;
            MinLotSize = minLotSize;
            ContractMultiplier = contractMultiplier;
        }

        public int? Digits { get; internal set; }
        public decimal? PointSize { get; internal set; }
        public int? MinLotSize { get; internal set; }
        public decimal? ContractMultiplier { get; internal set; }
        public string Currency { get; internal set; }
        public string Name { get; internal set; }
        public string Broker { get; internal set; }

        public static string GetKey(string broker, string market)
        {
            return $"{broker}-{market}";
        }
    }
}