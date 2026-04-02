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
    public partial class Team_Players : Form
    {
        int k = 0;
        public Team_Players()
        {
            InitializeComponent();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            Teams Teams = new Teams();
            Teams.Show();
            this.Hide();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            try
            {
                k = dataGridView1.RowCount - 2;

                // Переменные под каждый столбец таблицы TeamPlayers (7 столбцов)
                string TeamPlayerID = dataGridView1.Rows[k].Cells[0].Value.ToString();    // IDENTITY - не используется для вставки
                string TeamID = dataGridView1.Rows[k].Cells[1].Value.ToString();
                string PlayerID = dataGridView1.Rows[k].Cells[2].Value.ToString();
                string JoinDate = dataGridView1.Rows[k].Cells[3].Value.ToString();
                string LeaveDate = dataGridView1.Rows[k].Cells[4].Value.ToString();      // может быть NULL
                string IsActive = dataGridView1.Rows[k].Cells[5].Value.ToString();        // BIT - преобразуем в 1/0
                string Role = dataGridView1.Rows[k].Cells[6].Value.ToString();            // может быть NULL

                // Проверка обязательных полей
                if (!string.IsNullOrEmpty(TeamID) && !string.IsNullOrEmpty(PlayerID) &&
                    !string.IsNullOrEmpty(JoinDate) && !string.IsNullOrEmpty(IsActive))
                {
                    // Преобразуем IsActive в 1 или 0
                    string isActiveBit = "0";
                    if (IsActive.ToLower() == "true" || IsActive == "1" || IsActive.ToLower() == "да" || IsActive.ToLower() == "yes")
                    {
                        isActiveBit = "1";
                    }

                    // Проверяем существует ли команда с таким ID
                    MainFunc.conBD.Open();
                    SqlCommand checkTeam = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamID] = " + TeamID, MainFunc.conBD);
                    string teamCount = checkTeam.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (teamCount == "0")
                    {
                        MessageBox.Show("Команды с таким ID не существует");
                        return;
                    }

                    // Проверяем существует ли игрок с таким ID
                    MainFunc.conBD.Open();
                    SqlCommand checkPlayer = new SqlCommand("SELECT COUNT(*) FROM [Players] WHERE [PlayerID] = " + PlayerID, MainFunc.conBD);
                    string playerCount = checkPlayer.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (playerCount == "0")
                    {
                        MessageBox.Show("Игрока с таким ID не существует");
                        return;
                    }

                    // Проверяем не занят ли уже игрок в этой команде (активная запись)
                    if (isActiveBit == "1")
                    {
                        MainFunc.conBD.Open();
                        SqlCommand checkActive = new SqlCommand("SELECT COUNT(*) FROM [TeamPlayers] WHERE [PlayerID] = " + PlayerID +
                                                               " AND [TeamID] = " + TeamID + " AND [IsActive] = 1", MainFunc.conBD);
                        string activeCount = checkActive.ExecuteScalar().ToString();
                        MainFunc.conBD.Close();

                        if (activeCount != "0")
                        {
                            MessageBox.Show("Этот игрок уже активен в данной команде");
                            return;
                        }
                    }

                    // Проверка дат (LeaveDate должна быть NULL или больше JoinDate)
                    if (!string.IsNullOrEmpty(LeaveDate))
                    {
                        DateTime join, leave;
                        if (DateTime.TryParse(JoinDate, out join) && DateTime.TryParse(LeaveDate, out leave))
                        {
                            if (leave <= join)
                            {
                                MessageBox.Show("Дата ухода должна быть позже даты присоединения");
                                return;
                            }
                        }
                    }

                    // Все проверки пройдены - добавляем запись
                    string query;
                    if (string.IsNullOrEmpty(LeaveDate) && string.IsNullOrEmpty(Role))
                    {
                        // Только обязательные поля
                        query = $"INSERT INTO TeamPlayers ([TeamID], [PlayerID], [JoinDate], [IsActive]) " +
                                $"VALUES ({TeamID}, {PlayerID}, '{JoinDate}', {isActiveBit})";
                    }
                    else if (!string.IsNullOrEmpty(LeaveDate) && string.IsNullOrEmpty(Role))
                    {
                        // С LeaveDate
                        query = $"INSERT INTO TeamPlayers ([TeamID], [PlayerID], [JoinDate], [LeaveDate], [IsActive]) " +
                                $"VALUES ({TeamID}, {PlayerID}, '{JoinDate}', '{LeaveDate}', {isActiveBit})";
                    }
                    else if (string.IsNullOrEmpty(LeaveDate) && !string.IsNullOrEmpty(Role))
                    {
                        // С Role
                        query = $"INSERT INTO TeamPlayers ([TeamID], [PlayerID], [JoinDate], [IsActive], [Role]) " +
                                $"VALUES ({TeamID}, {PlayerID}, '{JoinDate}', {isActiveBit}, '{Role}')";
                    }
                    else
                    {
                        // Все поля
                        query = $"INSERT INTO TeamPlayers ([TeamID], [PlayerID], [JoinDate], [LeaveDate], [IsActive], [Role]) " +
                                $"VALUES ({TeamID}, {PlayerID}, '{JoinDate}', '{LeaveDate}', {isActiveBit}, '{Role}')";
                    }

                    MainFunc.acitvateCommand(query);
                    MainFunc.ShowTable(dataGridView1, "SELECT [TeamPlayerID] AS [ID связи], [TeamID] AS [ID команды], [PlayerID] AS [ID игрока], [JoinDate] AS [Дата присоединения], [LeaveDate] AS [Дата ухода], [IsActive] AS [Активен], [Role] AS [Роль] FROM TeamPlayers");

                    k = dataGridView1.RowCount;
                }
                else
                {
                    MessageBox.Show("Заполните обязательные поля: ID команды, ID игрока, Дата присоединения, Активен");
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

            // Переменные под каждый столбец таблицы TeamPlayers (7 столбцов)
            string TeamPlayerID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();    // ID для WHERE
            string TeamID = dataGridView1.Rows[rCount].Cells[1].Value.ToString();
            string PlayerID = dataGridView1.Rows[rCount].Cells[2].Value.ToString();
            string JoinDate = dataGridView1.Rows[rCount].Cells[3].Value.ToString();
            string LeaveDate = dataGridView1.Rows[rCount].Cells[4].Value.ToString();
            string IsActive = dataGridView1.Rows[rCount].Cells[5].Value.ToString();        // BIT - преобразуем в 1/0
            string Role = dataGridView1.Rows[rCount].Cells[6].Value.ToString();

            // Проверка обязательных полей
            if (!string.IsNullOrEmpty(TeamID) && !string.IsNullOrEmpty(PlayerID) &&
                !string.IsNullOrEmpty(JoinDate) && !string.IsNullOrEmpty(IsActive))
            {
                // Преобразуем IsActive в 1 или 0
                string isActiveBit = "0";
                if (IsActive.ToLower() == "true" || IsActive == "1" || IsActive.ToLower() == "да" || IsActive.ToLower() == "yes")
                {
                    isActiveBit = "1";
                }

                // Проверяем существует ли запись с таким ID
                MainFunc.conBD.Open();
                SqlCommand checkRecord = new SqlCommand("SELECT COUNT(*) FROM [TeamPlayers] WHERE [TeamPlayerID] = " + TeamPlayerID, MainFunc.conBD);
                string recordCount = checkRecord.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (recordCount == "0")
                {
                    MessageBox.Show("Записи с таким ID не существует");
                    return;
                }

                // Проверяем существует ли команда
                MainFunc.conBD.Open();
                SqlCommand checkTeam = new SqlCommand("SELECT COUNT(*) FROM [Teams] WHERE [TeamID] = " + TeamID, MainFunc.conBD);
                string teamCount = checkTeam.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (teamCount == "0")
                {
                    MessageBox.Show("Команды с таким ID не существует");
                    return;
                }

                // Проверяем существует ли игрок
                MainFunc.conBD.Open();
                SqlCommand checkPlayer = new SqlCommand("SELECT COUNT(*) FROM [Players] WHERE [PlayerID] = " + PlayerID, MainFunc.conBD);
                string playerCount = checkPlayer.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (playerCount == "0")
                {
                    MessageBox.Show("Игрока с таким ID не существует");
                    return;
                }

                // Проверяем не занят ли игрок в другой активной записи этой команды (исключая текущую)
                if (isActiveBit == "1")
                {
                    MainFunc.conBD.Open();
                    SqlCommand checkActive = new SqlCommand("SELECT COUNT(*) FROM [TeamPlayers] WHERE [PlayerID] = " + PlayerID +
                                                           " AND [TeamID] = " + TeamID + " AND [IsActive] = 1 AND [TeamPlayerID] != " + TeamPlayerID, MainFunc.conBD);
                    string activeCount = checkActive.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (activeCount != "0")
                    {
                        MessageBox.Show("Этот игрок уже активен в данной команде в другой записи");
                        return;
                    }
                }

                // Проверка дат
                if (!string.IsNullOrEmpty(LeaveDate))
                {
                    DateTime join, leave;
                    if (DateTime.TryParse(JoinDate, out join) && DateTime.TryParse(LeaveDate, out leave))
                    {
                        if (leave <= join)
                        {
                            MessageBox.Show("Дата ухода должна быть позже даты присоединения");
                            return;
                        }
                    }
                }

                // Все проверки пройдены - обновляем запись
                string query = "UPDATE TeamPlayers SET " +
                              "[TeamID] = " + TeamID + ", " +
                              "[PlayerID] = " + PlayerID + ", " +
                              "[JoinDate] = '" + JoinDate + "', " +
                              "[LeaveDate] = " + (string.IsNullOrEmpty(LeaveDate) ? "NULL" : "'" + LeaveDate + "'") + ", " +
                              "[IsActive] = " + isActiveBit + ", " +
                              "[Role] = " + (string.IsNullOrEmpty(Role) ? "NULL" : "'" + Role + "'") + " " +
                              "WHERE [TeamPlayerID] = " + TeamPlayerID;

                MainFunc.acitvateCommand(query);
                MainFunc.ShowTable(dataGridView1, "SELECT [TeamPlayerID] AS [ID связи], [TeamID] AS [ID команды], [PlayerID] AS [ID игрока], [JoinDate] AS [Дата присоединения], [LeaveDate] AS [Дата ухода], [IsActive] AS [Активен], [Role] AS [Роль] FROM TeamPlayers");
            }
            else
            {
                MessageBox.Show("Заполните обязательные поля: ID команды, ID игрока, Дата присоединения, Активен");
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;
            string TeamPlayerID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();

            // Для TeamPlayers нет сложных внешних связей (это сама связующая таблица)
            // Можно удалять напрямую

            MainFunc.acitvateCommand("DELETE FROM TeamPlayers WHERE [TeamPlayerID] = " + TeamPlayerID);
            MainFunc.ShowTable(dataGridView1, "SELECT [TeamPlayerID] AS [ID связи], [TeamID] AS [ID команды], [PlayerID] AS [ID игрока], [JoinDate] AS [Дата присоединения], [LeaveDate] AS [Дата ухода], [IsActive] AS [Активен], [Role] AS [Роль] FROM TeamPlayers");
        }

        private void Team_Players_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
            MainFunc.ShowTable(dataGridView1, "SELECT [TeamPlayerID] AS [ID связи], [TeamID] AS [ID команды], [PlayerID] AS [ID игрока], [JoinDate] AS [Дата присоединения], [LeaveDate] AS [Дата ухода], [IsActive] AS [Активен], [Role] AS [Роль] FROM TeamPlayers");
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
    }
}
