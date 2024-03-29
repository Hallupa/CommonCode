﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SciChart.Charting.Model.ChartSeries;
using SciChart.Charting.ViewportManagers;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.TradeChart;

namespace TraderTools.Core.UI
{
    public class ChartPaneViewModel : IChildPane, INotifyPropertyChanged
    {
        private string _title;
        private AnnotationCollection _tradeAnnotations;
        public event PropertyChangedEventHandler PropertyChanged;
        private double _height = double.NaN;
        private bool _isLastChartPane;
        private string _yAxisTextFormatting = "0.000";

        public ChartPaneViewModel(ChartViewModel parentChartViewModel, IViewportManager viewportManager)
        {
            ViewportManager = viewportManager;
            ParentViewModel = parentChartViewModel;
        }

        public string Title
        {
            get { return _title; }
            set
            {
                if (_title == value) return;

                _title = value;
                OnPropertyChanged("Title");
            }
        }

        public ObservableCollection<IChartSeriesViewModel> ChartSeriesViewModels { get; private set; } = new ObservableCollection<IChartSeriesViewModel>();

        public ICommand ClosePaneCommand { get; set; }

        public IViewportManager ViewportManager { get; }

        public ChartViewModel ParentViewModel { get; }

        public bool IsFirstChartPane { get; set; }

        public string YAxisTextFormatting
        {
            get => _yAxisTextFormatting;
            set => _yAxisTextFormatting = value;
        }

        public bool IsLastChartPane
        {
            get { return _isLastChartPane; }
            set
            {
                if (_isLastChartPane == value) return;
                _isLastChartPane = value;
                OnPropertyChanged("IsLastChartPane");
            }
        }

        public double Height
        {
            get { return _height; }
            set
            {
                if (Math.Abs(_height - value) < double.Epsilon) return;
                _height = value;
                OnPropertyChanged("Height");
            }
        }

        public AnnotationCollection TradeAnnotations
        {
            get { return _tradeAnnotations; }
            set
            {
                _tradeAnnotations = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName]string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ZoomExtents()
        {
        }
    }
}