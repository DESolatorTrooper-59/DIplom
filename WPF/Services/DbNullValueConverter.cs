using System;
using System.Globalization;
using System.Windows.Data;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public sealed class DbNullValueConverter : IValueConverter
    {
        private static readonly string[] SupportedDateFormats =
        {
            "dd.MM.yyyy",
            "d.M.yyyy",
            "yyyy-MM-dd"
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == DBNull.Value ? null : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return DBNull.Value;
            }

            string text = value as string;
            if (text == null)
            {
                return value;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return DBNull.Value;
            }

            switch (ResolveFieldType(parameter))
            {
                case FieldType.Date:
                    DateTime dateValue;
                    if (DateTime.TryParse(trimmed, culture, DateTimeStyles.None, out dateValue) ||
                        DateTime.TryParse(trimmed, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out dateValue) ||
                        DateTime.TryParseExact(trimmed, SupportedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateValue))
                    {
                        return dateValue;
                    }

                    throw new FormatException("Введите дату в формате дд.ММ.гггг или оставьте поле пустым.");

                case FieldType.Integer:
                    int intValue;
                    if (int.TryParse(trimmed, NumberStyles.Integer, culture, out intValue) ||
                        int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                    {
                        return intValue;
                    }

                    throw new FormatException("Введите целое число или оставьте поле пустым.");

                case FieldType.Decimal:
                    decimal decimalValue;
                    if (decimal.TryParse(trimmed, NumberStyles.Number, culture, out decimalValue) ||
                        decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimalValue))
                    {
                        return decimalValue;
                    }

                    throw new FormatException("Введите число или оставьте поле пустым.");

                default:
                    return trimmed;
            }
        }

        private static FieldType ResolveFieldType(object parameter)
        {
            if (parameter is FieldType fieldType)
            {
                return fieldType;
            }

            if (parameter is string text && Enum.TryParse(text, out FieldType parsedFieldType))
            {
                return parsedFieldType;
            }

            return FieldType.Text;
        }
    }
}
