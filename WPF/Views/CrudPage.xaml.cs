using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class CrudPage : UserControl
    {
        private readonly DatabaseService _database;
        private readonly EntityCrudService _crud;
        private readonly EntityDefinition _definition;
        private readonly string _currentLogin;
        private readonly SearchMatchBackgroundConverter _searchMatchBackgroundConverter = new SearchMatchBackgroundConverter();
        private readonly DbNullValueConverter _dbNullValueConverter = new DbNullValueConverter();
        private readonly Dictionary<string, IReadOnlyList<LookupOption>> _lookupOptions = new Dictionary<string, IReadOnlyList<LookupOption>>(StringComparer.OrdinalIgnoreCase);
        private DataTable _sourceTable;
        private bool _isLoaded;
        private DataRow _pendingInsertRow;
        private DataRow _pendingEditRow;
        private Dictionary<string, object> _pendingEditOriginalValues;
        private bool _isFinalizingPendingChange;

        public CrudPage(DatabaseService database, EntityCrudService crud, EntityDefinition definition, string currentLogin)
        {
            InitializeComponent();
            _database = database;
            _crud = crud;
            _definition = definition;
            _currentLogin = currentLogin;
            TitleText.Text = definition.Title;
            Loaded += CrudPage_Loaded;
        }

        private void CrudPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                return;
            }

            _isLoaded = true;
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                _pendingInsertRow = null;
                _pendingEditRow = null;
                _pendingEditOriginalValues = null;
                _sourceTable = _crud.Load(_definition);
                _lookupOptions.Clear();
                GridItems.ItemsSource = _sourceTable.DefaultView;
                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки данных: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter(string note = null)
        {
            if (_sourceTable == null)
            {
                GridItems.ItemsSource = null;
                StatusText.Text = "Записей: 0";
                return;
            }

            DataView dataView = _sourceTable.DefaultView;
            if (!ReferenceEquals(GridItems.ItemsSource, dataView))
            {
                GridItems.ItemsSource = dataView;
            }

            dataView.RowFilter = TextSearchService.BuildRowFilter(_sourceTable, SearchTextBox.Text);
            int count = dataView.Count;
            StatusText.Text = string.IsNullOrWhiteSpace(note)
                ? "Записей: " + count
                : "Записей: " + count + " • " + note;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CompletePendingChangeIfAny();
                SearchTextBox.Text = string.Empty;
                ApplyFilter();

                DataRow row = _sourceTable.NewRow();
                foreach (DataColumn column in _sourceTable.Columns)
                {
                    row[column.ColumnName] = DBNull.Value;
                }

                PopulateAutomaticValues(row);

                foreach (FieldDefinition field in GetWritableFields())
                {
                    if (_sourceTable.Columns.Contains(field.Name) && field.Type == FieldType.Boolean && row[field.Name] == DBNull.Value)
                    {
                        row[field.Name] = false;
                    }
                }

                _sourceTable.Rows.Add(row);
                _pendingInsertRow = row;
                ApplyFilter("Заполните новую строку. Если оставить обязательные поля пустыми, строка будет удалена.");

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        BeginEditingRow(_pendingInsertRow, true);
                    }
                    catch (Exception ex)
                    {
                        RemovePendingInsertRow("Ошибка перехода к редактированию: " + ex.Message);
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка добавления строки: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CompletePendingChangeIfAny();

                DataRowView rowView = GridItems.SelectedItem as DataRowView;
                if (rowView == null)
                {
                    MessageBox.Show("Сначала выберите запись.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _pendingEditRow = rowView.Row;
                _pendingEditOriginalValues = GetAllRowValues(_pendingEditRow);
                ApplyFilter("Редактируйте строку прямо в таблице. ID-поля заблокированы.");

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        BeginEditingRow(_pendingEditRow, false);
                    }
                    catch (Exception ex)
                    {
                        RestorePendingEditRow("Ошибка перехода к редактированию: " + ex.Message);
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка перехода к редактированию: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (HasPendingInsertRow() && IsPendingRowSelected())
            {
                RemovePendingInsertRow("Новая строка удалена.");
                return;
            }

            CompletePendingChangeIfAny();

            IDictionary<string, object> originalValues = GetSelectedRowValues();
            if (originalValues == null)
            {
                MessageBox.Show("Сначала выберите запись.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            EntityEditContext context = new EntityEditContext(false, originalValues, originalValues, _database);
            EntityValidationResult validation = _definition.DeleteValidator == null ? EntityValidationResult.Success() : _definition.DeleteValidator(context);
            if (!validation.IsValid)
            {
                MessageBox.Show(validation.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show("Удалить выбранную запись?", "Tournaments WPF", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _crud.Delete(_definition, originalValues);
                LoadData();
                ApplyFilter("Запись удалена.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка удаления: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            CompletePendingChangeIfAny();
            ApplyFilter();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            CompletePendingChangeIfAny();
            LoadData();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CompletePendingChangeIfAny();

                OpenFileDialog dialog = new OpenFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = _definition.TableName + ".csv"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                if (MessageBox.Show("Импорт полностью заменит данные текущей таблицы. Продолжить?", "Tournaments WPF", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                DataTable schemaTable = _database.GetTable(_definition.TableName);
                DataTable importedTable = CsvTableImportService.Load(dialog.FileName, _definition, schemaTable);
                _database.ReplaceTableValidated(_definition, importedTable);

                SearchTextBox.Text = string.Empty;
                LoadData();
                ApplyFilter("Импорт завершён. Загружено записей: " + importedTable.Rows.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Импорт остановлен: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            CompletePendingChangeIfAny();

            DataView view = GridItems.ItemsSource as DataView;
            List<DataRowView> rows = view == null ? new List<DataRowView>() : view.Cast<DataRowView>().ToList();

            if (rows.Count == 0)
            {
                MessageBox.Show("Нет данных для экспорта.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = _definition.TableName + ".csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            using (StreamWriter writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(string.Join(";", _definition.Fields.Select(field => EscapeCsv(field.Label))));
                foreach (DataRowView row in rows)
                {
                    writer.WriteLine(string.Join(";", _definition.Fields.Select(field => EscapeCsv(Convert.ToString(row[field.Name])))));
                }
            }

            MessageBox.Show("Экспорт завершён.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void GridItems_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            FieldDefinition field = _definition.Fields.FirstOrDefault(item => item.Name == e.PropertyName);
            if (field == null)
            {
                e.Cancel = true;
                return;
            }

            if (field.Type == FieldType.Date)
            {
                e.Column = CreateDateColumn(field);
            }
            else if (field.Type == FieldType.Choice)
            {
                e.Column = CreateChoiceColumn(field);
            }
            else if (!string.IsNullOrWhiteSpace(field.LookupTableName) && !string.IsNullOrWhiteSpace(field.LookupColumnName))
            {
                e.Column = CreateLookupColumn(field);
            }

            e.Column.Header = field.Label;
            e.Column.SortMemberPath = field.Name;
            e.Column.IsReadOnly = field.IsReadOnly || field.IsIdentity;
            ApplySearchHighlightStyle(e.Column, e.PropertyName);

            DataGridTextColumn textColumn = e.Column as DataGridTextColumn;
            if (textColumn != null)
            {
                Binding binding = textColumn.Binding as Binding;
                if (binding != null)
                {
                    binding.Converter = _dbNullValueConverter;
                    binding.ConverterParameter = field.Type;
                    binding.TargetNullValue = string.Empty;
                    binding.ValidatesOnExceptions = true;
                    binding.NotifyOnValidationError = true;

                    if (field.Type == FieldType.Date)
                    {
                        binding.StringFormat = "dd.MM.yyyy";
                    }
                    else if (field.Type == FieldType.Decimal)
                    {
                        binding.StringFormat = "F2";
                    }
                }
            }
        }
        private void GridItems_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (_isFinalizingPendingChange)
            {
                return;
            }

            DataRowView rowView = e.Row.Item as DataRowView;
            if (rowView == null)
            {
                e.Cancel = true;
                return;
            }

            bool isInsertRow = HasPendingInsertRow() && ReferenceEquals(rowView.Row, _pendingInsertRow);
            bool isEditRow = HasPendingEditRow() && ReferenceEquals(rowView.Row, _pendingEditRow);
            if (!isInsertRow && !isEditRow)
            {
                e.Cancel = true;
                return;
            }

            FieldDefinition field = GetFieldForColumn(e.Column);
            if (field == null || field.IsReadOnly || field.IsIdentity)
            {
                e.Cancel = true;
                return;
            }

            if (isEditRow && IsExistingIdField(field))
            {
                e.Cancel = true;
            }
        }

        private void GridItems_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (_isFinalizingPendingChange || e.EditAction != DataGridEditAction.Commit)
            {
                return;
            }

            DataRowView rowView = e.Row.Item as DataRowView;
            if (rowView == null)
            {
                return;
            }

            if (HasPendingInsertRow() && ReferenceEquals(rowView.Row, _pendingInsertRow))
            {
                Dispatcher.BeginInvoke(new Action(FinalizePendingInsertRow), DispatcherPriority.Background);
                return;
            }

            if (HasPendingEditRow() && ReferenceEquals(rowView.Row, _pendingEditRow))
            {
                Dispatcher.BeginInvoke(new Action(FinalizePendingEditRow), DispatcherPriority.Background);
            }
        }

        private void GridItems_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_isFinalizingPendingChange || !HasPendingChange())
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (HasPendingChange() && !GridItems.IsKeyboardFocusWithin)
                {
                    CompletePendingChangeIfAny();
                }
            }), DispatcherPriority.Background);
        }

        private void GridItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (HasPendingChange())
            {
                return;
            }

            Edit_Click(sender, e);
        }

        private void BeginEditingRow(DataRow row, bool isInsertMode)
        {
            if (row == null)
            {
                return;
            }

            DataRowView rowView = FindRowView(row);
            if (rowView == null)
            {
                throw new InvalidOperationException("Выбранная строка недоступна для редактирования в текущем представлении.");
            }

            GridItems.UpdateLayout();
            GridItems.SelectedItem = rowView;

            DataGridColumn editableColumn = GridItems.Columns.FirstOrDefault(column => CanEditColumn(column, isInsertMode));
            if (editableColumn == null)
            {
                if (!isInsertMode)
                {
                    _pendingEditRow = null;
                    _pendingEditOriginalValues = null;
                    ApplyFilter("В выбранной строке нет доступных для редактирования полей.");
                }

                return;
            }

            GridItems.ScrollIntoView(rowView, editableColumn);
            GridItems.CurrentCell = new DataGridCellInfo(rowView, editableColumn);
            GridItems.Focus();

            if (!GridItems.BeginEdit())
            {
                throw new InvalidOperationException("Не удалось перевести строку в режим редактирования.");
            }
        }

        private void CompletePendingChangeIfAny()
        {
            if (_isFinalizingPendingChange || !HasPendingChange())
            {
                return;
            }

            GridItems.CommitEdit(DataGridEditingUnit.Cell, true);
            GridItems.CommitEdit(DataGridEditingUnit.Row, true);

            if (HasPendingInsertRow())
            {
                FinalizePendingInsertRow();
            }
            else if (HasPendingEditRow())
            {
                FinalizePendingEditRow();
            }
        }

        private void FinalizePendingInsertRow()
        {
            if (!HasPendingInsertRow() || _isFinalizingPendingChange)
            {
                return;
            }

            _isFinalizingPendingChange = true;
            try
            {
                Dictionary<string, object> values = GetWritableRowValues(_pendingInsertRow);
                if (!HasRequiredValues(values))
                {
                    RemovePendingInsertRow("Незаполненная строка удалена.");
                    return;
                }

                EntityEditContext context = new EntityEditContext(true, values, null, _database);
                EntityValidationResult validation = _definition.SaveValidator == null ? EntityValidationResult.Success() : _definition.SaveValidator(context);
                if (!validation.IsValid)
                {
                    RemovePendingInsertRow(validation.Message + " Строка удалена.");
                    return;
                }

                _crud.Insert(_definition, values);
                _pendingInsertRow = null;
                SearchTextBox.Text = string.Empty;
                LoadData();
                SelectLastRow();
                ApplyFilter("Запись добавлена.");
            }
            catch (Exception ex)
            {
                RemovePendingInsertRow("Ошибка сохранения: " + ex.Message);
            }
            finally
            {
                _isFinalizingPendingChange = false;
            }
        }

        private void FinalizePendingEditRow()
        {
            if (!HasPendingEditRow() || _isFinalizingPendingChange)
            {
                return;
            }

            _isFinalizingPendingChange = true;
            try
            {
                Dictionary<string, object> values = GetWritableRowValues(_pendingEditRow);
                if (!HasRequiredValues(values))
                {
                    RestorePendingEditRow("Не все обязательные поля заполнены. Изменения отменены.");
                    return;
                }

                EntityEditContext context = new EntityEditContext(false, values, _pendingEditOriginalValues, _database);
                EntityValidationResult validation = _definition.SaveValidator == null ? EntityValidationResult.Success() : _definition.SaveValidator(context);
                if (!validation.IsValid)
                {
                    RestorePendingEditRow(validation.Message + " Изменения отменены.");
                    return;
                }

                IDictionary<string, object> selectedKeys = new Dictionary<string, object>(_pendingEditOriginalValues);
                _crud.Update(_definition, values, _pendingEditOriginalValues);
                _pendingEditRow = null;
                _pendingEditOriginalValues = null;
                LoadData();
                SelectRowByKeys(selectedKeys);
                ApplyFilter("Изменения сохранены.");
            }
            catch (Exception ex)
            {
                RestorePendingEditRow("Ошибка сохранения: " + ex.Message);
            }
            finally
            {
                _isFinalizingPendingChange = false;
            }
        }

        private void RemovePendingInsertRow(string note)
        {
            if (HasPendingInsertRow())
            {
                _sourceTable.Rows.Remove(_pendingInsertRow);
            }

            _pendingInsertRow = null;
            ApplyFilter(note);
        }

        private void RestorePendingEditRow(string note)
        {
            if (HasPendingEditRow() && _pendingEditOriginalValues != null)
            {
                foreach (DataColumn column in _pendingEditRow.Table.Columns)
                {
                    object value = _pendingEditOriginalValues.ContainsKey(column.ColumnName) ? _pendingEditOriginalValues[column.ColumnName] : null;
                    _pendingEditRow[column.ColumnName] = value ?? DBNull.Value;
                }
            }

            _pendingEditRow = null;
            _pendingEditOriginalValues = null;
            ApplyFilter(note);
        }
        private bool HasPendingInsertRow()
        {
            return _pendingInsertRow != null &&
                   _pendingInsertRow.Table != null &&
                   ReferenceEquals(_pendingInsertRow.Table, _sourceTable) &&
                   _pendingInsertRow.RowState != DataRowState.Detached;
        }

        private bool HasPendingEditRow()
        {
            return _pendingEditRow != null &&
                   _pendingEditOriginalValues != null &&
                   _pendingEditRow.Table != null &&
                   ReferenceEquals(_pendingEditRow.Table, _sourceTable) &&
                   _pendingEditRow.RowState != DataRowState.Detached;
        }

        private bool HasPendingChange()
        {
            return HasPendingInsertRow() || HasPendingEditRow();
        }

        private bool IsPendingRowSelected()
        {
            DataRowView rowView = GridItems.SelectedItem as DataRowView;
            return rowView != null && HasPendingInsertRow() && ReferenceEquals(rowView.Row, _pendingInsertRow);
        }

        private DataRowView FindRowView(DataRow row)
        {
            DataView view = GridItems.ItemsSource as DataView;
            if (view == null)
            {
                return null;
            }

            foreach (DataRowView rowView in view)
            {
                if (ReferenceEquals(rowView.Row, row))
                {
                    return rowView;
                }
            }

            return null;
        }

        private Dictionary<string, object> GetWritableRowValues(DataRow row)
        {
            Dictionary<string, object> values = new Dictionary<string, object>();
            foreach (FieldDefinition field in GetWritableFields())
            {
                object value = row.Table.Columns.Contains(field.Name) ? row[field.Name] : null;
                values[field.Name] = value == DBNull.Value ? null : value;
            }

            return values;
        }

        private Dictionary<string, object> GetAllRowValues(DataRow row)
        {
            Dictionary<string, object> values = new Dictionary<string, object>();
            foreach (DataColumn column in row.Table.Columns)
            {
                object value = row[column.ColumnName];
                values[column.ColumnName] = value == DBNull.Value ? null : value;
            }

            return values;
        }

        private bool HasRequiredValues(IDictionary<string, object> values)
        {
            foreach (FieldDefinition field in GetWritableFields().Where(item => item.IsRequired))
            {
                object value = values.ContainsKey(field.Name) ? values[field.Name] : null;
                if (IsRequiredValueMissing(field, value))
                {
                    return false;
                }
            }

            return true;
        }

        private void PopulateAutomaticValues(DataRow row)
        {
            foreach (FieldDefinition field in _definition.Fields.Where(item => item.IsIdentity))
            {
                if (!_sourceTable.Columns.Contains(field.Name))
                {
                    continue;
                }

                int? nextIdentity = _database.PeekNextIdentityValue(_definition.TableName);
                if (nextIdentity.HasValue)
                {
                    row[field.Name] = nextIdentity.Value;
                }
            }

            if (string.Equals(_definition.TableName, "Tournaments", StringComparison.OrdinalIgnoreCase))
            {
                if (_sourceTable.Columns.Contains("Organizer") && !string.IsNullOrWhiteSpace(_currentLogin))
                {
                    row["Organizer"] = _currentLogin;
                }

                if (_sourceTable.Columns.Contains("ParticipantMode") && row["ParticipantMode"] == DBNull.Value)
                {
                    row["ParticipantMode"] = "Команды";
                }
            }

            if (string.Equals(_definition.TableName, "Players", StringComparison.OrdinalIgnoreCase) &&
                _sourceTable.Columns.Contains("RealName") &&
                row["RealName"] == DBNull.Value)
            {
                row["RealName"] = "Скрыто";
            }
        }
        private static bool IsRequiredValueMissing(FieldDefinition field, object value)
        {
            if (field.Type == FieldType.Boolean)
            {
                return value == null;
            }

            if (value == null)
            {
                return true;
            }

            string text = value as string;
            return text != null && string.IsNullOrWhiteSpace(text);
        }

        private IDictionary<string, object> GetSelectedRowValues()
        {
            DataRowView rowView = GridItems.SelectedItem as DataRowView;
            return rowView == null ? null : GetAllRowValues(rowView.Row);
        }

        private List<FieldDefinition> GetWritableFields()
        {
            return _definition.Fields
                .Where(field => !field.IsIdentity && !field.IsReadOnly)
                .ToList();
        }

        private void SelectLastRow()
        {
            DataView view = GridItems.ItemsSource as DataView;
            if (view == null || view.Count == 0)
            {
                return;
            }

            DataRowView rowView = view[view.Count - 1];
            GridItems.SelectedItem = rowView;
            GridItems.ScrollIntoView(rowView);
        }

        private void SelectRowByKeys(IDictionary<string, object> keyValues)
        {
            if (keyValues == null)
            {
                return;
            }

            DataView view = GridItems.ItemsSource as DataView;
            if (view == null)
            {
                return;
            }

            foreach (DataRowView rowView in view)
            {
                bool isMatch = _definition.KeyColumns.All(key => keyValues.ContainsKey(key) && ValuesEqual(rowView.Row[key], keyValues[key]));
                if (!isMatch)
                {
                    continue;
                }

                GridItems.SelectedItem = rowView;
                GridItems.ScrollIntoView(rowView);
                return;
            }
        }

        private void ApplySearchHighlightStyle(DataGridColumn column, string propertyName)
        {
            Style baseStyle = column.CellStyle ?? Application.Current.TryFindResource(typeof(DataGridCell)) as Style;
            Style style = baseStyle == null
                ? new Style(typeof(DataGridCell))
                : new Style(typeof(DataGridCell), baseStyle);

            DataTrigger highlightTrigger = new DataTrigger
            {
                Value = true
            };

            MultiBinding matchBinding = new MultiBinding
            {
                Converter = _searchMatchBackgroundConverter
            };
            matchBinding.Bindings.Add(new Binding(propertyName));
            matchBinding.Bindings.Add(new Binding("Text") { Source = SearchTextBox });
            highlightTrigger.Binding = matchBinding;
            highlightTrigger.Setters.Add(new Setter(DataGridCell.BackgroundProperty, System.Windows.Media.Brushes.Khaki));

            style.Triggers.Add(highlightTrigger);
            column.CellStyle = style;
        }

        private DataGridColumn CreateDateColumn(FieldDefinition field)
        {
            return new DataGridTemplateColumn
            {
                CellTemplate = CreateDateCellTemplate(field),
                CellEditingTemplate = CreateDateEditingTemplate(field),
                SortMemberPath = field.Name
            };
        }

        private DataTemplate CreateDateCellTemplate(FieldDefinition field)
        {
            FrameworkElementFactory textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textBlock.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0));
            textBlock.SetBinding(TextBlock.TextProperty, new Binding(field.Name)
            {
                Converter = _dbNullValueConverter,
                ConverterParameter = field.Type,
                StringFormat = "dd.MM.yyyy",
                TargetNullValue = string.Empty
            });

            return new DataTemplate
            {
                VisualTree = textBlock
            };
        }

        private DataTemplate CreateDateEditingTemplate(FieldDefinition field)
        {
            FrameworkElementFactory datePicker = new FrameworkElementFactory(typeof(DatePicker));
            datePicker.SetValue(DatePicker.SelectedDateFormatProperty, DatePickerFormat.Short);
            datePicker.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            datePicker.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            datePicker.SetBinding(DatePicker.SelectedDateProperty, CreateDateEditableBinding(field));

            return new DataTemplate
            {
                VisualTree = datePicker
            };
        }

        private Binding CreateDateEditableBinding(FieldDefinition field)
        {
            return new Binding(field.Name)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Converter = _dbNullValueConverter,
                ConverterParameter = field.Type,
                ValidatesOnExceptions = true,
                NotifyOnValidationError = true
            };
        }
        private DataGridColumn CreateChoiceColumn(FieldDefinition field)
        {
            return new DataGridComboBoxColumn
            {
                ItemsSource = field.AllowedValues.ToList(),
                SelectedItemBinding = CreateSelectorBinding(field),
                SortMemberPath = field.Name
            };
        }

        private DataGridColumn CreateLookupColumn(FieldDefinition field)
        {
            IReadOnlyList<LookupOption> options = GetLookupOptions(field);
            return new DataGridComboBoxColumn
            {
                ItemsSource = options,
                DisplayMemberPath = nameof(LookupOption.Display),
                SelectedValuePath = nameof(LookupOption.Value),
                SelectedValueBinding = CreateSelectorBinding(field),
                SortMemberPath = field.Name
            };
        }

        private Binding CreateEditableBinding(FieldDefinition field)
        {
            return new Binding(field.Name)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Converter = _dbNullValueConverter,
                ConverterParameter = field.Type,
                TargetNullValue = string.Empty,
                ValidatesOnExceptions = true,
                NotifyOnValidationError = true
            };
        }

        private Binding CreateSelectorBinding(FieldDefinition field)
        {
            return new Binding(field.Name)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Converter = _dbNullValueConverter,
                ConverterParameter = field.Type,
                TargetNullValue = null,
                FallbackValue = null,
                ValidatesOnExceptions = true,
                NotifyOnValidationError = true
            };
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

                List<LookupOption> options = lookupTable.Rows
                    .Cast<DataRow>()
                    .Where(row => row[field.LookupColumnName] != DBNull.Value)
                    .OrderBy(row => Convert.ToString(row[field.LookupColumnName]))
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
            string valueText = Convert.ToString(row[valueColumnName]);
            if (string.IsNullOrWhiteSpace(displayColumnName) || row[displayColumnName] == DBNull.Value)
            {
                return valueText;
            }

            string displayText = Convert.ToString(row[displayColumnName]);
            if (string.IsNullOrWhiteSpace(displayText) || string.Equals(displayText, valueText, StringComparison.CurrentCultureIgnoreCase))
            {
                return valueText;
            }

            return valueText + " - " + displayText;
        }
        private bool CanEditColumn(DataGridColumn column, bool isInsertMode)
        {
            FieldDefinition field = GetFieldForColumn(column);
            if (field == null || field.IsReadOnly || field.IsIdentity)
            {
                return false;
            }

            return isInsertMode || !IsExistingIdField(field);
        }

        private FieldDefinition GetFieldForColumn(DataGridColumn column)
        {
            string propertyName = GetColumnPropertyName(column);
            return propertyName == null ? null : _definition.Fields.FirstOrDefault(field => field.Name == propertyName);
        }

        private static string GetColumnPropertyName(DataGridColumn column)
        {
            if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
            {
                return column.SortMemberPath;
            }

            DataGridBoundColumn boundColumn = column as DataGridBoundColumn;
            Binding binding = boundColumn?.Binding as Binding;
            return binding?.Path?.Path;
        }

        private static bool IsExistingIdField(FieldDefinition field)
        {
            return field.IsKey || field.Name.EndsWith("ID", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (left == DBNull.Value)
            {
                left = null;
            }

            if (right == DBNull.Value)
            {
                right = null;
            }

            if (left == null && right == null)
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left is DateTime || right is DateTime)
            {
                return Convert.ToDateTime(left).Date == Convert.ToDateTime(right).Date;
            }

            if (left is bool || right is bool)
            {
                return Convert.ToBoolean(left) == Convert.ToBoolean(right);
            }

            if (left is decimal || right is decimal || left is double || right is double || left is float || right is float)
            {
                return Convert.ToDecimal(left) == Convert.ToDecimal(right);
            }

            return string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.CurrentCultureIgnoreCase);
        }

        private static string EscapeCsv(string value)
        {
            string safeValue = value ?? string.Empty;
            return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
        }
    }
}




