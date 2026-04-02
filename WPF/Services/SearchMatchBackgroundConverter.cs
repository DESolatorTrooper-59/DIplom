using System;
using System.Globalization;
using System.Windows.Data;

namespace Tournaments.WPF.Services
{
    public sealed class SearchMatchBackgroundConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            object cellValue = values != null && values.Length > 0 ? values[0] : null;
            string query = values != null && values.Length > 1 ? values[1] as string : null;
            return TextSearchService.CellMatches(cellValue, query);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
