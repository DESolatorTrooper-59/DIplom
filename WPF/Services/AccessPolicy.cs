using System;
using System.Collections.Generic;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    internal static class AccessPolicy
    {
        private static readonly HashSet<string> PlayerTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Tournaments",
            "Players",
            "Teams"
        };

        private static readonly HashSet<string> GuestTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Tournaments",
            "Teams",
            "Players"
        };

        public static bool CanManageData(UserRole role)
        {
            return role == UserRole.Administrator;
        }

        public static bool CanCreateTournaments(UserRole role)
        {
            return role == UserRole.Administrator || role == UserRole.Organizer;
        }

        public static bool CanCreateTeams(UserRole role)
        {
            return role == UserRole.Administrator || role == UserRole.Organizer;
        }

        public static bool CanManageBracket(UserRole role)
        {
            return role == UserRole.Administrator;
        }

        public static bool CanManageBracket(UserRole role, string currentLogin, string tournamentOrganizer)
        {
            if (CanManageBracket(role))
            {
                return true;
            }

            return role == UserRole.Organizer &&
                   !string.IsNullOrWhiteSpace(currentLogin) &&
                   !string.IsNullOrWhiteSpace(tournamentOrganizer) &&
                   string.Equals(currentLogin, tournamentOrganizer, StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanAccessBracket(UserRole role)
        {
            return role == UserRole.Administrator || role == UserRole.Organizer || role == UserRole.Player;
        }

        public static bool CanAccessEntity(UserRole role, string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return false;
            }

            switch (role)
            {
                case UserRole.Administrator:
                    return true;
                case UserRole.Organizer:
                case UserRole.Player:
                    return PlayerTables.Contains(tableName);
                case UserRole.Guest:
                    return GuestTables.Contains(tableName);
                default:
                    return false;
            }
        }

        public static string GetRoleTitle(UserRole role)
        {
            switch (role)
            {
                case UserRole.Administrator:
                    return "Администратор";
                case UserRole.Organizer:
                    return "Организатор";
                case UserRole.Player:
                    return "Игрок";
                default:
                    return "Гость";
            }
        }

        public static string GetAccountTitle(string login, UserRole role)
        {
            if (role == UserRole.Guest || string.IsNullOrWhiteSpace(login))
            {
                return "Гость";
            }

            return login;
        }
    }
}
