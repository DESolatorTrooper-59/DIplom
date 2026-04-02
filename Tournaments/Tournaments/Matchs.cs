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
    public partial class Matchs : Form
    {
        int k = 0;
        public Matchs()
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

                // Переменные под каждый столбец таблицы Matches (11 столбцов)
                string MatchID = dataGridView1.Rows[k].Cells[0].Value.ToString();          // IDENTITY - не используется для вставки
                string TournamentID = dataGridView1.Rows[k].Cells[1].Value.ToString();
                string StageID = dataGridView1.Rows[k].Cells[2].Value.ToString();
                string MatchNumber = dataGridView1.Rows[k].Cells[3].Value.ToString();
                string Team1ID = dataGridView1.Rows[k].Cells[4].Value.ToString();          // может быть NULL
                string Team2ID = dataGridView1.Rows[k].Cells[5].Value.ToString();          // может быть NULL
                string WinnerTeamID = dataGridView1.Rows[k].Cells[6].Value.ToString();     // может быть NULL
                string Team1Score = dataGridView1.Rows[k].Cells[7].Value.ToString();       // может быть NULL
                string Team2Score = dataGridView1.Rows[k].Cells[8].Value.ToString();       // может быть NULL
                string MatchDate = dataGridView1.Rows[k].Cells[9].Value.ToString();        // может быть NULL
                string BestOf = dataGridView1.Rows[k].Cells[10].Value.ToString();          // может быть NULL (по умолчанию 3)
                string Status = dataGridView1.Rows[k].Cells[11].Value.ToString();          // может быть NULL (по умолчанию 'Scheduled')

                // Проверка обязательных полей
                if (!string.IsNullOrEmpty(TournamentID) && !string.IsNullOrEmpty(StageID) && !string.IsNullOrEmpty(MatchNumber))
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

                    // Проверяем существует ли этап с таким ID
                    MainFunc.conBD.Open();
                    SqlCommand checkStage = new SqlCommand("SELECT COUNT(*) FROM [TournamentStages] WHERE [StageID] = " + StageID, MainFunc.conBD);
                    string stageCount = checkStage.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (stageCount == "0")
                    {
                        MessageBox.Show("Этапа с таким ID не существует");
                        return;
                    }

                    // Проверяем Team1ID если указан
                    if (!string.IsNullOrEmpty(Team1ID))
                    {
                        MainFunc.conBD.Open();
                        SqlCommand checkTeam1 = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamID] = " + Team1ID, MainFunc.conBD);
                        string team1Count = checkTeam1.ExecuteScalar().ToString();
                        MainFunc.conBD.Close();

                        if (team1Count == "0")
                        {
                            MessageBox.Show("Команды 1 с таким ID не существует");
                            return;
                        }
                    }

                    // Проверяем Team2ID если указан
                    if (!string.IsNullOrEmpty(Team2ID))
                    {
                        MainFunc.conBD.Open();
                        SqlCommand checkTeam2 = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamID] = " + Team2ID, MainFunc.conBD);
                        string team2Count = checkTeam2.ExecuteScalar().ToString();
                        MainFunc.conBD.Close();

                        if (team2Count == "0")
                        {
                            MessageBox.Show("Команды 2 с таким ID не существует");
                            return;
                        }
                    }

                    // Проверка что команды разные (если обе указаны)
                    if (!string.IsNullOrEmpty(Team1ID) && !string.IsNullOrEmpty(Team2ID) && Team1ID == Team2ID)
                    {
                        MessageBox.Show("Команды должны быть разными");
                        return;
                    }

                    // Проверяем WinnerTeamID если указан
                    if (!string.IsNullOrEmpty(WinnerTeamID))
                    {
                        MainFunc.conBD.Open();
                        SqlCommand checkWinner = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamID] = " + WinnerTeamID, MainFunc.conBD);
                        string winnerCount = checkWinner.ExecuteScalar().ToString();
                        MainFunc.conBD.Close();

                        if (winnerCount == "0")
                        {
                            MessageBox.Show("Команды-победителя с таким ID не существует");
                            return;
                        }

                        // Проверяем что победитель - одна из команд
                        if ((!string.IsNullOrEmpty(Team1ID) && WinnerTeamID == Team1ID) ||
                            (!string.IsNullOrEmpty(Team2ID) && WinnerTeamID == Team2ID))
                        {
                            // OK
                        }
                        else
                        {
                            MessageBox.Show("Победитель должен быть одной из команд матча");
                            return;
                        }
                    }

                    // Проверка статуса
                    if (!string.IsNullOrEmpty(Status))
                    {
                        string[] validStatuses = { "Scheduled", "Live", "Completed", "Cancelled" };
                        bool validStatus = false;
                        foreach (string s in validStatuses)
                        {
                            if (Status == s)
                            {
                                validStatus = true;
                                break;
                            }
                        }
                        if (!validStatus)
                        {
                            MessageBox.Show("Статус должен быть: Scheduled, Live, Completed или Cancelled");
                            return;
                        }
                    }

                    // Все проверки пройдены - добавляем матч
                    string query = "INSERT INTO Matches ([TournamentID], [StageID], [MatchNumber], [Team1ID], [Team2ID], [WinnerTeamID], [Team1Score], [Team2Score], [MatchDate], [BestOf], [Status]) VALUES (" +
                                  TournamentID + ", " +
                                  StageID + ", " +
                                  MatchNumber + ", " +
                                  (string.IsNullOrEmpty(Team1ID) ? "NULL" : Team1ID) + ", " +
                                  (string.IsNullOrEmpty(Team2ID) ? "NULL" : Team2ID) + ", " +
                                  (string.IsNullOrEmpty(WinnerTeamID) ? "NULL" : WinnerTeamID) + ", " +
                                  (string.IsNullOrEmpty(Team1Score) ? "NULL" : Team1Score) + ", " +
                                  (string.IsNullOrEmpty(Team2Score) ? "NULL" : Team2Score) + ", " +
                                  (string.IsNullOrEmpty(MatchDate) ? "NULL" : "'" + MatchDate + "'") + ", " +
                                  (string.IsNullOrEmpty(BestOf) ? "NULL" : BestOf) + ", " +
                                  (string.IsNullOrEmpty(Status) ? "'Scheduled'" : "'" + Status + "'") + ")";

                    MainFunc.acitvateCommand(query);
                    MainFunc.ShowTable(dataGridView1, "SELECT [MatchID] AS [ID матча], [TournamentID] AS [ID турнира], [StageID] AS [ID этапа], [MatchNumber] AS [Номер матча], [Team1ID] AS [Команда 1], [Team2ID] AS [Команда 2], [WinnerTeamID] AS [Победитель], [Team1Score] AS [Счет 1], [Team2Score] AS [Счет 2], [MatchDate] AS [Дата матча], [BestOf] AS [Best Of], [Status] AS [Статус] FROM Matches");

                    k = dataGridView1.RowCount;
                }
                else
                {
                    MessageBox.Show("Заполните обязательные поля: ID турнира, ID этапа, Номер матча");
                }
            }
            catch (System.NullReferenceException)
            {
                MessageBox.Show("Заполните все поля корректно");
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;
            string MatchID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();

            // Проверяем, есть ли связанные записи в таблице Streams
            MainFunc.conBD.Open();
            SqlCommand checkStreams = new SqlCommand("SELECT COUNT(*) FROM [Streams] WHERE [MatchID] = " + MatchID, MainFunc.conBD);
            string streamsCount = checkStreams.ExecuteScalar().ToString();
            MainFunc.conBD.Close();

            if (streamsCount != "0")
            {
                MessageBox.Show("Невозможно удалить матч, так как существуют связанные трансляции");
            }
            else
            {
                // Если нет связанных записей - удаляем матч
                MainFunc.acitvateCommand("DELETE FROM Matches WHERE [MatchID] = " + MatchID);
                MainFunc.ShowTable(dataGridView1, "SELECT [MatchID] AS [ID матча], [TournamentID] AS [ID турнира], [StageID] AS [ID этапа], [MatchNumber] AS [Номер матча], [Team1ID] AS [Команда 1], [Team2ID] AS [Команда 2], [WinnerTeamID] AS [Победитель], [Team1Score] AS [Счет 1], [Team2Score] AS [Счет 2], [MatchDate] AS [Дата матча], [BestOf] AS [Best Of], [Status] AS [Статус] FROM Matches");
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;

            // Переменные под каждый столбец таблицы Matches (11 столбцов)
            string MatchID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();
            string TournamentID = dataGridView1.Rows[rCount].Cells[1].Value.ToString();
            string StageID = dataGridView1.Rows[rCount].Cells[2].Value.ToString();
            string MatchNumber = dataGridView1.Rows[rCount].Cells[3].Value.ToString();
            string Team1ID = dataGridView1.Rows[rCount].Cells[4].Value.ToString();
            string Team2ID = dataGridView1.Rows[rCount].Cells[5].Value.ToString();
            string WinnerTeamID = dataGridView1.Rows[rCount].Cells[6].Value.ToString();
            string Team1Score = dataGridView1.Rows[rCount].Cells[7].Value.ToString();
            string Team2Score = dataGridView1.Rows[rCount].Cells[8].Value.ToString();
            string MatchDate = dataGridView1.Rows[rCount].Cells[9].Value.ToString();
            string BestOf = dataGridView1.Rows[rCount].Cells[10].Value.ToString();
            string Status = dataGridView1.Rows[rCount].Cells[11].Value.ToString();

            // Проверка обязательных полей
            if (!string.IsNullOrEmpty(TournamentID) && !string.IsNullOrEmpty(StageID) && !string.IsNullOrEmpty(MatchNumber))
            {
                // Проверяем существует ли матч с таким ID
                MainFunc.conBD.Open();
                SqlCommand checkMatch = new SqlCommand("SELECT COUNT(*) FROM [Matches] WHERE [MatchID] = " + MatchID, MainFunc.conBD);
                string matchCount = checkMatch.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (matchCount == "0")
                {
                    MessageBox.Show("Матча с таким ID не существует");
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

                // Проверяем существует ли этап
                MainFunc.conBD.Open();
                SqlCommand checkStage = new SqlCommand("SELECT COUNT(*) FROM [TournamentStages] WHERE [StageID] = " + StageID, MainFunc.conBD);
                string stageCount = checkStage.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (stageCount == "0")
                {
                    MessageBox.Show("Этапа с таким ID не существует");
                    return;
                }

                // Проверяем Team1ID если указан
                if (!string.IsNullOrEmpty(Team1ID))
                {
                    MainFunc.conBD.Open();
                    SqlCommand checkTeam1 = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamID] = " + Team1ID, MainFunc.conBD);
                    string team1Count = checkTeam1.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (team1Count == "0")
                    {
                        MessageBox.Show("Команды 1 с таким ID не существует");
                        return;
                    }
                }

                // Проверяем Team2ID если указан
                if (!string.IsNullOrEmpty(Team2ID))
                {
                    MainFunc.conBD.Open();
                    SqlCommand checkTeam2 = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamID] = " + Team2ID, MainFunc.conBD);
                    string team2Count = checkTeam2.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (team2Count == "0")
                    {
                        MessageBox.Show("Команды 2 с таким ID не существует");
                        return;
                    }
                }

                // Проверка что команды разные
                if (!string.IsNullOrEmpty(Team1ID) && !string.IsNullOrEmpty(Team2ID) && Team1ID == Team2ID)
                {
                    MessageBox.Show("Команды должны быть разными");
                    return;
                }

                // Проверяем WinnerTeamID если указан
                if (!string.IsNullOrEmpty(WinnerTeamID))
                {
                    MainFunc.conBD.Open();
                    SqlCommand checkWinner = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamID] = " + WinnerTeamID, MainFunc.conBD);
                    string winnerCount = checkWinner.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (winnerCount == "0")
                    {
                        MessageBox.Show("Команды-победителя с таким ID не существует");
                        return;
                    }

                    // Проверяем что победитель - одна из команд
                    if ((!string.IsNullOrEmpty(Team1ID) && WinnerTeamID == Team1ID) ||
                        (!string.IsNullOrEmpty(Team2ID) && WinnerTeamID == Team2ID))
                    {
                        // OK
                    }
                    else
                    {
                        MessageBox.Show("Победитель должен быть одной из команд матча");
                        return;
                    }
                }

                // Проверка статуса
                if (!string.IsNullOrEmpty(Status))
                {
                    string[] validStatuses = { "Scheduled", "Live", "Completed", "Cancelled" };
                    bool validStatus = false;
                    foreach (string s in validStatuses)
                    {
                        if (Status == s)
                        {
                            validStatus = true;
                            break;
                        }
                    }
                    if (!validStatus)
                    {
                        MessageBox.Show("Статус должен быть: Scheduled, Live, Completed или Cancelled");
                        return;
                    }
                }

                // Все проверки пройдены - обновляем матч
                string query = "UPDATE Matches SET " +
                              "[TournamentID] = " + TournamentID + ", " +
                              "[StageID] = " + StageID + ", " +
                              "[MatchNumber] = " + MatchNumber + ", " +
                              "[Team1ID] = " + (string.IsNullOrEmpty(Team1ID) ? "NULL" : Team1ID) + ", " +
                              "[Team2ID] = " + (string.IsNullOrEmpty(Team2ID) ? "NULL" : Team2ID) + ", " +
                              "[WinnerTeamID] = " + (string.IsNullOrEmpty(WinnerTeamID) ? "NULL" : WinnerTeamID) + ", " +
                              "[Team1Score] = " + (string.IsNullOrEmpty(Team1Score) ? "NULL" : Team1Score) + ", " +
                              "[Team2Score] = " + (string.IsNullOrEmpty(Team2Score) ? "NULL" : Team2Score) + ", " +
                              "[MatchDate] = " + (string.IsNullOrEmpty(MatchDate) ? "NULL" : "'" + MatchDate + "'") + ", " +
                              "[BestOf] = " + (string.IsNullOrEmpty(BestOf) ? "NULL" : BestOf) + ", " +
                              "[Status] = " + (string.IsNullOrEmpty(Status) ? "'Scheduled'" : "'" + Status + "'") + " " +
                              "WHERE [MatchID] = " + MatchID;

                MainFunc.acitvateCommand(query);
                MainFunc.ShowTable(dataGridView1, "SELECT [MatchID] AS [ID матча], [TournamentID] AS [ID турнира], [StageID] AS [ID этапа], [MatchNumber] AS [Номер матча], [Team1ID] AS [Команда 1], [Team2ID] AS [Команда 2], [WinnerTeamID] AS [Победитель], [Team1Score] AS [Счет 1], [Team2Score] AS [Счет 2], [MatchDate] AS [Дата матча], [BestOf] AS [Best Of], [Status] AS [Статус] FROM Matches");
            }
            else
            {
                MessageBox.Show("Заполните обязательные поля: ID турнира, ID этапа, Номер матча");
            }
        }

        private void Matchs_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
            MainFunc.ShowTable(dataGridView1, "SELECT [MatchID] AS [ID матча], [TournamentID] AS [ID турнира], [StageID] AS [ID этапа], [MatchNumber] AS [Номер матча], [Team1ID] AS [Команда 1], [Team2ID] AS [Команда 2], [WinnerTeamID] AS [Победитель], [Team1Score] AS [Счет 1], [Team2Score] AS [Счет 2], [MatchDate] AS [Дата матча], [BestOf] AS [Best Of], [Status] AS [Статус] FROM Matches");
            k = dataGridView1.RowCount;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
