using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Tournaments.WPF.Models
{
    public sealed class TournamentCardViewModel
    {
        public int TournamentId { get; set; }

        public string TournamentName { get; set; }

        public string GameName { get; set; }

        public string FormatType { get; set; }

        public string FormatDisplay { get; set; }

        public string ParticipantMode { get; set; }

        public string ParticipantModeBadgeText { get; set; }

        public int RegisteredParticipants { get; set; }

        public int MaxParticipants { get; set; }

        public string ParticipantCountText { get; set; }

        public DateTime StartDate { get; set; }

        public string StartDateText { get; set; }

        public decimal? PrizePool { get; set; }

        public string PrizePoolText { get; set; }

        public string Organizer { get; set; }

        public string Location { get; set; }

        public string PreviewPlaceholderText { get; set; }

        public string PreviewPath { get; set; }

        public ImageSource PreviewImage { get; set; }

        public Visibility PreviewImageVisibility { get; set; }

        public Visibility PreviewPlaceholderVisibility { get; set; }

        public Visibility RegisterButtonVisibility { get; set; }

        public Visibility SettingsButtonVisibility { get; set; }

        public Visibility AdminManageParticipantsVisibility { get; set; }

        public bool IsAdminManageParticipantsEnabled { get; set; }

        public string AdminManageParticipantsToolTip { get; set; }

        public string AdminManageParticipantsButtonText { get; set; }

        public bool IsRegisterEnabled { get; set; }

        public string RegisterButtonText { get; set; }

        public string RegisterButtonToolTip { get; set; }

        public bool IsRegistered { get; set; }

        public IDictionary<string, object> OriginalValues { get; set; }
    }
}
