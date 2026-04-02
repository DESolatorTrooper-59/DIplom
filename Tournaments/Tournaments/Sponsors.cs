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
    public partial class Sponsors : Form
    {
        int k = 0;
        public Sponsors()
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

                // Переменные под каждый столбец таблицы Sponsors (3 столбца)
                string SponsorID = dataGridView1.Rows[k].Cells[0].Value.ToString();      // IDENTITY - не используется для вставки
                string SponsorName = dataGridView1.Rows[k].Cells[1].Value.ToString();
                string Industry = dataGridView1.Rows[k].Cells[2].Value.ToString();

                // Проверка что все поля заполнены
                if (!string.IsNullOrEmpty(SponsorName) && !string.IsNullOrEmpty(Industry))
                {
                    // Проверяем существует ли спонсор с таким названием
                    MainFunc.conBD.Open();
                    SqlCommand checkSponsor = new SqlCommand("SELECT COUNT(*) FROM [Sponsors] WHERE [SponsorName] = '" + SponsorName + "'", MainFunc.conBD);
                    string sponsorCount = checkSponsor.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (sponsorCount == "1")
                    {
                        MessageBox.Show("Спонсор с таким названием уже существует");
                    }
                    else
                    {
                        // Все проверки пройдены - добавляем спонсора
                        string query = $"INSERT INTO Sponsors ([SponsorName], [Industry]) " +
                                      $"VALUES ('{SponsorName}', '{Industry}')";

                        MainFunc.acitvateCommand(query);
                        MainFunc.ShowTable(dataGridView1, "SELECT [SponsorID] AS [ID спонсора], [SponsorName] AS [Название спонсора], [Industry] AS [Индустрия] FROM Sponsors");

                        k = dataGridView1.RowCount;
                    }
                }
                else
                {
                    MessageBox.Show("Заполните все поля: Название спонсора, Индустрия");
                }
            }
            catch (System.NullReferenceException)
            {
                MessageBox.Show("Заполните все поля корректно");
            }
        }

        private void Sponsors_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
            MainFunc.ShowTable(dataGridView1, "SELECT [SponsorID] AS [ID спонсора], [SponsorName] AS [Название спонсора], [Industry] AS [Индустрия] FROM Sponsors");
            k = dataGridView1.RowCount;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;

            // Переменные под каждый столбец таблицы Sponsors (3 столбца)
            string SponsorID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();      // ID для WHERE
            string SponsorName = dataGridView1.Rows[rCount].Cells[1].Value.ToString();
            string Industry = dataGridView1.Rows[rCount].Cells[2].Value.ToString();

            // Проверка что все поля заполнены
            if (!string.IsNullOrEmpty(SponsorName) && !string.IsNullOrEmpty(Industry))
            {
                // Проверяем существует ли спонсор с таким ID
                MainFunc.conBD.Open();
                SqlCommand checkSponsor = new SqlCommand("SELECT COUNT(*) FROM [Sponsors] WHERE [SponsorID] = " + SponsorID, MainFunc.conBD);
                string sponsorCount = checkSponsor.ExecuteScalar().ToString();
                MainFunc.conBD.Close();

                if (sponsorCount == "0")
                {
                    MessageBox.Show("Спонсора с таким ID не существует");
                }
                else
                {
                    // Проверяем нет ли другого спонсора с таким же названием (кроме текущего)
                    MainFunc.conBD.Open();
                    SqlCommand checkName = new SqlCommand("SELECT COUNT(*) FROM [Sponsors] WHERE [SponsorName] = '" + SponsorName + "' AND [SponsorID] != " + SponsorID, MainFunc.conBD);
                    string nameCount = checkName.ExecuteScalar().ToString();
                    MainFunc.conBD.Close();

                    if (nameCount != "0")
                    {
                        MessageBox.Show("Спонсор с таким названием уже существует");
                    }
                    else
                    {
                        // Все проверки пройдены - обновляем спонсора
                        string query = "UPDATE Sponsors SET " +
                                      "[SponsorName] = '" + SponsorName + "', " +
                                      "[Industry] = '" + Industry + "' " +
                                      "WHERE [SponsorID] = " + SponsorID;

                        MainFunc.acitvateCommand(query);
                        MainFunc.ShowTable(dataGridView1, "SELECT [SponsorID] AS [ID спонсора], [SponsorName] AS [Название спонсора], [Industry] AS [Индустрия] FROM Sponsors");
                    }
                }
            }
            else
            {
                MessageBox.Show("Заполните все поля: Название спонсора, Индустрия");
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            int rCount = dataGridView1.CurrentCell.RowIndex;
            string SponsorID = dataGridView1.Rows[rCount].Cells[0].Value.ToString();

            // Проверяем, есть ли связанные записи в таблице TournamentSponsors
            MainFunc.conBD.Open();
            SqlCommand checkTournamentSponsors = new SqlCommand("SELECT COUNT(*) FROM [TournamentSponsors] WHERE [SponsorID] = " + SponsorID, MainFunc.conBD);
            string tournamentSponsorsCount = checkTournamentSponsors.ExecuteScalar().ToString();
            MainFunc.conBD.Close();

            if (tournamentSponsorsCount != "0")
            {
                MessageBox.Show("Невозможно удалить спонсора, так как он связан с турнирами");
            }
            else
            {
                // Если нет связанных записей - удаляем спонсора
                MainFunc.acitvateCommand("DELETE FROM Sponsors WHERE [SponsorID] = " + SponsorID);
                MainFunc.ShowTable(dataGridView1, "SELECT [SponsorID] AS [ID спонсора], [SponsorName] AS [Название спонсора], [Industry] AS [Индустрия] FROM Sponsors");
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
