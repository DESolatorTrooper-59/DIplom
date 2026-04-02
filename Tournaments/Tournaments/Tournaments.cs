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
    public partial class Tournaments : Form
    {
        int k;
        public Tournaments()
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

        private void button5_Click(object sender, EventArgs e)
        {
            try
            {
                k = dataGridView1.RowCount - 2;

                // Переменные под каждый столбец таблицы Tournaments (9 столбцов)
                string TournamentID = dataGridView1.Rows[k].Cells[0].Value.ToString();      // IDENTITY - не используется для вставки
                string TournamentName = dataGridView1.Rows[k].Cells[1].Value.ToString();
                string GameID = dataGridView1.Rows[k].Cells[2].Value.ToString();
                string StartDate = dataGridView1.Rows[k].Cells[3].Value.ToString();
                string EndDate = dataGridView1.Rows[k].Cells[4].Value.ToString();
                string PrizePool = dataGridView1.Rows[k].Cells[5].Value.ToString();         // может быть NULL
                string Organizer = dataGridView1.Rows[k].Cells[6].Value.ToString();         // может быть NULL
                string Location = dataGridView1.Rows[k].Cells[7].Value.ToString();          // может быть NULL
                string FormatType = dataGridView1.Rows[k].Cells[8].Value.ToString();
                string MaxTeams = dataGridView1.Rows[k].Cells[9].Value.ToString();

                // Проверка обязательных полей
                if (!string.IsNullOrEmpty(TournamentName) && !string.IsNullOrEmpty(GameID) &&
                    !string.IsNullOrEmpty(StartDate) && !string.IsNullOrEmpty(EndDate) &&
                    !string.IsNullOrEmpty(FormatType) && !string.IsNullOrEmpty(MaxTeams))
                {
                    // Проверяем существует ли игра с таким ID
                    MainFunc.conBD.Open();
                    SqlCommand checkGame = new SqlCommand("SELECT COUNT(*) FROM [GameTitles] WHERE [GameID] = " + GameID, MainFunc.conBD);
                    string gameCount = checkGame.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (gameCount == "0")
                    {
                        MessageBox.Show("Игры с таким ID не существует");
                        return;
                    }

                    // Проверка дат (EndDate >= StartDate)
                    DateTime start, end;
                    if (DateTime.TryParse(StartDate, out start) && DateTime.TryParse(EndDate, out end))
                    {
                        if (end < start)
                        {
                            MessageBox.Show("Дата окончания должна быть позже или равна дате начала");
                            return;
                        }
                    }

                    // Проверка MaxTeams (должно быть четным числом согласно ограничению)
                    int maxTeams;
                    if (int.TryParse(MaxTeams, out maxTeams))
                    {
                        if (maxTeams % 2 != 0)
                        {
                            MessageBox.Show("Количество команд должно быть четным числом");
                            return;
                        }
                    }

                    // Все проверки пройдены - добавляем турнир
                    string query = $"INSERT INTO Tournaments ([TournamentName], [GameID], [StartDate], [EndDate], [PrizePool], [Organizer], [Location], [FormatType], [MaxTeams]) " +
                                  $"VALUES ('{TournamentName}', {GameID}, '{StartDate}', '{EndDate}', " +
                                  (string.IsNullOrEmpty(PrizePool) ? "NULL" : PrizePool) + ", " +
                                  (string.IsNullOrEmpty(Organizer) ? "NULL" : "'" + Organizer + "'") + ", " +
                                  (string.IsNullOrEmpty(Location) ? "NULL" : "'" + Location + "'") + ", " +
                                  $"'{FormatType}', {MaxTeams})";

                    MainFunc.acitvateCommand(query);
                    MainFunc.ShowTable(dataGridView1, "SELECT [TournamentID] AS [ID турнира], [TournamentName] AS [Название турнира], [GameID] AS [ID игры], [StartDate] AS [Дата начала], [EndDate] AS [Дата окончания], [PrizePool] AS [Призовой фонд], [Organizer] AS [Организатор], [Location] AS [Место проведения], [FormatType] AS [Формат], [MaxTeams] AS [Макс. команд] FROM Tournaments");

                    k = dataGridView1.RowCount;
                }
                else
                {
                    MessageBox.Show("Заполните обязательные поля: Название турнира, ID игры, Дата начала, Дата окончания, Формат, Макс. команд");
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

            // Переменные под каждый столбец таблицы Tournaments
            string TournamentID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();
            string TournamentName = dataGridView1.Rows[rCount].Cells[1].Value.ToString();
            string GameID = dataGridView1.Rows[rCount].Cells[2].Value.ToString();
            string StartDate = dataGridView1.Rows[rCount].Cells[3].Value.ToString();
            string EndDate = dataGridView1.Rows[rCount].Cells[4].Value.ToString();
            string PrizePool = dataGridView1.Rows[rCount].Cells[5].Value.ToString();
            string Organizer = dataGridView1.Rows[rCount].Cells[6].Value.ToString();
            string Location = dataGridView1.Rows[rCount].Cells[7].Value.ToString();
            string FormatType = dataGridView1.Rows[rCount].Cells[8].Value.ToString();
            string MaxTeams = dataGridView1.Rows[rCount].Cells[9].Value.ToString();

            // Проверка обязательных полей
            if (!string.IsNullOrEmpty(TournamentName) && !string.IsNullOrEmpty(GameID) &&
                !string.IsNullOrEmpty(StartDate) && !string.IsNullOrEmpty(EndDate) &&
                !string.IsNullOrEmpty(FormatType) && !string.IsNullOrEmpty(MaxTeams))
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

                // Проверяем существует ли игра
                MainFunc.conBD.Open();
                SqlCommand checkGame = new SqlCommand("SELECT COUNT(*) FROM [GameTitles] WHERE [GameID] = " + GameID, MainFunc.conBD);
                string gameCount = checkGame.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (gameCount == "0")
                {
                    MessageBox.Show("Игры с таким ID не существует");
                    return;
                }

                // Проверка дат
                DateTime start, end;
                if (DateTime.TryParse(StartDate, out start) && DateTime.TryParse(EndDate, out end))
                {
                    if (end < start)
                    {
                        MessageBox.Show("Дата окончания должна быть позже или равна дате начала");
                        return;
                    }
                }

                // Проверка MaxTeams
                int maxTeams;
                if (int.TryParse(MaxTeams, out maxTeams))
                {
                    if (maxTeams % 2 != 0)
                    {
                        MessageBox.Show("Количество команд должно быть четным числом");
                        return;
                    }
                }

                // Все проверки пройдены - обновляем турнир
                string query = "UPDATE Tournaments SET " +
                              "[TournamentName] = '" + TournamentName + "', " +
                              "[GameID] = " + GameID + ", " +
                              "[StartDate] = '" + StartDate + "', " +
                              "[EndDate] = '" + EndDate + "', " +
                              "[PrizePool] = " + (string.IsNullOrEmpty(PrizePool) ? "NULL" : PrizePool.Replace(',', '.')) + ", " +
                              "[Organizer] = " + (string.IsNullOrEmpty(Organizer) ? "NULL" : "'" + Organizer + "'") + ", " +
                              "[Location] = " + (string.IsNullOrEmpty(Location) ? "NULL" : "'" + Location + "'") + ", " +
                              "[FormatType] = '" + FormatType + "', " +
                              "[MaxTeams] = " + MaxTeams + " " +
                              "WHERE [TournamentID] = " + TournamentID;

                MainFunc.acitvateCommand(query);
                MainFunc.ShowTable(dataGridView1, "SELECT [TournamentID] AS [ID турнира], [TournamentName] AS [Название турнира], [GameID] AS [ID игры], [StartDate] AS [Дата начала], [EndDate] AS [Дата окончания], [PrizePool] AS [Призовой фонд], [Organizer] AS [Организатор], [Location] AS [Место проведения], [FormatType] AS [Формат], [MaxTeams] AS [Макс. команд] FROM Tournaments");
            }
            else
            {
                MessageBox.Show("Заполните обязательные поля: Название турнира, ID игры, Дата начала, Дата окончания, Формат, Макс. команд");
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;
            string TournamentID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();

            // Проверяем, есть ли связанные записи в других таблицах
            MainFunc.conBD.Open();

            // Проверка в TournamentStages (этапы турнира)
            SqlCommand checkStages = new SqlCommand("SELECT COUNT(*) FROM [TournamentStages] WHERE [TournamentID] = " + TournamentID, MainFunc.conBD);
            string stagesCount = checkStages.ExecuteScalar().ToString();

            // Проверка в TournamentParticipants (участники турнира)
            SqlCommand checkParticipants = new SqlCommand("SELECT COUNT(*) FROM [TournamentParticipants] WHERE [TournamentID] = " + TournamentID, MainFunc.conBD);
            string participantsCount = checkParticipants.ExecuteScalar().ToString();

            // Проверка в Matches (матчи турнира)
            SqlCommand checkMatches = new SqlCommand("SELECT COUNT(*) FROM [Matches] WHERE [TournamentID] = " + TournamentID, MainFunc.conBD);
            string matchesCount = checkMatches.ExecuteScalar().ToString();

            // Проверка в Streams (трансляции турнира)
            SqlCommand checkStreams = new SqlCommand("SELECT COUNT(*) FROM [Streams] WHERE [TournamentID] = " + TournamentID, MainFunc.conBD);
            string streamsCount = checkStreams.ExecuteScalar().ToString();

            // Проверка в TournamentSponsors (спонсоры турнира)
            SqlCommand checkSponsors = new SqlCommand("SELECT COUNT(*) FROM [TournamentSponsors] WHERE [TournamentID] = " + TournamentID, MainFunc.conBD);
            string sponsorsCount = checkSponsors.ExecuteScalar().ToString();

            MainFunc.conBD.Close();

            if (stagesCount != "0" || participantsCount != "0" || matchesCount != "0" || streamsCount != "0" || sponsorsCount != "0")
            {
                MessageBox.Show("Невозможно удалить турнир, так как существуют связанные записи:\n" +
                                (stagesCount != "0" ? "- Этапы турнира\n" : "") +
                                (participantsCount != "0" ? "- Участники турнира\n" : "") +
                                (matchesCount != "0" ? "- Матчи\n" : "") +
                                (streamsCount != "0" ? "- Трансляции\n" : "") +
                                (sponsorsCount != "0" ? "- Спонсоры\n" : ""));
            }
            else
            {
                // Если нет связанных записей - удаляем турнир
                MainFunc.acitvateCommand("DELETE FROM Tournaments WHERE [TournamentID] = " + TournamentID);
                MainFunc.ShowTable(dataGridView1, "SELECT [TournamentID] AS [ID турнира], [TournamentName] AS [Название турнира], [GameID] AS [ID игры], [StartDate] AS [Дата начала], [EndDate] AS [Дата окончания], [PrizePool] AS [Призовой фонд], [Organizer] AS [Организатор], [Location] AS [Место проведения], [FormatType] AS [Формат], [MaxTeams] AS [Макс. команд] FROM Tournaments");
            }
        }

        private void Tournaments_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
            MainFunc.ShowTable(dataGridView1, "SELECT [TournamentID] AS [ID турнира], [TournamentName] AS [Название турнира], [GameID] AS [ID игры], [StartDate] AS [Дата начала], [EndDate] AS [Дата окончания], [PrizePool] AS [Призовой фонд], [Organizer] AS [Организатор], [Location] AS [Место проведения], [FormatType] AS [Формат], [MaxTeams] AS [Макс. команд] FROM Tournaments");
            k = dataGridView1.RowCount;
        }
    }
}
