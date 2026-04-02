using System;
using System.Configuration;
using System.Data.SqlClient;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public sealed class SqlServerConnectionService
    {
        private static readonly Lazy<SqlServerConnectionService> _instance = new Lazy<SqlServerConnectionService>(() => new SqlServerConnectionService());

        private SqlServerConnectionSettings _settings;

        private SqlServerConnectionService()
        {
            _settings = ReadFromConfiguration();
        }

        public static SqlServerConnectionService Instance => _instance.Value;

        public string ActiveConnectionString { get; private set; }

        public string ActiveConnectionLabel { get; private set; }

        public bool HasSuccessfulConnection
        {
            get { return !string.IsNullOrWhiteSpace(ActiveConnectionString); }
        }

        public SqlServerConnectionSettings GetSettings()
        {
            return (_settings ?? new SqlServerConnectionSettings()).Clone();
        }

        public void Connect(SqlServerConnectionSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Validate(settings);
            string connectionString = BuildConnectionString(settings);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
            }

            _settings = settings.Clone();
            ActiveConnectionString = connectionString;
            ActiveConnectionLabel = settings.Database + " @ " + settings.Server;
        }

        private static SqlServerConnectionSettings ReadFromConfiguration()
        {
            ConnectionStringSettings configured = ConfigurationManager.ConnectionStrings["BdCon"];
            if (configured == null || string.IsNullOrWhiteSpace(configured.ConnectionString))
            {
                return new SqlServerConnectionSettings();
            }

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(configured.ConnectionString);
            return new SqlServerConnectionSettings
            {
                Server = builder.DataSource,
                Database = builder.InitialCatalog,
                UserName = builder.IntegratedSecurity ? string.Empty : builder.UserID,
                Password = builder.IntegratedSecurity ? string.Empty : builder.Password,
                UseWindowsAuthentication = builder.IntegratedSecurity
            };
        }

        private static void Validate(SqlServerConnectionSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Server))
            {
                throw new InvalidOperationException("Укажите сервер SQL Server.");
            }

            if (string.IsNullOrWhiteSpace(settings.Database))
            {
                throw new InvalidOperationException("Укажите имя базы данных.");
            }

            if (!settings.UseWindowsAuthentication)
            {
                if (string.IsNullOrWhiteSpace(settings.UserName))
                {
                    throw new InvalidOperationException("Укажите логин SQL Server.");
                }

                if (string.IsNullOrWhiteSpace(settings.Password))
                {
                    throw new InvalidOperationException("Укажите пароль SQL Server.");
                }
            }
        }

        private static string BuildConnectionString(SqlServerConnectionSettings settings)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                DataSource = settings.Server.Trim(),
                InitialCatalog = settings.Database.Trim(),
                Encrypt = true,
                TrustServerCertificate = true,
                ConnectTimeout = 5
            };

            if (settings.UseWindowsAuthentication)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.IntegratedSecurity = false;
                builder.UserID = settings.UserName.Trim();
                builder.Password = settings.Password;
            }

            return builder.ConnectionString;
        }
    }
}
