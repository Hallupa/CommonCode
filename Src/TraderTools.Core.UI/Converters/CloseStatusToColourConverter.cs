using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TraderTools.Basics;

namespace TraderTools.Core.UI.Converters
{
    public class CloseStatusToColourConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var trade = (Trade) value;

            if (trade.CloseReason == TradeCloseReason.HitStop)
            {
                return new SolidColorBrush(Color.FromRgb(255, 210, 210));
            }

            if (trade.CloseDateTime == null && trade.EntryPrice != null)
            {
                return new SolidColorBrush(Color.FromRgb(210, 255, 210));
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}