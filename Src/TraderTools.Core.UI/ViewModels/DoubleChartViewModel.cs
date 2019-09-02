using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Abt.Controls.SciChart;
using Hallupa.Library.UI;
using TraderTools.Basics;

namespace TraderTools.Core.UI.ViewModels
{
    public abstract class DoubleChartViewModel : DependencyObject, INotifyPropertyChanged
    {
        private Timeframe _largeChartTimeframe = Timeframe.H2;

        public DoubleChartViewModel()
        {
            ChartViewModel.XVisibleRange = new IndexRange();
            ChartViewModelSmaller1.XVisibleRange = new IndexRange();

            LargeChartTimeframeOptions.Add(Timeframe.D1);
            LargeChartTimeframeOptions.Add(Timeframe.H4);
            LargeChartTimeframeOptions.Add(Timeframe.H2);
            LargeChartTimeframeOptions.Add(Timeframe.H1);

        }

        public ChartViewModel ChartViewModel { get; } = new ChartViewModel();

        public ChartViewModel ChartViewModelSmaller1 { get; } = new ChartViewModel();

        public event PropertyChangedEventHandler PropertyChanged;

        public Timeframe LargeChartTimeframe
        {
            get => _largeChartTimeframe;
            set
            {
                _largeChartTimeframe = value;
                OnPropertyChanged();
                LargeChartTimeframeChanged();
            }
        }

        protected virtual void LargeChartTimeframeChanged()
        {
        }

        public Timeframe SmallChartTimeframe { get; set; } = Timeframe.D1;
        public ObservableCollection<Timeframe> LargeChartTimeframeOptions { get; } = new ObservableCollection<Timeframe>();
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}