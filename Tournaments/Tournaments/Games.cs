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
    public partial class Games : Form
    {   int k=0; 
        public Games()
        {
            InitializeComponent();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            menu menu = new menu();
            menu.Show();
            this.Hide();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;
            string GameID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();

            // Проверяем, есть ли связанные записи в других таблицах (турниры)
            MainFunc.conBD.Open();
            SqlCommand checkTournaments = new SqlCommand("SELECT COUNT(*) FROM [Tournaments] WHERE [GameID] = " + GameID, MainFunc.conBD);
            string tournamentsCount = checkTournaments.ExecuteScalar().ToString();
            MainFunc.conBD.Close();

            if (tournamentsCount != "0")
            {
                MessageBox.Show("Невозможно удалить игру, так как существуют турниры, связанные с ней");
            }
            else
            {
                // Если нет связанных записей - удаляем игру
                MainFunc.acitvateCommand("DELETE FROM GameTitles WHERE [GameID] = " + GameID);
                MainFunc.ShowTable(dataGridView1, "SELECT [GameID] AS [ID игры], [GameName] AS [Название игры], [Developer] AS [Разработчик], [ReleaseYear] AS [Год выпуска], [MaxPlayersPerTeam] AS [Макс. игроков] FROM GameTitles");
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;

            // Переменные под каждый столбец таблицы GameTitles (5 столбцов)
            string GameID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();      // ID для WHERE
            string GameName = dataGridView1.Rows[rCount].Cells[1].Value.ToString();
            string Developer = dataGridView1.Rows[rCount].Cells[2].Value.ToString();
            string ReleaseYear = dataGridView1.Rows[rCount].Cells[3].Value.ToString();
            string MaxPlayersPerTeam = dataGridView1.Rows[rCount].Cells[4].Value.ToString();

            // Проверка что все поля заполнены
            if (!string.IsNullOrEmpty(GameName) && !string.IsNullOrEmpty(Developer) &&
                !string.IsNullOrEmpty(ReleaseYear) && !string.IsNullOrEmpty(MaxPlayersPerTeam))
            {
                // Проверяем существует ли игра с таким ID (на всякий случай)
                MainFunc.conBD.Open();
                SqlCommand checkGame = new SqlCommand("SELECT COUNT(*) FROM [GameTitles] WHERE [GameID] = " + GameID, MainFunc.conBD);
                string gameCount = checkGame.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (gameCount == "0")
                {
                    MessageBox.Show("Игры с таким ID не существует");
                }
                else
                {
                    // Проверяем нет ли другой игры с таким же названием (кроме текущей)
                    MainFunc.conBD.Open();
                    SqlCommand checkName = new SqlCommand("SELECT COUNT(*) FROM [GameTitles] WHERE [GameName] = '" + GameName + "' AND [GameID] != " + GameID, MainFunc.conBD);
                    string nameCount = checkName.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (nameCount != "0")
                    {
                        MessageBox.Show("Игра с таким названием уже существует");
                    }
                    else
                    {
                        // Все проверки пройдены - обновляем игру
                        string query = "UPDATE GameTitles SET " +
                                      "[GameName] = '" + GameName + "', " +
                                      "[Developer] = '" + Developer + "', " +
                                      "[ReleaseYear] = " + ReleaseYear + ", " +
                                      "[MaxPlayersPerTeam] = " + MaxPlayersPerTeam + " " +
                                      "WHERE [GameID] = " + GameID;

                        MainFunc.acitvateCommand(query);
                        MainFunc.ShowTable(dataGridView1, "SELECT [GameID] AS [ID игры], [GameName] AS [Название игры], [Developer] AS [Разработчик], [ReleaseYear] AS [Год выпуска], [MaxPlayersPerTeam] AS [Макс. игроков] FROM GameTitles");
                    }
                }
            }
            else
            {
                MessageBox.Show("Заполните все поля: Название игры, Разработчик, Год выпуска, Макс. игроков");
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            try
            {
                k = dataGridView1.RowCount - 2;

                // Переменные под каждый столбец таблицы GameTitles (5 столбцов)
                string GameID = dataGridView1.Rows[k].Cells[0].Value.ToString();      // IDENTITY - не используется для вставки
                string GameName = dataGridView1.Rows[k].Cells[1].Value.ToString();
                string Developer = dataGridView1.Rows[k].Cells[2].Value.ToString();
                string ReleaseYear = dataGridView1.Rows[k].Cells[3].Value.ToString();
                string MaxPlayersPerTeam = dataGridView1.Rows[k].Cells[4].Value.ToString();

                // Проверка что все поля заполнены
                if (!string.IsNullOrEmpty(GameName) && !string.IsNullOrEmpty(Developer) &&
                    !string.IsNullOrEmpty(ReleaseYear) && !string.IsNullOrEmpty(MaxPlayersPerTeam))
                {
                    // Проверяем существует ли игра с таким названием (уникальное поле)
                    MainFunc.conBD.Open();
                    SqlCommand checkGame = new SqlCommand("SELECT COUNT(*) FROM [GameTitles] WHERE [GameName] = '" + GameName + "'", MainFunc.conBD);
                    string gameCount = checkGame.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (gameCount == "1")
                    {
                        MessageBox.Show("Игра с таким названием уже существует");
                    }
                    else
                    {
                        // Все проверки пройдены - добавляем игру
                        string query = $"INSERT INTO GameTitles ([GameName], [Developer], [ReleaseYear], [MaxPlayersPerTeam]) " +
                                      $"VALUES ('{GameName}', '{Developer}', {ReleaseYear}, {MaxPlayersPerTeam})";

                        MainFunc.acitvateCommand(query);
                        MainFunc.ShowTable(dataGridView1, "SELECT [GameID] AS [ID игры], [GameName] AS [Название игры], [Developer] AS [Разработчик], [ReleaseYear] AS [Год выпуска], [MaxPlayersPerTeam] AS [Макс. игроков] FROM GameTitles");

                        k = dataGridView1.RowCount;
                    }
                }
                else
                {
                    MessageBox.Show("Заполните все поля: Название игры, Разработчик, Год выпуска, Макс. игроков");
                }
            }
            catch (System.NullReferenceException)
            {
                MessageBox.Show("Заполните все поля корректно");
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            MainFunc.Search(dataGridView1, textBox1);
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void Games_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
            MainFunc.ShowTable(dataGridView1, "SELECT [GameID] AS [ID игры], [GameName] AS [Название игры], [Developer] AS [Разработчик], [ReleaseYear] AS [Год выпуска], [MaxPlayersPerTeam] AS [Макс. игроков] FROM GameTitles");
            k = dataGridView1.RowCount;
        }
    }
}
