using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tournaments
{
    public partial class menu : Form
    {
        public menu()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Teams teams = new Teams(); 
            teams.Show();
            this.Hide();

        }

        private void button2_Click(object sender, EventArgs e)
        {
            Players Players = new Players();
            Players.Show();
            this.Hide();
        }

        private void button10_Click(object sender, EventArgs e)
        {
            Application.Exit(); 
        }

        private void menu_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            Sponsors Sponsors = new Sponsors();
            Sponsors.Show();
            this.Hide();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            Tournaments Tournaments = new Tournaments();
            Tournaments.Show();
            this.Hide();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            Streams Streams = new Streams();
            Streams.Show();
            this.Hide();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            Games Games = new Games();
            Games.Show();
            this.Hide();

        }

        private void button7_Click(object sender, EventArgs e)
        {
            Matchs Matchs = new Matchs();
            Matchs.Show();
            this.Hide();
        }

        private void button8_Click(object sender, EventArgs e)
        {

        }

        private void button9_Click(object sender, EventArgs e)
        {

        }
    }
}
