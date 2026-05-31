using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class EditEntityWindow : Window
    {
        private readonly EntityDefinition _definition;
        private readonly IDictionary<string, object> _originalValues;
        private readonly DatabaseService _database;
        private readonly bool _isInsert;
        private readonly Dictionary<string, FrameworkElement> _controls = new Dictionary<string, FrameworkElement>();
        private readonly Dictionary<string, IReadOnlyList<LookupOption>> _lookupOptions = new Dictionary<string, IReadOnlyList<LookupOption>>(StringComparer.OrdinalIgnoreCase);

        public EditEntityWindow(EntityDefinition definition, IDictionary<string, object> originalValues, DatabaseService database)
        {
            InitializeComponent();
            _definition = definition;
            _originalValues = originalValues;
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _isInsert = originalValues == null;
            TitleText.Text = (_isInsert ? "Добавление" : "Редактирование") + ": " + _definition.Title;
            BuildForm();
        }

        public Dictionary<string, object> ResultValues { get; private set; }

        private void BuildForm()
        {
            foreach (FieldDefinition field in _definition.Fields)
            {
                if (_isInsert && (field.IsIdentity || field.IsReadOnly))
                {
                    continue;
                }

                TextBlock label = new TextBlock
                {
                    Text = field.Label + (field.IsRequired ? " *" : string.Empty),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                FieldsHost.Children.Add(label);

                FrameworkElement control = CreateControl(field);
                SetInitialValue(control, field);
                FieldsHost.Children.Add(control);
                _controls[field.Name] = control;
            }
        }

        private FrameworkElement CreateControl(FieldDefinition field)
        {
            if (field.IsReadOnly || field.IsIdentity)
            {
                return new TextBox { IsReadOnly = true, IsEnabled = false };
            }

            if (!string.IsNullOrWhiteSpace(field.LookupTableName) && !string.IsNullOrWhiteSpace(field.LookupColumnName))
            {
                ComboBox lookupComboBox = new ComboBox
                {
                    ItemsSource = GetLookupOptions(field),
                    DisplayMemberPath = nameof(LookupOption.Display),
                    SelectedValuePath = nameof(LookupOption.Value)
                };
                return lookupComboBox;
            }

            switch (field.Type)
            {
                case FieldType.Boolean:
                    return new CheckBox { Content = "Да / Нет" };
                case FieldType.Date:
                    return new DatePicker();
                case FieldType.Choice:
                    ComboBox comboBox = new ComboBox();
                    comboBox.Items.Add(string.Empty);
                    foreach (string value in field.AllowedValues)
                    {
                        comboBox.Items.Add(value);
                    }
                    return comboBox;
                default:
                    return new TextBox();
            }
        }

        private void SetInitialValue(FrameworkElement control, FieldDefinition field)
        {
            if (_originalValues == null || !_originalValues.ContainsKey(field.Name))
            {
                if (field.Type == FieldType.Boolean && control is CheckBox checkBox)
                {
                    checkBox.IsChecked = false;
                }

                return;
            }

            object value = _originalValues[field.Name];
            if (value == null)
            {
                return;
            }

            if (control is TextBox textBox)
            {
                textBox.Text = Convert.ToString(value, CultureInfo.CurrentCulture);
            }
            else if (control is DatePicker datePicker)
            {
                datePicker.SelectedDate = Convert.ToDateTime(value, CultureInfo.CurrentCulture);
            }
            else if (control is CheckBox boolBox)
            {
                boolBox.IsChecked = Convert.ToBoolean(value, CultureInfo.CurrentCulture);
            }
            else if (control is ComboBox comboBox)
            {
                if (!string.IsNullOrWhiteSpace(field.LookupTableName) && !string.IsNullOrWhiteSpace(field.LookupColumnName))
                {
                    comboBox.SelectedValue = value;
                }
                else
                {
                    comboBox.SelectedItem = Convert.ToString(value, CultureInfo.CurrentCulture);
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Dictionary<string, object> values = new Dictionary<string, object>();

            foreach (FieldDefinition field in _definition.Fields)
            {
                if (_isInsert && (field.IsIdentity || field.IsReadOnly))
                {
                    continue;
                }

                if (!_controls.ContainsKey(field.Name))
                {
                    continue;
                }

                if (!TryReadValue(field, _controls[field.Name], out object value, out string error))
                {
                    MessageBox.Show(error, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (field.IsRequired && IsRequiredValueMissing(field, value))
                {
                    MessageBox.Show("Заполните поле '" + field.Label + "'.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                values[field.Name] = value;
            }

            ResultValues = values;
            DialogResult = true;
        }

        private static bool TryReadValue(FieldDefinition field, FrameworkElement control, out object value, out string error)
        {
            value = null;
            error = null;

            if (control is CheckBox checkBox)
            {
                value = checkBox.IsChecked == true;
                return true;
            }

            if (control is DatePicker datePicker)
            {
                value = datePicker.SelectedDate.HasValue ? (object)datePicker.SelectedDate.Value.Date : null;
                return true;
            }

            string rawText;
            if (control is ComboBox comboBox)
            {
                if (!string.IsNullOrWhiteSpace(field.LookupTableName) && !string.IsNullOrWhiteSpace(field.LookupColumnName))
                {
                    value = comboBox.SelectedValue;
                    return true;
                }

                rawText = Convert.ToString(comboBox.SelectedItem);
            }
            else
            {
                rawText = control is TextBox textBox ? textBox.Text : string.Empty;
            }

            rawText = string.IsNullOrWhiteSpace(rawText) ? null : rawText.Trim();

            switch (field.Type)
            {
                case FieldType.Integer:
                    if (rawText == null)
                    {
                        value = null;
                        return true;
                    }

                    if (int.TryParse(rawText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int intValue))
                    {
                        value = intValue;
                        return true;
                    }

                    error = "Поле '" + field.Label + "' должно быть целым числом.";
                    return false;
                case FieldType.Decimal:
                    if (rawText == null)
                    {
                        value = null;
                        return true;
                    }

                    if (decimal.TryParse(rawText, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal decimalValue) || decimal.TryParse(rawText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimalValue))
                    {
                        value = decimalValue;
                        return true;
                    }

                    error = "Поле '" + field.Label + "' должно быть числом.";
                    return false;
                default:
                    value = rawText;
                    return true;
            }
        }

        private static bool IsRequiredValueMissing(FieldDefinition field, object value)
        {
            if (field.Type == FieldType.Boolean)
            {
                return false;
            }

            if (value == null)
            {
                return true;
            }

            if (value is string text)
            {
                return string.IsNullOrWhiteSpace(text);
            }

            return false;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private IReadOnlyList<LookupOption> GetLookupOptions(FieldDefinition field)
        {
            string cacheKey = BuildLookupCacheKey(field);
            if (_lookupOptions.TryGetValue(cacheKey, out IReadOnlyList<LookupOption> cachedOptions))
            {
                return cachedOptions;
            }

            if (string.IsNullOrWhiteSpace(field.LookupTableName) || string.IsNullOrWhiteSpace(field.LookupColumnName))
            {
                return Array.Empty<LookupOption>();
            }

            try
            {
                DataTable lookupTable = _database.GetTable(field.LookupTableName);
                if (!lookupTable.Columns.Contains(field.LookupColumnName))
                {
                    return Array.Empty<LookupOption>();
                }

                string displayColumnName = !string.IsNullOrWhiteSpace(field.LookupDisplayColumnName) && lookupTable.Columns.Contains(field.LookupDisplayColumnName)
                    ? field.LookupDisplayColumnName
                    : null;

                IEnumerable<DataRow> rows = lookupTable.Rows
                    .Cast<DataRow>()
                    .Where(row => row[field.LookupColumnName] != DBNull.Value);

                if (RoleRules.IsTournamentOrganizerField(field))
                {
                    rows = rows.Where(row => RoleRules.CanOrganizeTournament(_database, row));
                }

                List<LookupOption> options = rows
                    .OrderBy(row => Convert.ToString(row[field.LookupColumnName], CultureInfo.CurrentCulture))
                    .Select(row => new LookupOption(
                        row[field.LookupColumnName],
                        BuildLookupDisplayText(row, field.LookupColumnName, displayColumnName)))
                    .ToList();

                if (!field.IsRequired)
                {
                    options.Insert(0, new LookupOption(null, string.Empty));
                }

                _lookupOptions[cacheKey] = options;
                return options;
            }
            catch
            {
                return Array.Empty<LookupOption>();
            }
        }

        private static string BuildLookupCacheKey(FieldDefinition field)
        {
            return string.Join("|",
                field.LookupTableName ?? string.Empty,
                field.LookupColumnName ?? string.Empty,
                field.LookupDisplayColumnName ?? string.Empty,
                field.IsRequired.ToString());
        }

        private static string BuildLookupDisplayText(DataRow row, string valueColumnName, string displayColumnName)
        {
            string valueText = Convert.ToString(row[valueColumnName], CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(displayColumnName) || row[displayColumnName] == DBNull.Value)
            {
                return valueText;
            }

            string displayText = Convert.ToString(row[displayColumnName], CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(displayText) || string.Equals(displayText, valueText, StringComparison.CurrentCultureIgnoreCase))
            {
                return valueText;
            }

            return valueText + " - " + displayText;
        }
    }
}
