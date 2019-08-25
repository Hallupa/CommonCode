using System;

namespace TraderTools.Simulation
{
    public abstract class UpdateTradeStrategyAttribute : Attribute
    {
        public abstract void UpdateTrade(UpdateTradeParameters p);
        public override int GetHashCode()
        {
            return GetUpdateTradeStrategyHashCode();
        }

        /// <summary>
        /// When trades are cached, the update strategy's setup will be saved with the cached details using the hashcode.
        /// When looking up cached trades, if this hashcode matches then it wil be considered the same.
        /// </summary>
        /// <returns></returns>
        public abstract int GetUpdateTradeStrategyHashCode();
    }
}