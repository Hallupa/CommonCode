using System.Collections.Generic;
using System.Linq;
using Hallupa.TraderTools.Simulation;
using TraderTools.Basics;

namespace TraderTools.Test.Strategies
{
    public class StrategyBuyWithOrder : StrategyBase
    {
        private bool _ordered = false;

        public StrategyBuyWithOrder()
        {
            SetMarkets("UP");
            SetTimeframes(Timeframe.H1);
        }

        public override void ProcessCandles(List<AddedCandleTimeframe> addedCandleTimeframes)
        {
            if (addedCandleTimeframes.All(c => c.Timeframe != Timeframe.H1)) return;

            if (!_ordered)
            {
                OrderLong("UP", "BASEASSET",10000M, 6000M);
                _ordered = true;
            }
        }
    }
}