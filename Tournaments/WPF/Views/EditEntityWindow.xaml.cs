using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Views
{
    public partial class EditEntityWindow : Window
    {
        private readonly EntityDefinition _definition;
        private readonly IDictionary<string, object> _originalValues;
        private readonly bool _isInsert;
        private readonly Dictionary<string, FrameworkElement> _controls = new Dictionary<string, FrameworkElement>();

        public EditEntityWindow(EntityDefinition definition, IDictionary<string, object> originalValues)
        {
            InitializeComponent();
            _definition = definition;
            _originalValues = originalValues;
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
                comboBox.SelectedItem = Convert.ToString(value, CultureInfo.CurrentCulture);
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
    }
}
