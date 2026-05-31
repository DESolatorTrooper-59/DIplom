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
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string a, b, c;
            a = textBox1.Text;
            b = textBox2.Text;
            if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b))
            {
                MainFunc.conBD.Open();
                SqlCommand sc = new SqlCommand("Select count(*) From [Organizer] Where [Login] = @Login AND [Password] = @Password", MainFunc.conBD);
                sc.Parameters.Add("@Login", SqlDbType.NVarChar, 50).Value = a;
                sc.Parameters.Add("@Password", SqlDbType.NVarChar, 128).Value = PasswordHasher.HashPassword(b);
                c = sc.ExecuteScalar().ToString();
                if (c == "1")
                {
                  menu menu=new menu();
                  menu.Show();
                  this.Hide();
                }
                else
                {
                    MessageBox.Show("Не верный логин и/или пароль");                  
                }
            }
            else MessageBox.Show("Заполните поля авторизации");
            MainFunc.conBD.Close();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Size screenSize = Screen.PrimaryScreen.WorkingArea.Size;
            Location = new Point(screenSize.Width / 2 - Width / 2, screenSize.Height / 2 - Height / 2);
        }
    }
}
