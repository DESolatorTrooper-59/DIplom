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
    public partial class Players : Form
    {
        int k = 0;
        public Players()
        {
            InitializeComponent();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            menu menu = new menu();
            menu.Show();
            this.Hide();
        }

        private void Players_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
            MainFunc.ShowTable(dataGridView1, "SELECT [PlayerID] AS [ID игрока], [Nickname] AS [Никнейм], [RealName] AS [Реальное имя], [Country] AS [Страна], [BirthDate] AS [Дата рождения] FROM Players");
            k = dataGridView1.RowCount;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Application.Exit();
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

                // Переменные под каждый столбец таблицы Players (5 столбцов)
                string PlayerID = dataGridView1.Rows[k].Cells[0].Value.ToString();      // IDENTITY - не используется для вставки
                string Nickname = dataGridView1.Rows[k].Cells[1].Value.ToString();
                string RealName = dataGridView1.Rows[k].Cells[2].Value.ToString();
                string Country = dataGridView1.Rows[k].Cells[3].Value.ToString();
                string BirthDate = dataGridView1.Rows[k].Cells[4].Value.ToString();

                // Проверка что все поля заполнены
                if (!string.IsNullOrEmpty(Nickname) && !string.IsNullOrEmpty(RealName) &&
                    !string.IsNullOrEmpty(Country) && !string.IsNullOrEmpty(BirthDate))
                {
                    // Проверяем существует ли игрок с таким никнеймом (уникальное поле)
                    MainFunc.conBD.Open();
                    SqlCommand checkPlayer = new SqlCommand("SELECT COUNT(*) FROM [Players] WHERE [Nickname] = '" + Nickname + "'", MainFunc.conBD);
                    string playerCount = checkPlayer.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (playerCount == "1")
                    {
                        MessageBox.Show("Игрок с таким никнеймом уже существует");
                    }
                    else
                    {
                        // Все проверки пройдены - добавляем игрока
                        string query = $"INSERT INTO Players ([Nickname], [RealName], [Country], [BirthDate]) " +
                                      $"VALUES ('{Nickname}', '{RealName}', '{Country}', '{BirthDate}')";

                        MainFunc.acitvateCommand(query);
                        MainFunc.ShowTable(dataGridView1, "SELECT [PlayerID] AS [ID игрока], [Nickname] AS [Никнейм], [RealName] AS [Реальное имя], [Country] AS [Страна], [BirthDate] AS [Дата рождения] FROM Players");

                        k = dataGridView1.RowCount;
                    }
                }
                else
                {
                    MessageBox.Show("Заполните все поля: Никнейм, Реальное имя, Страна, Дата рождения");
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

            // Переменные под каждый столбец таблицы Players (4 столбца)
            string PlayerID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();      // ID для WHERE
            string Nickname = dataGridView1.Rows[rCount].Cells[1].Value.ToString();
            string RealName = dataGridView1.Rows[rCount].Cells[2].Value.ToString();
            string Country = dataGridView1.Rows[rCount].Cells[3].Value.ToString();
            string BirthDate = dataGridView1.Rows[rCount].Cells[4].Value.ToString();

            // Проверка обязательных полей
            if (!string.IsNullOrEmpty(Nickname))
            {
                // Проверяем существует ли игрок с таким ID
                MainFunc.conBD.Open();
                SqlCommand checkPlayer = new SqlCommand("SELECT COUNT(*) FROM [Players] WHERE [PlayerID] = " + PlayerID, MainFunc.conBD);
                string playerCount = checkPlayer.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (playerCount == "0")
                {
                    MessageBox.Show("Игрока с таким ID не существует");
                }
                else
                {
                    // Проверяем нет ли другого игрока с таким же никнеймом (кроме текущего)
                    MainFunc.conBD.Open();
                    SqlCommand checkNickname = new SqlCommand("SELECT COUNT(*) FROM [Players] WHERE [Nickname] = '" + Nickname + "' AND [PlayerID] != " + PlayerID, MainFunc.conBD);
                    string nicknameCount = checkNickname.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (nicknameCount != "0")
                    {
                        MessageBox.Show("Игрок с таким никнеймом уже существует");
                    }
                    else
                    {
                        // Все проверки пройдены - обновляем игрока
                        string query = "UPDATE Players SET " +
                                      "[Nickname] = '" + Nickname + "', " +
                                      "[RealName] = " + (string.IsNullOrEmpty(RealName) ? "NULL" : "'" + RealName + "'") + ", " +
                                      "[Country] = " + (string.IsNullOrEmpty(Country) ? "NULL" : "'" + Country + "'") + ", " +
                                      "[BirthDate] = " + (string.IsNullOrEmpty(BirthDate) ? "NULL" : "'" + BirthDate + "'") + " " +
                                      "WHERE [PlayerID] = " + PlayerID;

                        MainFunc.acitvateCommand(query);
                        MainFunc.ShowTable(dataGridView1, "SELECT [PlayerID] AS [ID игрока], [Nickname] AS [Никнейм], [RealName] AS [Реальное имя], [Country] AS [Страна], [BirthDate] AS [Дата рождения] FROM Players");
                    }
                }
            }
            else
            {
                MessageBox.Show("Заполните обязательное поле: Никнейм");
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;
            string PlayerID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();

            // Проверяем, есть ли связанные записи в других таблицах
            MainFunc.conBD.Open();

            // Проверка в TeamPlayers (игрок в командах)
            SqlCommand checkTeamPlayers = new SqlCommand("SELECT COUNT(*) FROM [TeamPlayers] WHERE [PlayerID] = " + PlayerID, MainFunc.conBD);
            string teamPlayersCount = checkTeamPlayers.ExecuteScalar().ToString();

            MainFunc.conBD.Close();

            if (teamPlayersCount != "0")
            {
                MessageBox.Show("Невозможно удалить игрока, так как он состоит в командах");
            }
            else
            {
                // Если нет связанных записей - удаляем игрока
                MainFunc.acitvateCommand("DELETE FROM Players WHERE [PlayerID] = " + PlayerID);
                MainFunc.ShowTable(dataGridView1, "SELECT [PlayerID] AS [ID игрока], [Nickname] AS [Никнейм], [RealName] AS [Реальное имя], [Country] AS [Страна], [BirthDate] AS [Дата рождения] FROM Players");
            }
        }
    }
}
