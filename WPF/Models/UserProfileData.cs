using System;

namespace Tournaments.WPF.Models
{
    public sealed class UserProfileData
    {
        public UserProfileData(UserRole role, string login)
        {
            Role = role;
            Login = login;
            Nickname = login;
        }

        public UserRole Role { get; }

        public string Login { get; set; }

        public string Nickname { get; set; }

        public string RealName { get; set; }

        public string Country { get; set; }

        public DateTime? BirthDate { get; set; }

        public bool CanEditExtendedProfile { get; set; }

        public bool CanChangePassword { get; set; }
    }
}
