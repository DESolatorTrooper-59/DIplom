using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tournaments
{
    public partial class Streams : Form
    {
        int k = 0;
        public Streams()
        {
            InitializeComponent();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            menu menu = new menu();
            menu.Show();
            this.Hide();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            MainFunc.Search(dataGridView1, textBox1);
        }

        private void Streams_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
            MainFunc.ShowTable(dataGridView1, "SELECT [StreamID] AS [ID трансляции], [TournamentID] AS [ID турнира], [MatchID] AS [ID матча], [Platform] AS [Платформа], [StreamURL] AS [Ссылка на трансляцию] FROM Streams");
            k = dataGridView1.RowCount;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            try
            {
                k = dataGridView1.RowCount - 2;

                // Переменные под каждый столбец таблицы Streams (4 столбца)
                string StreamID = dataGridView1.Rows[k].Cells[0].Value.ToString();      // IDENTITY - не используется для вставки
                string TournamentID = dataGridView1.Rows[k].Cells[1].Value.ToString();
                string MatchID = dataGridView1.Rows[k].Cells[2].Value.ToString();       // может быть NULL
                string Platform = dataGridView1.Rows[k].Cells[3].Value.ToString();      // может быть NULL
                string StreamURL = dataGridView1.Rows[k].Cells[4].Value.ToString();     // может быть NULL

                // Проверка обязательных полей (TournamentID - обязательное)
                if (!string.IsNullOrEmpty(TournamentID))
                {
                    // Проверяем существует ли турнир с таким ID
                    MainFunc.conBD.Open();
                    SqlCommand checkTournament = new SqlCommand("SELECT COUNT(*) FROM [Tournaments] WHERE [TournamentID] = " + TournamentID, MainFunc.conBD);
                    string tournamentCount = checkTournament.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (tournamentCount == "0")
                    {
                        MessageBox.Show("Турнира с таким ID не существует");
                        return;
                    }

                    // Проверяем MatchID если он указан
                    if (!string.IsNullOrEmpty(MatchID))
                    {
                        MainFunc.conBD.Open();
                        SqlCommand checkMatch = new SqlCommand("SELECT COUNT(*) FROM [Matches] WHERE [MatchID] = " + MatchID, MainFunc.conBD);
                        string matchCount = checkMatch.ExecuteScalar().ToString();
                        MainFunc.conBD.Close();

                        if (matchCount == "0")
                        {
                            MessageBox.Show("Матча с таким ID не существует");
                            return;
                        }
                    }

                    // Все проверки пройдены - добавляем трансляцию
                    string query = "INSERT INTO Streams ([TournamentID], [MatchID], [Platform], [StreamURL]) VALUES (" +
                                  TournamentID + ", " +
                                  (string.IsNullOrEmpty(MatchID) ? "NULL" : MatchID) + ", " +
                                  (string.IsNullOrEmpty(Platform) ? "NULL" : "'" + Platform + "'") + ", " +
                                  (string.IsNullOrEmpty(StreamURL) ? "NULL" : "'" + StreamURL + "'") + ")";

                    MainFunc.acitvateCommand(query);
                    MainFunc.ShowTable(dataGridView1, "SELECT [StreamID] AS [ID трансляции], [TournamentID] AS [ID турнира], [MatchID] AS [ID матча], [Platform] AS [Платформа], [StreamURL] AS [Ссылка на трансляцию] FROM Streams");

                    k = dataGridView1.RowCount;
                }
                else
                {
                    MessageBox.Show("Заполните обязательное поле: ID турнира");
                }
            }
            catch (System.NullReferenceException)
            {
                MessageBox.Show("Заполните все поля корректно");
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;

            // Переменные под каждый столбец таблицы Streams (4 столбца)
            string StreamID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();      // ID для WHERE
            string TournamentID = dataGridView1.Rows[rCount].Cells[1].Value.ToString();
            string MatchID = dataGridView1.Rows[rCount].Cells[2].Value.ToString();
            string Platform = dataGridView1.Rows[rCount].Cells[3].Value.ToString();
            string StreamURL = dataGridView1.Rows[rCount].Cells[4].Value.ToString();

            // Проверка обязательных полей
            if (!string.IsNullOrEmpty(TournamentID))
            {
                // Проверяем существует ли трансляция с таким ID
                MainFunc.conBD.Open();
                SqlCommand checkStream = new SqlCommand("SELECT COUNT(*) FROM [Streams] WHERE [StreamID] = " + StreamID, MainFunc.conBD);
                string streamCount = checkStream.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (streamCount == "0")
                {
                    MessageBox.Show("Трансляции с таким ID не существует");
                    return;
                }

                // Проверяем существует ли турнир
                MainFunc.conBD.Open();
                SqlCommand checkTournament = new SqlCommand("SELECT COUNT(*) FROM [Tournaments] WHERE [TournamentID] = " + TournamentID, MainFunc.conBD);
                string tournamentCount = checkTournament.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (tournamentCount == "0")
                {
                    MessageBox.Show("Турнира с таким ID не существует");
                    return;
                }

                // Проверяем MatchID если он указан
                if (!string.IsNullOrEmpty(MatchID))
                {
                    MainFunc.conBD.Open();
                    SqlCommand checkMatch = new SqlCommand("SELECT COUNT(*) FROM [Matches] WHERE [MatchID] = " + MatchID, MainFunc.conBD);
                    string matchCount = checkMatch.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (matchCount == "0")
                    {
                        MessageBox.Show("Матча с таким ID не существует");
                        return;
                    }
                }

                // Все проверки пройдены - обновляем трансляцию
                string query = "UPDATE Streams SET " +
                              "[TournamentID] = " + TournamentID + ", " +
                              "[MatchID] = " + (string.IsNullOrEmpty(MatchID) ? "NULL" : MatchID) + ", " +
                              "[Platform] = " + (string.IsNullOrEmpty(Platform) ? "NULL" : "'" + Platform + "'") + ", " +
                              "[StreamURL] = " + (string.IsNullOrEmpty(StreamURL) ? "NULL" : "'" + StreamURL + "'") + " " +
                              "WHERE [StreamID] = " + StreamID;

                MainFunc.acitvateCommand(query);
                MainFunc.ShowTable(dataGridView1, "SELECT [StreamID] AS [ID трансляции], [TournamentID] AS [ID турнира], [MatchID] AS [ID матча], [Platform] AS [Платформа], [StreamURL] AS [Ссылка на трансляцию] FROM Streams");
            }
            else
            {
                MessageBox.Show("Заполните обязательное поле: ID турнира");
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;
            string StreamID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();

            // Для Streams нет связанных таблиц (это конечная таблица)
            // Можно удалять напрямую

            MainFunc.acitvateCommand("DELETE FROM Streams WHERE [StreamID] = " + StreamID);
            MainFunc.ShowTable(dataGridView1, "SELECT [StreamID] AS [ID трансляции], [TournamentID] AS [ID турнира], [MatchID] AS [ID матча], [Platform] AS [Платформа], [StreamURL] AS [Ссылка на трансляцию] FROM Streams");
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
