using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TraderTools.Basics;

namespace TraderTools.Core.UI.Converters
{
   public class ProfitToColourConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var v = (TradeDetails)value;

            if (v != null)
            {
                if (v.Profit != null && v.Profit.Value < 0)
                {
                    return new SolidColorBrush(Color.FromRgb(255, 210, 210));
                }

                if (v.Profit != null && v.Profit.Value > 0)
                {
                    return new SolidColorBrush(Color.FromRgb(210, 255, 210));
                }
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
