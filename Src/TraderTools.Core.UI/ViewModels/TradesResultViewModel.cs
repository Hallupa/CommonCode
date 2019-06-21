using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Hallupa.Library.UI;
using TraderTools.Basics;

namespace TraderTools.Core.UI.ViewModels
{
    public class TradesResultViewModel : DependencyObject, INotifyPropertyChanged
    {
        private string _name;
        private int _tradesCount;
        private decimal _percentSuccessfulTrades;
        private decimal _avRWinningTrades;
        private decimal _avRLosingTrades;
        private decimal _rSum;
        private decimal _avAdverseRFor10Candles;
        private decimal _avPositiveRFor20Candles;
        private decimal _expectancyR;
        private int _openTradesCount;

        public ObservableCollectionEx<TradeDetails> Trades { get; } = new ObservableCollectionEx<TradeDetails>();

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        public int OpenTradesCount
        {
            get => _openTradesCount;
            set
            {
                _openTradesCount = value;
                OnPropertyChanged();
            }
        }

        public int TradesCount
        {
            get => _tradesCount;
            set
            {
                _tradesCount = value;
                OnPropertyChanged();
            }
        }

        public decimal PercentSuccessfulTrades
        {
            get => _percentSuccessfulTrades;
            set
            {
                _percentSuccessfulTrades = value;
                OnPropertyChanged();
            }
        }

        public decimal AvRWinningTrades
        {
            get => _avRWinningTrades;
            set
            {
                _avRWinningTrades = value;
                OnPropertyChanged();
            }
        }

        public decimal AvRLosingTrades
        {
            get => _avRLosingTrades;
            set
            {
                _avRLosingTrades = value;
                OnPropertyChanged();
            }
        }

        public decimal AvAdverseRFor10Candles
        {
            get => _avAdverseRFor10Candles;
            set
            {
                _avAdverseRFor10Candles = value;
                OnPropertyChanged();
            }
        }

        public decimal RExpectancy
        {
            get => _expectancyR;
            set
            {
                _expectancyR = value;
                OnPropertyChanged();
            }

        }

        public decimal AvPositiveRFor20Candles
        {
            get => _avPositiveRFor20Candles;
            set
            {
                _avPositiveRFor20Candles = value;
                OnPropertyChanged();
            }
        }

        public decimal RSum
        {
            get => _rSum;
            set
            {
                _rSum = value;
                OnPropertyChanged();
            }
        }

        public decimal Profit { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void UpdateStats()
        {
            var positiveRMultipleTrades = Trades.Where(t => t.RMultiple != null && t.RMultiple > 0).ToList();
            var negativeRMultipleTrades = Trades.Where(t => t.RMultiple != null && t.RMultiple <= 0).ToList();
            var profitableTrades = Trades.Where(t => t.Profit > 0).ToList();
            TradesCount = Trades.Count;
            OpenTradesCount = Trades.Count(x => x.EntryDateTime != null && x.CloseDateTime == null);
            Profit = Trades.Sum(t => t.Profit ?? 0);
            PercentSuccessfulTrades = profitableTrades.Count != 0 ? (profitableTrades.Count * 100M) / (decimal)Trades.Count(x => x.Profit != null) : 0;
            RSum = Trades.Where(t => t.RMultiple != null).Sum(t => t.RMultiple.Value);

            AvRWinningTrades = positiveRMultipleTrades.Count != 0 ? positiveRMultipleTrades.Where(t => t.RMultiple != null).Sum(t => t.RMultiple.Value) / (decimal)positiveRMultipleTrades.Count : 0;
            AvRLosingTrades = negativeRMultipleTrades.Count != 0 ? negativeRMultipleTrades.Where(t => t.RMultiple != null).Sum(t => t.RMultiple.Value) / (decimal)negativeRMultipleTrades.Count : 0;
            RExpectancy = Trades.Count(x => x.RMultiple != null) > 0 ? Trades.Where(t => t.RMultiple != null).Sum(t => t.RMultiple.Value) / Trades.Count(x => x.RMultiple != null) : 0;
        }
    }
}