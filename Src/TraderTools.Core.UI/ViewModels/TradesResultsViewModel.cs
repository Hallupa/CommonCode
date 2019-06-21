using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Hallupa.Library.UI;
using TraderTools.Basics;

namespace TraderTools.Core.UI.ViewModels
{
    public class TradesResultsViewModel : DependencyObject
    {
        private readonly Func<List<TradeDetails>> _getTradesFunc;
        private readonly bool _createStrategiesSubResults;
        private string _selectedResultOption = "All";
        private Dispatcher _dispatcher;

        public ObservableCollectionEx<TradesResultViewModel> Results { get; } = new ObservableCollectionEx<TradesResultViewModel>();

        public List<string> ResultOptions { get; private set; } = new List<string>
        {
            "All",
            "Market",
            "Month (Using entry date)",
            "Timeframe",
            "Strategy"
        };

        public TradesResultsViewModel(Func<List<TradeDetails>> getTradesFunc, bool createStrategiesSubResults = true)
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _getTradesFunc = getTradesFunc;
            _createStrategiesSubResults = createStrategiesSubResults;
        }

        public static readonly DependencyProperty ShowSubOptionsProperty = DependencyProperty.Register(
            "ShowSubOptions", typeof(bool), typeof(TradesResultsViewModel), new PropertyMetadata(true));

        public bool ShowSubOptions
        {
            get { return (bool) GetValue(ShowSubOptionsProperty); }
            set { SetValue(ShowSubOptionsProperty, value); }
        }

        public static readonly DependencyProperty ShowOptionsProperty = DependencyProperty.Register(
            "ShowOptions", typeof(bool), typeof(TradesResultsViewModel), new PropertyMetadata(true));

        public bool ShowOptions
        {
            get { return (bool) GetValue(ShowOptionsProperty); }
            set { SetValue(ShowOptionsProperty, value); }
        }

        public static readonly DependencyProperty SubItemsIndexProperty = DependencyProperty.Register(
            "SubItemsIndex", typeof(int), typeof(TradesResultsViewModel), new PropertyMetadata(0));

        public int SubItemsIndex
        {
            get { return (int) GetValue(SubItemsIndexProperty); }
            set { SetValue(SubItemsIndexProperty, value); }
        }

        public bool ShowProfit { get; set; } = false;

        public bool AdvStrategyNaming { get; set; } = false;

        public string SelectedResultOption
        {
            get => _selectedResultOption;
            set
            {
                if (_selectedResultOption == value)
                {
                    return;
                }

                _selectedResultOption = value;
                Results.Clear();

                Task.Run(() => { UpdateResults(); });
            }
        }

        public void UpdateResults()
        {
            var trades = _getTradesFunc();

            var groupedTrades = GetGroupedTrades(trades);
            if (groupedTrades == null)
            {
                return;
            }

            // Update or add
            var results = new List<TradesResultViewModel>();
            foreach (var group in groupedTrades)
            {
                var result = new TradesWithStrategiesResultsViewModel()
                {
                    Name = group.Key
                };
                results.Add(result);

                var groupTrades = group.ToList();

                // Add trades to result
                result.Trades.AddRange(groupTrades);

                if (_createStrategiesSubResults)
                {
                    _dispatcher.Invoke(() =>
                    {
                        result.StrategyResults = new TradesResultsViewModel(() => group.ToList(), false)
                        {
                            ShowOptions = false,
                            ShowSubOptions = false,
                            SelectedResultOption = "Strategy",
                            AdvStrategyNaming = true

                        };
                        result.StrategyResults.UpdateResults();
                    });
                }

                result.UpdateStats();
            }

            _dispatcher.Invoke(() =>
            {
                Results.Clear();
                Results.AddRange(results.OrderByDescending(x => x.Name).ToList());
            });
        }

        private IEnumerable<IGrouping<string, TradeDetails>> GetGroupedTrades(List<TradeDetails> trades)
        {
            IEnumerable<IGrouping<string, TradeDetails>> groupedTrades = null;

            switch (SelectedResultOption)
            {
                case "All":
                    groupedTrades = trades.GroupBy(x => "All trades").ToList();
                    break;
                case "Market":
                    groupedTrades = trades.GroupBy(x => x.Market).ToList();
                    break;
                case "Month (Using entry date)":
                    var now = DateTime.Now;
                    groupedTrades =
                        from t in trades
                        let time = t.EntryDateTimeLocal ?? t.OrderDateTimeLocal ?? now
                        group t by $"{time.Year}/{time.Month:00}"
                        into timeGroup
                        select timeGroup;
                    break;
                case "Timeframe":
                    groupedTrades = trades.GroupBy(x => $"{x.Timeframe}").ToList();
                    break;
                case "Strategy":
                    groupedTrades = GetTradesGroupedByStrategies(trades);
                    break;
            }

            return groupedTrades;
        }

        private List<IGrouping<string, TradeDetails>> GetTradesGroupedByStrategies(List<TradeDetails> trades)
        {
            if (AdvStrategyNaming)
            {
                return trades.SelectMany(t =>
                {
                    var ret = new List<(string Name, TradeDetails Trade)>();

                    if (!string.IsNullOrEmpty(t.Strategies))
                    {
                        foreach (var strategy in t.Strategies.Split(','))
                        {
                            ret.Add((strategy.Trim(), t));
                        }
                    }
                    else
                    {
                        ret.Add((string.Empty, t));
                    }

                    return ret;
                }).GroupBy(x => x.Name, x => x.Trade).ToList();
            }
            else
            {
                return trades.GroupBy(x => x.Strategies).ToList();
            }
        }
    }
}