using System;
using System.Data;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    internal static class RoleRules
    {
        private const int OrganizerRoleId = 2;
        private const int AdministratorRoleId = 3;

        public static bool IsTournamentOrganizerField(FieldDefinition field)
        {
            return field != null &&
                   string.Equals(field.Name, "Organizer", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(field.LookupTableName, "Players", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(field.LookupColumnName, "Nickname", StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanOrganizeTournament(DatabaseService database, DataRow playerRow)
        {
            if (playerRow == null ||
                !playerRow.Table.Columns.Contains("RoleID") ||
                playerRow["RoleID"] == DBNull.Value)
            {
                return false;
            }

            int roleId = Convert.ToInt32(playerRow["RoleID"]);
            if (roleId == OrganizerRoleId || roleId == AdministratorRoleId)
            {
                return true;
            }

            string roleName = ResolveRoleName(database, roleId);
            return IsOrganizerOrAdministratorRoleName(roleName);
        }

        public static bool IsTournamentOrganizerLogin(DatabaseService database, string nickname)
        {
            if (database == null || string.IsNullOrWhiteSpace(nickname))
            {
                return false;
            }

            DataTable players = database.GetTable("Players");
            DataRow player = players.Rows
                .Cast<DataRow>()
                .FirstOrDefault(row =>
                    row.Table.Columns.Contains("Nickname") &&
                    row["Nickname"] != DBNull.Value &&
                    string.Equals(Convert.ToString(row["Nickname"]), nickname, StringComparison.OrdinalIgnoreCase));

            return CanOrganizeTournament(database, player);
        }

        private static string ResolveRoleName(DatabaseService database, int roleId)
        {
            if (database == null)
            {
                return string.Empty;
            }

            DataTable roles = database.GetTable("Roles");
            DataRow role = roles.Rows
                .Cast<DataRow>()
                .FirstOrDefault(row =>
                    row.Table.Columns.Contains("RoleID") &&
                    row.Table.Columns.Contains("RoleName") &&
                    row["RoleID"] != DBNull.Value &&
                    Convert.ToInt32(row["RoleID"]) == roleId);

            return role == null || role["RoleName"] == DBNull.Value ? string.Empty : Convert.ToString(role["RoleName"]);
        }

        private static bool IsOrganizerOrAdministratorRoleName(string roleName)
        {
            return string.Equals(roleName, "Организатор", StringComparison.CurrentCultureIgnoreCase) ||
                   string.Equals(roleName, "Администратор", StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
