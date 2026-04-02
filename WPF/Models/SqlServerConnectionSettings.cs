namespace Tournaments.WPF.Models
{
    public sealed class SqlServerConnectionSettings
    {
        public string Server { get; set; }

        public string Database { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public bool UseWindowsAuthentication { get; set; }

        public SqlServerConnectionSettings Clone()
        {
            return new SqlServerConnectionSettings
            {
                Server = Server,
                Database = Database,
                UserName = UserName,
                Password = Password,
                UseWindowsAuthentication = UseWindowsAuthentication
            };
        }
    }
}
