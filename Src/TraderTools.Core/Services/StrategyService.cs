using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using TraderTools.Core.Trading;

namespace TraderTools.Core.Services
{
    [Export(typeof(StrategyService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class StrategyService
    {
        private List<(IStrategy Strategy, bool IsCustomStrategy)> _strategies = new List<(IStrategy Strategy, bool IsCustomStrategy)>();

        public List<IStrategy> Strategies => _strategies.Select(x => x.Strategy).ToList();

        public void RegisterStrategy(IStrategy strategy)
        {
            _strategies.Add((strategy, false));
        }

        public void RegisterCustomStrategy(IStrategy strategy)
        {
            _strategies.Add((strategy, true));
        }

        public void ClearCustomStrategies()
        {
            for (var i = _strategies.Count - 1; i >= 0; i--)
            {
                if (_strategies[i].IsCustomStrategy)
                {
                    _strategies.RemoveAt(i);
                }
            }
        }
    }
}