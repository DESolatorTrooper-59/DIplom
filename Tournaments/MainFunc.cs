using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;
using static System.Net.Mime.MediaTypeNames;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Office.Interop.Word;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;

namespace Tournaments
{
    internal class MainFunc
    {


        public static SqlConnection conBD = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["BdCon"].ConnectionString);


        public static bool isPasport(DataGridView dg, DataGridViewCellValidatingEventArgs e, int Column)
        {

            dg.Rows[e.RowIndex].ErrorText = "";
            if (e.ColumnIndex == Column)
            {
                if (dg.Rows[e.RowIndex].IsNewRow) { return true; }
                if (e.FormattedValue.ToString().Length != 10)
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Неверный формат нумерации паспортного бланка";
                    MessageBox.Show("Неверный формат нумерации паспортного бланка");
                }
            }
            return false;
        }
        public static bool SendFriendRequest(string currentUserLogin, string friendLogin)
        {
            bool success = false;

            string query = @"
        INSERT INTO Friendlist (Id_profile1, Id_profile2)
        SELECT 
            u1.Id_profile,
            u2.Id_profile
        FROM users u1, users u2
        WHERE u1.Login = @CurrentUserLogin 
          AND u2.Login = @FriendLogin
          AND u1.Id_profile != u2.Id_profile
          AND NOT EXISTS (
              SELECT 1 FROM Friendlist f 
              WHERE f.Id_profile1 = u1.Id_profile 
                AND f.Id_profile2 = u2.Id_profile
          )";

            try
            {
                if (conBD.State != ConnectionState.Open)
                    conBD.Open();

                using (SqlCommand command = new SqlCommand(query, conBD))
                {
                    command.Parameters.Add("@CurrentUserLogin", SqlDbType.NVarChar, 50).Value = currentUserLogin;
                    command.Parameters.Add("@FriendLogin", SqlDbType.NVarChar, 50).Value = friendLogin;

                    int rowsAffected = command.ExecuteNonQuery();
                    success = rowsAffected > 0;
                }
            }
            catch (SqlException sqlEx)
            {
                if (sqlEx.Number == 2627) // Ошибка первичного ключа
                {
                    MessageBox.Show("Заявка в друзья уже отправлена");
                }
                else
                {
                    MessageBox.Show($"Ошибка отправки заявки: {sqlEx.Message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки заявки: {ex.Message}");
            }
            finally
            {
                if (conBD.State == ConnectionState.Open)
                    conBD.Close();
            }

            return success;
        }
        public static bool isPhoneNumber(DataGridView dg, DataGridViewCellValidatingEventArgs e, int Column)
        {

            dg.Rows[e.RowIndex].ErrorText = "";
            if (e.ColumnIndex == Column)
            {
                if (dg.Rows[e.RowIndex].IsNewRow) { return true; }
                if (e.FormattedValue.ToString().Length != 11)
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Неверный формат номера телефона";
                    MessageBox.Show("Неверный формат номера телефона");
                }
            }
            return false;
        }

        public static bool isEmail(DataGridView dg, DataGridViewCellValidatingEventArgs e, int Column)
        {

            dg.Rows[e.RowIndex].ErrorText = "";
            if (e.ColumnIndex == Column)
            {
                if (dg.Rows[e.RowIndex].IsNewRow) { return true; }

                if (!IsValidEmail(e.FormattedValue.ToString()))
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Неверный формат Email";
                    MessageBox.Show("Неверный формат Email");
                }
            }
            return false;
        }

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                email = Regex.Replace(email, @"(@)(.+)$", DomainMapper,
                                      RegexOptions.None, TimeSpan.FromMilliseconds(200));


                string DomainMapper(Match match)
                {
                    var idn = new IdnMapping();
                    string domainName = idn.GetAscii(match.Groups[2].Value);

                    return match.Groups[1].Value + domainName;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }

            try
            {
                return Regex.IsMatch(email,
                    @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                    RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        public static bool isPrice(DataGridView dg, DataGridViewCellValidatingEventArgs e, int Column)
        {
            dg.Rows[e.RowIndex].ErrorText = "";
            if (e.ColumnIndex == Column)
            {
                if (dg.Rows[e.RowIndex].IsNewRow) { return true; }
                double test = 12.12;
                string price = e.FormattedValue.ToString();

                if (!double.TryParse(price, out test))
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Неверный формат числа";
                    MessageBox.Show("Неверный формат числа");
                }

            }
            return false;
        }

        public static void isNumber(object sender, KeyPressEventArgs e)
        {
            if (!Char.IsDigit(e.KeyChar) && e.KeyChar != 8)
            {
                e.Handled = true;
            }
        }
        public static bool isTimeShort(DataGridView dg, DataGridViewCellValidatingEventArgs e, int Column)
        {
            dg.Rows[e.RowIndex].ErrorText = "";

            if (e.ColumnIndex == Column)
            {
                if (dg.Rows[e.RowIndex].IsNewRow) { return true; }

                string input = e.FormattedValue.ToString().Trim();

                // 1. Ищем позицию двоеточия
                int colonIndex = input.IndexOf(':');
                if (colonIndex == -1)
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Отсутствует двоеточие";
                    MessageBox.Show("Неверный формат. Используйте ЧЧ:ММ");
                    return false;
                }

                // 2. Извлекаем части до и после двоеточия
                string hoursStr = input.Substring(0, colonIndex);
                string minutesStr = input.Substring(colonIndex + 1);

                // 3. Проверяем что части не пустые
                if (string.IsNullOrEmpty(hoursStr) || string.IsNullOrEmpty(minutesStr))
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Отсутствуют часы или минуты";
                    MessageBox.Show("Неверный формат. Отсутствуют часы или минуты");
                    return false;
                }

                // 4. Проверяем что состоят только из цифр
                foreach (char c in hoursStr)
                {
                    if (!char.IsDigit(c))
                    {
                        e.Cancel = true;
                        dg.Rows[e.RowIndex].ErrorText = "Часы должны содержать только цифры";
                        MessageBox.Show("Неверный формат. Часы должны содержать только цифры");
                        return false;
                    }
                }

                foreach (char c in minutesStr)
                {
                    if (!char.IsDigit(c))
                    {
                        e.Cancel = true;
                        dg.Rows[e.RowIndex].ErrorText = "Минуты должны содержать только цифры";
                        MessageBox.Show("Неверный формат. Минуты должны содержать только цифры");
                        return false;
                    }
                }

                // 5. Парсим числа
                if (!int.TryParse(hoursStr, out int hours) || !int.TryParse(minutesStr, out int minutes))
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Не удалось преобразовать в числа";
                    MessageBox.Show("Неверный формат. Не удалось преобразовать в числа");
                    return false;
                }

                // 6. Проверяем диапазоны
                if (hours < 0 || hours > 24)
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Часы должны быть от 0 до 24";
                    MessageBox.Show($"Неверные часы: {hours}. Допустимо: 0-24");
                    return false;
                }

                if (minutes < 0 || minutes > 59)
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Минуты должны быть от 0 до 59";
                    MessageBox.Show($"Неверные минуты: {minutes}. Допустимо: 0-59");
                    return false;
                }

                // 7. Если 24 часа, то минуты должны быть 0
                if (hours == 24 && minutes != 0)
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "При 24 часах минуты должны быть 0";
                    MessageBox.Show("При 24 часах минуты должны быть 0");
                    return false;
                }
            }
            return true;
        }
        public static bool isDate(DataGridView dg, DataGridViewCellValidatingEventArgs e, int Column)
        {
            dg.Rows[e.RowIndex].ErrorText = "";

            if (e.ColumnIndex == Column)
            {
                if (dg.Rows[e.RowIndex].IsNewRow) { return true; }

                string input = e.FormattedValue.ToString();

                // Проверка формата ДД.ММ.ГГГГ
                if (!Regex.IsMatch(input, @"^\d{2}\.\d{2}\.\d{4}$"))
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Неверный формат даты. Используйте ДД.ММ.ГГГГ";
                    MessageBox.Show("Неверный формат даты. Используйте ДД.ММ.ГГГГ");
                    return false;
                }

                // Разбиваем на части
                string[] parts = input.Split('.');
                int day, month, year;

                if (!int.TryParse(parts[0], out day) ||
                    !int.TryParse(parts[1], out month) ||
                    !int.TryParse(parts[2], out year))
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Неверный формат даты";
                    MessageBox.Show("Неверный формат даты");
                    return false;
                }

                // Проверка корректности даты
                if (!IsValidDate(day, month, year))
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Некорректная дата";
                    MessageBox.Show("Некорректная дата");
                    return false;
                }

                // Дополнительная проверка: дата не должна быть будущей
                DateTime inputDate = new DateTime(year, month, day);
                if (inputDate > DateTime.Now)
                {
                    e.Cancel = true;
                    dg.Rows[e.RowIndex].ErrorText = "Дата не может быть будущей";
                    MessageBox.Show("Дата не может быть будущей");
                    return false;
                }
            }
            return true;
        }

        // Вспомогательный метод для проверки корректности даты
        private static bool IsValidDate(int day, int month, int year)
        {
            if (year < 1900 || year > DateTime.Now.Year)
                return false;

            if (month < 1 || month > 12)
                return false;

            if (day < 1 || day > DateTime.DaysInMonth(year, month))
                return false;

            return true;
        }
        public static void isDateTime(object sender, KeyPressEventArgs e)
        {
            char number = e.KeyChar;
            if ((number <= 47 || number >= 58) && number != 8 && number != 46 && number != 32 && number != 58)
            {
                e.Handled = true;
            }
        }
        public static void isPriceVvod(object sender, KeyPressEventArgs e)
        {
            char number = e.KeyChar;
            if ((e.KeyChar <= 47 || e.KeyChar >= 58) && number != 8 && number != 44)
            {
                e.Handled = true;
            }
        }
        public static void ShowTable(DataGridView dg, string zapros) //показать таблицу
        {
            if (conBD.State == ConnectionState.Closed)
            {
                conBD.Open();
            }
            SqlDataAdapter da = new SqlDataAdapter(zapros, conBD);
            System.Data.DataTable dt = new System.Data.DataTable();
            da.Fill(dt);
            dg.DataSource = dt;
            conBD.Close();
        }

        public static void acitvateCommand(string zapros) //кинуть запрос
        {
            conBD.Open();
            SqlCommand da = new SqlCommand(zapros, conBD);
            da.ExecuteNonQuery();
            conBD.Close();
        }


        public static string GiveResultSqlCommand(string zapros) //кинуть запрос
        {
            conBD.Open();
            SqlCommand da = new SqlCommand(zapros, conBD);
            string res=da.ExecuteScalar().ToString();
            conBD.Close();
            return res;
            
        }



        static public object Encrypt(byte[] data, string password) //шифрование
        {
            SymmetricAlgorithm sa = null;
            try
            {
                sa = Rijndael.Create();
                ICryptoTransform ct = sa.CreateEncryptor(
                    (new PasswordDeriveBytes(password, null)).GetBytes(16),
                    new byte[16]);
                MemoryStream ms = new MemoryStream();
                CryptoStream cs = new CryptoStream(ms, ct, CryptoStreamMode.Write);
                cs.Write(data, 0, data.Length);
                cs.FlushFinalBlock();
                return ms.ToArray();
            }
            catch (Exception e)
            {
                return e;
            }
        }
        static public string Encrypt(string data, string password) //дешифровка строки подключения
        {
            object tmp = Encrypt(Encoding.UTF8.GetBytes(data), password);
            if (!(tmp is Exception))
            {
                return Convert.ToBase64String((byte[])tmp);
            }
            else return null;
        }

        static public void Search(DataGridView dg, System.Windows.Forms.TextBox textBox) //сам поиск через левентшейна
        {
            int raznica = 0;
            string poisk = textBox.Text;
            for (int i = 0; i < dg.RowCount; i++)
            {
                dg.Rows[i].Selected = false;
                for (int j = 0; j < dg.ColumnCount; j++)
                    if (dg.Rows[i].Cells[j].Value != null)
                    {
                        raznica = LevenshteinDistance(poisk, dg.Rows[i].Cells[j].Value.ToString());
                        if (raznica < 2)
                        {
                            dg.Rows[i].Selected = true;
                        }

                    }
            }
        }

        static public int LevenshteinDistance(string source, string target) //расстояние левентшейна (метод возвращает непосредственно число - разницу между строками)
        {
            if (String.IsNullOrEmpty(source))
            {
                if (String.IsNullOrEmpty(target)) return 0;
                return target.Length;
            }
            if (String.IsNullOrEmpty(target)) return source.Length;

            var m = target.Length;
            var n = source.Length;
            var distance = new int[2, m + 1];
            for (var j = 1; j <= m; j++) distance[0, j] = j;
            var currentRow = 0;
            for (var i = 1; i <= n; ++i)
            {
                currentRow = i & 1;
                distance[currentRow, 0] = i;
                var previousRow = currentRow ^ 1;
                for (var j = 1; j <= m; j++)
                {
                    var cost = (target[j - 1] == source[i - 1] ? 0 : 1);
                    distance[currentRow, j] = Math.Min(Math.Min(
                                distance[previousRow, j] + 1,
                                distance[currentRow, j - 1] + 1),
                                distance[previousRow, j - 1] + cost);
                }
            }
            return distance[currentRow, m];
        }


        public static void vExcel(DataGridView dataGridView1, EventArgs e)
        {
            try
            {
                Microsoft.Office.Interop.Excel._Application app = new Microsoft.Office.Interop.Excel.Application();
                Microsoft.Office.Interop.Excel._Workbook workbook = app.Workbooks.Add(Type.Missing);
                Microsoft.Office.Interop.Excel._Worksheet worksheet = null;
                app.Visible = true;
                worksheet = workbook.Sheets["Sheet1"];
                worksheet = workbook.ActiveSheet;
                for (int i = 1; i < dataGridView1.Columns.Count + 1; i++)
                {
                    worksheet.Cells[1, i] = dataGridView1.Columns[i - 1].HeaderText;
                }
                for (int i = 0; i < dataGridView1.Rows.Count - 1; i++)
                {
                    for (int j = 0; j < dataGridView1.Columns.Count; j++)
                    {
                        if (dataGridView1.Rows[i].Cells[j].Value != null)
                        {
                            worksheet.Cells[i + 2, j + 1] = dataGridView1.Rows[i].Cells[j].Value.ToString();
                        }
                        else
                        {
                            worksheet.Cells[i + 2, j + 1] = "";
                        }
                    }
                }
            }
            catch
            {
                MessageBox.Show("нее хватает прав для создания файла");
            }

        }
        public static void AnyColumnKeyPress(object sender, KeyPressEventArgs e) //проверка что текст без символов и цифр
        {
            if (!char.IsControl(e.KeyChar) && !char.IsLetter(e.KeyChar) && !char.IsWhiteSpace(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        public static void AnyColumnDigit(object sender, KeyPressEventArgs e) //проверка что в столбец пишут цифры
        {
            char number = e.KeyChar;
            if (!Char.IsDigit(number) && number != 8) // цифры и клавиша BackSpace
            {
                e.Handled = true;
            }
        }

        public static void AnyColumnDigitOrSpace(object sender, KeyPressEventArgs e) // цифры и клавиша BackSpace
        {
            char number = e.KeyChar;
            if (!Char.IsDigit(number) && number != 8 && !char.IsWhiteSpace(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        public static void ExportDataGridViewToWord(DataGridView dataGridView, string tableName)
        {
            // Создаем экземпляр Word
            var wordApp = new Microsoft.Office.Interop.Word.Application();
            wordApp.Visible = true; // Делаем Word видимым

            // Создаем новый документ
            var document = wordApp.Documents.Add();

            // Добавляем заголовок таблицы
            var paragraph = document.Paragraphs.Add();
            paragraph.Range.Text = tableName;
            paragraph.Range.Font.Bold = 1;
            paragraph.Range.Font.Size = 14;
            paragraph.Format.SpaceAfter = 24; // Отступ после заголовка
            paragraph.Range.InsertParagraphAfter();

            // Создаем таблицу в Word (строки + 1 для заголовков, столбцы как в DataGridView)
            var wordTable = document.Tables.Add(
                paragraph.Range,
                dataGridView.Rows.Count + 1,
                dataGridView.Columns.Count);

            // Заполняем заголовки таблицы
            for (int i = 0; i < dataGridView.Columns.Count; i++)
            {
                wordTable.Cell(1, i + 1).Range.Text = dataGridView.Columns[i].HeaderText;
                wordTable.Cell(1, i + 1).Range.Font.Bold = 1; // Жирный шрифт для заголовков
            }

            // Заполняем данные таблицы
            for (int i = 0; i < dataGridView.Rows.Count; i++)
            {
                for (int j = 0; j < dataGridView.Columns.Count; j++)
                {
                    if (dataGridView.Rows[i].Cells[j].Value != null)
                    {
                        wordTable.Cell(i + 2, j + 1).Range.Text = dataGridView.Rows[i].Cells[j].Value.ToString();
                    }
                }
            }

            // Применяем автоформатирование таблицы (опционально)
            wordTable.Borders.Enable = 1;
            wordTable.AutoFitBehavior(WdAutoFitBehavior.wdAutoFitContent);

            // Сохраняем документ (опционально)
            // document.SaveAs2(@"C:\Export.docx");

            // Освобождаем ресурсы (но оставляем Word открытым)
            System.Runtime.InteropServices.Marshal.ReleaseComObject(wordTable);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(document);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(wordApp);
        }
    }
}

