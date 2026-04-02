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
    public partial class Teams : Form
    {
        int k = 0;
        public Teams()
        {
            InitializeComponent();
        }

        private void Teams_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
            MainFunc.ShowTable(dataGridView1, "SELECT [TeamID] AS [ID команды], [TeamName] AS [Название команды], [FoundedDate] AS [Дата основания], [Country] AS [Страна], [CoachName] AS [Тренер], [CreatedDate] AS [Дата создания] FROM Teams");
            k = dataGridView1.RowCount;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Tournaments_Team Tournaments_Team = new Tournaments_Team();
            Tournaments_Team.Show();
            this.Hide();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Team_Players Team_Players = new Team_Players();
            Team_Players.Show();
            this.Hide();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            menu menu = new menu();
            menu.Show();
            this.Hide();
        }

        private void button4_Click(object sender, EventArgs e)
        {

        }

        private void button5_Click(object sender, EventArgs e)
        {
            menu menu = new menu();
            menu.Show();
            this.Hide();
        }

        private void button9_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void button8_Click(object sender, EventArgs e)
        {
            try
            {
                k = dataGridView1.RowCount - 2;

                // Переменные под каждый столбец таблицы Teams (5 столбцов)
                string TeamID = dataGridView1.Rows[k].Cells[0].Value.ToString();      // IDENTITY - не используется для вставки
                string TeamName = dataGridView1.Rows[k].Cells[1].Value.ToString();
                string FoundedDate = dataGridView1.Rows[k].Cells[2].Value.ToString();
                string Country = dataGridView1.Rows[k].Cells[3].Value.ToString();
                string CoachName = dataGridView1.Rows[k].Cells[4].Value.ToString();
                // CreatedDate - авто (GETDATE()), не вставляем

                // Проверка что все поля заполнены
                if (!string.IsNullOrEmpty(TeamName) && !string.IsNullOrEmpty(FoundedDate) &&
                    !string.IsNullOrEmpty(Country) && !string.IsNullOrEmpty(CoachName))
                {
                    // Проверяем существует ли команда с таким названием (уникальное поле)
                    MainFunc.conBD.Open();
                    SqlCommand checkTeam = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamName] = '" + TeamName + "'", MainFunc.conBD);
                    string teamCount = checkTeam.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (teamCount == "1")
                    {
                        MessageBox.Show("Команда с таким названием уже существует");
                    }
                    else
                    {
                        // Все проверки пройдены - добавляем команду
                        string query = $"INSERT INTO Teams ([TeamName], [FoundedDate], [Country], [CoachName]) " +
                                      $"VALUES ('{TeamName}', '{FoundedDate}', '{Country}', '{CoachName}')";

                        MainFunc.acitvateCommand(query);
                        MainFunc.ShowTable(dataGridView1, "SELECT [TeamID] AS [ID команды], [TeamName] AS [Название команды], [FoundedDate] AS [Дата основания], [Country] AS [Страна], [CoachName] AS [Тренер], [CreatedDate] AS [Дата создания] FROM Teams");

                        k = dataGridView1.RowCount;
                    }
                }
                else
                {
                    MessageBox.Show("Заполните все поля: Название команды, Дата основания, Страна, Тренер");
                }
            }
            catch (System.NullReferenceException)
            {
                MessageBox.Show("Заполните все поля корректно");
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;

            // Переменные под каждый столбец таблицы Teams (5 столбцов)
            string TeamID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();      // ID для WHERE
            string TeamName = dataGridView1.Rows[rCount].Cells[1].Value.ToString();
            string FoundedDate = dataGridView1.Rows[rCount].Cells[2].Value.ToString();
            string Country = dataGridView1.Rows[rCount].Cells[3].Value.ToString();
            string CoachName = dataGridView1.Rows[rCount].Cells[4].Value.ToString();
            // CreatedDate - не редактируем

            // Проверка что все поля заполнены
            if (!string.IsNullOrEmpty(TeamName) && !string.IsNullOrEmpty(FoundedDate) &&
                !string.IsNullOrEmpty(Country) && !string.IsNullOrEmpty(CoachName))
            {
                // Проверяем существует ли команда с таким ID
                MainFunc.conBD.Open();
                SqlCommand checkTeam = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamID] = " + TeamID, MainFunc.conBD);
                string teamCount = checkTeam.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (teamCount == "0")
                {
                    MessageBox.Show("Команды с таким ID не существует");
                }
                else
                {
                    // Проверяем нет ли другой команды с таким же названием (кроме текущей)
                    MainFunc.conBD.Open();
                    SqlCommand checkName = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamName] = '" + TeamName + "' AND [TeamID] != " + TeamID, MainFunc.conBD);
                    string nameCount = checkName.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (nameCount != "0")
                    {
                        MessageBox.Show("Команда с таким названием уже существует");
                    }
                    else
                    {
                        // Все проверки пройдены - обновляем команду
                        string query = "UPDATE Teams SET " +
                                      "[TeamName] = '" + TeamName + "', " +
                                      "[FoundedDate] = '" + FoundedDate + "', " +
                                      "[Country] = '" + Country + "', " +
                                      "[CoachName] = '" + CoachName + "' " +
                                      "WHERE [TeamID] = " + TeamID;

                        MainFunc.acitvateCommand(query);
                        MainFunc.ShowTable(dataGridView1, "SELECT [TeamID] AS [ID команды], [TeamName] AS [Название команды], [FoundedDate] AS [Дата основания], [Country] AS [Страна], [CoachName] AS [Тренер], [CreatedDate] AS [Дата создания] FROM Teams");
                    }
                }
            }
            else
            {
                MessageBox.Show("Заполните все поля: Название команды, Дата основания, Страна, Тренер");
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;
            string TeamID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();

            // Проверяем, есть ли связанные записи в других таблицах
            MainFunc.conBD.Open();

            // Проверка в TournamentParticipants (участие в турнирах)
            SqlCommand checkParticipants = new SqlCommand("SELECT COUNT(*) FROM [TournamentParticipants] WHERE [TeamID] = " + TeamID, MainFunc.conBD);
            string participantsCount = checkParticipants.ExecuteScalar().ToString();

            // Проверка в TeamPlayers (игроки команды)
            SqlCommand checkPlayers = new SqlCommand("SELECT COUNT(*) FROM [TeamPlayers] WHERE [TeamID] = " + TeamID, MainFunc.conBD);
            string playersCount = checkPlayers.ExecuteScalar().ToString();

            // Проверка в Matches (как Team1 или Team2)
            SqlCommand checkMatches1 = new SqlCommand("SELECT COUNT(*) FROM [Matches] WHERE [Team1ID] = " + TeamID + " OR [Team2ID] = " + TeamID, MainFunc.conBD);
            string matchesCount = checkMatches1.ExecuteScalar().ToString();

            MainFunc.conBD.Close();

            if (participantsCount != "0" || playersCount != "0" || matchesCount != "0")
            {
                MessageBox.Show("Невозможно удалить команду, так как существуют связанные записи:\n" +
                                (participantsCount != "0" ? "- Участие в турнирах\n" : "") +
                                (playersCount != "0" ? "- Игроки в команде\n" : "") +
                                (matchesCount != "0" ? "- Матчи с участием команды\n" : ""));
            }
            else
            {
                // Если нет связанных записей - удаляем команду
                MainFunc.acitvateCommand("DELETE FROM Teams WHERE [TeamID] = " + TeamID);
                MainFunc.ShowTable(dataGridView1, "SELECT [TeamID] AS [ID команды], [TeamName] AS [Название команды], [FoundedDate] AS [Дата основания], [Country] AS [Страна], [CoachName] AS [Тренер], [CreatedDate] AS [Дата создания] FROM Teams");
            }
        }

        private void button10_Click(object sender, EventArgs e)
        {
            MainFunc.Search(dataGridView1, textBox1);
        }
    }
}
