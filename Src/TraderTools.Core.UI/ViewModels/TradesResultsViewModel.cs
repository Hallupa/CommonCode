﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Hallupa.Library.UI;
using TraderTools.Basics;

namespace TraderTools.Core.UI.ViewModels
{
    public class TradesResultsViewModel : DependencyObject, INotifyPropertyChanged
    {
        private readonly Func<List<Trade>> _getTradesFunc;
        private readonly bool _createStrategiesSubResults;
        private string _selectedResultOption = "No grouping";
        private Dispatcher _dispatcher;
        private bool _includeOpenTrades = false;
        private bool _includeClosedTrades = true;
        private bool _showIncludeOpenClosedTradesOptions = true;

        public ObservableCollectionEx<TradesResultViewModel> Results { get; } = new ObservableCollectionEx<TradesResultViewModel>();

        public bool DisableUpdates { get; set; } = false;

        public List<string> ResultOptions { get; private set; } = new List<string>
        {
            "No grouping",
            "Market",
            "Trade close month",
            "Timeframe",
            "Strategy"
        };

        public TradesResultsViewModel(Func<List<Trade>> getTradesFunc, bool createStrategiesSubResults = true)
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

        public bool IncludeOpenTrades
        {
            get => _includeOpenTrades;
            set
            {
                _includeOpenTrades = value;
                OnPropertyChanged();
                UpdateResults();
            }
        }

        public bool IncludeClosedTrades
        {
            get => _includeClosedTrades;
            set
            {
                _includeClosedTrades = value;
                OnPropertyChanged();
                UpdateResults();
            }
        }

        public bool ShowIncludeOpenClosedTradesOptions
        {
            get => _showIncludeOpenClosedTradesOptions;
            set
            {
                _showIncludeOpenClosedTradesOptions = value;
                OnPropertyChanged();
            }
        }

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

                UpdateResults();
                OnPropertyChanged();
            }
        }

        public void UpdateResults()
        {
            if (DisableUpdates) return;

            var trades = _getTradesFunc().Where(t => 
                (IncludeOpenTrades && t.CloseDateTime == null && t.EntryDateTime != null)
            || (IncludeClosedTrades && t.CloseDateTime != null)).ToList();

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
                        result.StrategyResults = new TradesResultsViewModel(() => group.ToList(), false);
                        result.StrategyResults.DisableUpdates = true;
                        try
                        {

                            result.StrategyResults.ShowOptions = false;
                            result.StrategyResults.ShowSubOptions = false;
                            result.StrategyResults.SelectedResultOption = "Strategy";
                            result.StrategyResults.AdvStrategyNaming = true;
                            result.StrategyResults.IncludeOpenTrades = true;
                            result.StrategyResults.IncludeClosedTrades = true;
                            result.StrategyResults.ShowIncludeOpenClosedTradesOptions = false;
                        }
                        finally
                        {
                            result.StrategyResults.DisableUpdates = false;
                            result.StrategyResults.UpdateResults();
                        }
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

        private IEnumerable<IGrouping<string, Trade>> GetGroupedTrades(List<Trade> trades)
        {
            IEnumerable<IGrouping<string, Trade>> groupedTrades = null;

            switch (SelectedResultOption)
            {
                case "No grouping":
                    groupedTrades = trades.GroupBy(x => "All trades").ToList();
                    break;
                case "Market":
                    groupedTrades = trades.GroupBy(x => x.Market).ToList();
                    break;
                case "Trade close month":
                    var now = DateTime.Now;
                    groupedTrades =
                        from t in trades
                        let time = t.CloseDateTimeLocal ?? now
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

        private List<IGrouping<string, Trade>> GetTradesGroupedByStrategies(List<Trade> trades)
        {
            if (AdvStrategyNaming)
            {
                return trades.SelectMany(t =>
                {
                    var ret = new List<(string Name, Trade Trade)>();

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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}