SET NOCOUNT ON;
GO

IF DB_ID(N'SportsTournamentDB') IS NULL
BEGIN
    EXEC (N'CREATE DATABASE [SportsTournamentDB] COLLATE Cyrillic_General_CI_AS;');
END
GO

USE [SportsTournamentDB];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Скрипт разворачивает dev/demo-базу данных для проекта.
-- Демо-учётная запись: admin / password

DROP TABLE IF EXISTS [dbo].[Streams];
DROP TABLE IF EXISTS [dbo].[Matches];
DROP TABLE IF EXISTS [dbo].[TournamentSponsors];
DROP TABLE IF EXISTS [dbo].[TournamentParticipants];
DROP TABLE IF EXISTS [dbo].[TeamPlayers];
DROP TABLE IF EXISTS [dbo].[TournamentStages];
DROP TABLE IF EXISTS [dbo].[Tournaments];
DROP TABLE IF EXISTS [dbo].[Sponsors];
DROP TABLE IF EXISTS [dbo].[Players];
DROP TABLE IF EXISTS [dbo].[Teams];
DROP TABLE IF EXISTS [dbo].[GameTitles];
DROP TABLE IF EXISTS [dbo].[Organizer];
GO

CREATE TABLE [dbo].[Organizer]
(
    [Login] NVARCHAR(50) NOT NULL,
    [Password] NVARCHAR(50) NOT NULL,
    CONSTRAINT [PK_Organizer] PRIMARY KEY CLUSTERED ([Login] ASC)
);
GO

CREATE TABLE [dbo].[GameTitles]
(
    [GameID] INT IDENTITY(1,1) NOT NULL,
    [GameName] NVARCHAR(100) NOT NULL,
    [Developer] NVARCHAR(100) NOT NULL,
    [ReleaseYear] INT NOT NULL,
    [MaxPlayersPerTeam] INT NOT NULL
        CONSTRAINT [DF_GameTitles_MaxPlayersPerTeam] DEFAULT (5),
    CONSTRAINT [PK_GameTitles] PRIMARY KEY CLUSTERED ([GameID] ASC),
    CONSTRAINT [UQ_GameTitles_GameName] UNIQUE ([GameName]),
    CONSTRAINT [CHK_GameTitles_ReleaseYear] CHECK ([ReleaseYear] >= 1970),
    CONSTRAINT [CHK_GameTitles_MaxPlayersPerTeam] CHECK ([MaxPlayersPerTeam] > 0)
);
GO

CREATE TABLE [dbo].[Teams]
(
    [TeamID] INT IDENTITY(1,1) NOT NULL,
    [TeamName] NVARCHAR(100) NOT NULL,
    [FoundedDate] DATE NOT NULL,
    [Country] NVARCHAR(50) NOT NULL,
    [CoachName] NVARCHAR(150) NOT NULL,
    [CreatedDate] DATETIME2(0) NOT NULL
        CONSTRAINT [DF_Teams_CreatedDate] DEFAULT (SYSDATETIME()),
    CONSTRAINT [PK_Teams] PRIMARY KEY CLUSTERED ([TeamID] ASC),
    CONSTRAINT [UQ_Teams_TeamName] UNIQUE ([TeamName])
);
GO

CREATE TABLE [dbo].[Players]
(
    [PlayerID] INT IDENTITY(1,1) NOT NULL,
    [Nickname] NVARCHAR(100) NOT NULL,
    [RealName] NVARCHAR(150) NOT NULL,
    [Country] NVARCHAR(50) NOT NULL,
    [BirthDate] DATE NOT NULL,
    CONSTRAINT [PK_Players] PRIMARY KEY CLUSTERED ([PlayerID] ASC),
    CONSTRAINT [UQ_Players_Nickname] UNIQUE ([Nickname])
);
GO

CREATE TABLE [dbo].[Sponsors]
(
    [SponsorID] INT IDENTITY(1,1) NOT NULL,
    [SponsorName] NVARCHAR(150) NOT NULL,
    [Industry] NVARCHAR(100) NOT NULL,
    CONSTRAINT [PK_Sponsors] PRIMARY KEY CLUSTERED ([SponsorID] ASC),
    CONSTRAINT [UQ_Sponsors_SponsorName] UNIQUE ([SponsorName])
);
GO

CREATE TABLE [dbo].[Tournaments]
(
    [TournamentID] INT IDENTITY(1,1) NOT NULL,
    [TournamentName] NVARCHAR(200) NOT NULL,
    [GameID] INT NOT NULL,
    [StartDate] DATE NOT NULL,
    [EndDate] DATE NULL,
    [PrizePool] DECIMAL(15,2) NULL,
    [Organizer] NVARCHAR(150) NULL,
    [Location] NVARCHAR(200) NULL,
    [FormatType] NVARCHAR(50) NOT NULL,
    [MaxTeams] INT NOT NULL,
    [ParticipantMode] NVARCHAR(20) NOT NULL
        CONSTRAINT [DF_Tournaments_ParticipantMode] DEFAULT (N'Команды'),
    CONSTRAINT [PK_Tournaments] PRIMARY KEY CLUSTERED ([TournamentID] ASC),
    CONSTRAINT [FK_Tournaments_GameTitles] FOREIGN KEY ([GameID])
        REFERENCES [dbo].[GameTitles] ([GameID]),
    CONSTRAINT [CHK_Tournaments_MaxTeams] CHECK ([MaxTeams] > 1),
    CONSTRAINT [CHK_Tournaments_Dates] CHECK ([EndDate] IS NULL OR [EndDate] >= [StartDate]),
    CONSTRAINT [CHK_Tournaments_ParticipantMode] CHECK ([ParticipantMode] IN (N'Команды', N'Игроки'))
);
GO

CREATE TABLE [dbo].[TournamentStages]
(
    [StageID] INT IDENTITY(1,1) NOT NULL,
    [TournamentID] INT NOT NULL,
    [StageName] NVARCHAR(100) NOT NULL,
    [StageOrder] INT NOT NULL,
    [BracketType] NVARCHAR(20) NULL,
    CONSTRAINT [PK_TournamentStages] PRIMARY KEY CLUSTERED ([StageID] ASC),
    CONSTRAINT [FK_TournamentStages_Tournaments] FOREIGN KEY ([TournamentID])
        REFERENCES [dbo].[Tournaments] ([TournamentID]),
    CONSTRAINT [UQ_TournamentStages_Tournament_StageOrder] UNIQUE ([TournamentID], [StageOrder]),
    CONSTRAINT [CHK_TournamentStages_BracketType] CHECK ([BracketType] IS NULL OR [BracketType] IN (N'Winner', N'Loser', N'Final'))
);
GO

CREATE TABLE [dbo].[TeamPlayers]
(
    [TeamPlayerID] INT IDENTITY(1,1) NOT NULL,
    [TeamID] INT NOT NULL,
    [PlayerID] INT NOT NULL,
    [JoinDate] DATE NOT NULL,
    [LeaveDate] DATE NULL,
    [IsActive] BIT NOT NULL
        CONSTRAINT [DF_TeamPlayers_IsActive] DEFAULT ((1)),
    [Role] NVARCHAR(50) NULL,
    CONSTRAINT [PK_TeamPlayers] PRIMARY KEY CLUSTERED ([TeamPlayerID] ASC),
    CONSTRAINT [FK_TeamPlayers_Teams] FOREIGN KEY ([TeamID])
        REFERENCES [dbo].[Teams] ([TeamID]),
    CONSTRAINT [FK_TeamPlayers_Players] FOREIGN KEY ([PlayerID])
        REFERENCES [dbo].[Players] ([PlayerID]),
    CONSTRAINT [CHK_TeamPlayers_LeaveDate] CHECK ([LeaveDate] IS NULL OR [LeaveDate] > [JoinDate])
);
GO

CREATE UNIQUE INDEX [UX_TeamPlayers_ActivePair]
ON [dbo].[TeamPlayers] ([TeamID], [PlayerID])
WHERE [IsActive] = 1;
GO

CREATE TABLE [dbo].[TournamentParticipants]
(
    [ParticipationID] INT IDENTITY(1,1) NOT NULL,
    [TournamentID] INT NOT NULL,
    [TeamID] INT NULL,
    [PlayerID] INT NULL,
    [Seed] INT NULL,
    [FinalPlace] INT NULL,
    CONSTRAINT [PK_TournamentParticipants] PRIMARY KEY CLUSTERED ([ParticipationID] ASC),
    CONSTRAINT [FK_TournamentParticipants_Tournaments] FOREIGN KEY ([TournamentID])
        REFERENCES [dbo].[Tournaments] ([TournamentID]),
    CONSTRAINT [FK_TournamentParticipants_Teams] FOREIGN KEY ([TeamID])
        REFERENCES [dbo].[Teams] ([TeamID]),
    CONSTRAINT [FK_TournamentParticipants_Players] FOREIGN KEY ([PlayerID])
        REFERENCES [dbo].[Players] ([PlayerID]),
    CONSTRAINT [CHK_TournamentParticipants_SingleEntity] CHECK
    (
        ([TeamID] IS NOT NULL AND [PlayerID] IS NULL) OR
        ([TeamID] IS NULL AND [PlayerID] IS NOT NULL)
    ),
    CONSTRAINT [CHK_TournamentParticipants_Seed] CHECK ([Seed] IS NULL OR [Seed] > 0),
    CONSTRAINT [CHK_TournamentParticipants_FinalPlace] CHECK ([FinalPlace] IS NULL OR [FinalPlace] > 0)
);
GO

CREATE UNIQUE INDEX [UX_TournamentParticipants_TournamentTeam]
ON [dbo].[TournamentParticipants] ([TournamentID], [TeamID])
WHERE [TeamID] IS NOT NULL;
GO

CREATE UNIQUE INDEX [UX_TournamentParticipants_TournamentPlayer]
ON [dbo].[TournamentParticipants] ([TournamentID], [PlayerID])
WHERE [PlayerID] IS NOT NULL;
GO

CREATE TABLE [dbo].[Matches]
(
    [MatchID] INT IDENTITY(1,1) NOT NULL,
    [TournamentID] INT NOT NULL,
    [StageID] INT NOT NULL,
    [MatchNumber] INT NOT NULL,
    [Team1ID] INT NULL,
    [Team2ID] INT NULL,
    [WinnerTeamID] INT NULL,
    [Team1Score] INT NOT NULL
        CONSTRAINT [DF_Matches_Team1Score] DEFAULT ((0)),
    [Team2Score] INT NOT NULL
        CONSTRAINT [DF_Matches_Team2Score] DEFAULT ((0)),
    [MatchDate] NVARCHAR(50) NULL,
    [BestOf] INT NOT NULL
        CONSTRAINT [DF_Matches_BestOf] DEFAULT ((3)),
    [Status] NVARCHAR(20) NOT NULL
        CONSTRAINT [DF_Matches_Status] DEFAULT (N'Scheduled'),
    CONSTRAINT [PK_Matches] PRIMARY KEY CLUSTERED ([MatchID] ASC),
    CONSTRAINT [FK_Matches_Tournaments] FOREIGN KEY ([TournamentID])
        REFERENCES [dbo].[Tournaments] ([TournamentID]),
    CONSTRAINT [FK_Matches_TournamentStages] FOREIGN KEY ([StageID])
        REFERENCES [dbo].[TournamentStages] ([StageID]),
    CONSTRAINT [FK_Matches_Team1] FOREIGN KEY ([Team1ID])
        REFERENCES [dbo].[Teams] ([TeamID]),
    CONSTRAINT [FK_Matches_Team2] FOREIGN KEY ([Team2ID])
        REFERENCES [dbo].[Teams] ([TeamID]),
    CONSTRAINT [FK_Matches_WinnerTeam] FOREIGN KEY ([WinnerTeamID])
        REFERENCES [dbo].[Teams] ([TeamID]),
    CONSTRAINT [UQ_Matches_Tournament_MatchNumber] UNIQUE ([TournamentID], [MatchNumber]),
    CONSTRAINT [CHK_Matches_Status] CHECK ([Status] IN (N'Scheduled', N'Live', N'Completed', N'Cancelled')),
    CONSTRAINT [CHK_Matches_Teams] CHECK ([Team1ID] IS NULL OR [Team2ID] IS NULL OR [Team1ID] <> [Team2ID]),
    CONSTRAINT [CHK_Matches_Winner] CHECK ([WinnerTeamID] IS NULL OR [WinnerTeamID] = [Team1ID] OR [WinnerTeamID] = [Team2ID]),
    CONSTRAINT [CHK_Matches_Score1] CHECK ([Team1Score] >= 0),
    CONSTRAINT [CHK_Matches_Score2] CHECK ([Team2Score] >= 0),
    CONSTRAINT [CHK_Matches_BestOf] CHECK ([BestOf] > 0 AND [BestOf] % 2 = 1)
);
GO

CREATE TABLE [dbo].[Streams]
(
    [StreamID] INT IDENTITY(1,1) NOT NULL,
    [TournamentID] INT NOT NULL,
    [MatchID] INT NULL,
    [Platform] NVARCHAR(50) NULL,
    [StreamURL] NVARCHAR(500) NULL,
    CONSTRAINT [PK_Streams] PRIMARY KEY CLUSTERED ([StreamID] ASC),
    CONSTRAINT [FK_Streams_Tournaments] FOREIGN KEY ([TournamentID])
        REFERENCES [dbo].[Tournaments] ([TournamentID]),
    CONSTRAINT [FK_Streams_Matches] FOREIGN KEY ([MatchID])
        REFERENCES [dbo].[Matches] ([MatchID])
);
GO

CREATE TABLE [dbo].[TournamentSponsors]
(
    [TournamentID] INT NOT NULL,
    [SponsorID] INT NOT NULL,
    [SponsorshipAmount] DECIMAL(15,2) NULL,
    [Currency] NVARCHAR(10) NOT NULL
        CONSTRAINT [DF_TournamentSponsors_Currency] DEFAULT (N'USD'),
    CONSTRAINT [PK_TournamentSponsors] PRIMARY KEY CLUSTERED ([TournamentID] ASC, [SponsorID] ASC),
    CONSTRAINT [FK_TournamentSponsors_Tournaments] FOREIGN KEY ([TournamentID])
        REFERENCES [dbo].[Tournaments] ([TournamentID]),
    CONSTRAINT [FK_TournamentSponsors_Sponsors] FOREIGN KEY ([SponsorID])
        REFERENCES [dbo].[Sponsors] ([SponsorID]),
    CONSTRAINT [CHK_TournamentSponsors_Amount] CHECK ([SponsorshipAmount] IS NULL OR [SponsorshipAmount] >= 0)
);
GO

INSERT INTO [dbo].[Organizer] ([Login], [Password])
VALUES (N'admin', N'password');

INSERT INTO [dbo].[GameTitles] ([GameName], [Developer], [ReleaseYear], [MaxPlayersPerTeam])
VALUES (N'Counter-Strike 2', N'Valve', 2023, 5);

INSERT INTO [dbo].[Teams] ([TeamName], [FoundedDate], [Country], [CoachName])
VALUES
    (N'NAVI', '2009-12-17', N'Ukraine', N'B1ad3'),
    (N'Team Spirit', '2015-12-05', N'Russia', N'hally');

INSERT INTO [dbo].[Players] ([Nickname], [RealName], [Country], [BirthDate])
VALUES
    (N's1mple', N'Oleksandr Kostyliev', N'Ukraine', '1997-10-02'),
    (N'donk', N'Danil Kryshkovets', N'Russia', '2007-01-25');

INSERT INTO [dbo].[Sponsors] ([SponsorName], [Industry])
VALUES (N'Red Bull', N'Energy Drinks');

INSERT INTO [dbo].[Tournaments]
(
    [TournamentName],
    [GameID],
    [StartDate],
    [EndDate],
    [PrizePool],
    [Organizer],
    [Location],
    [FormatType],
    [MaxTeams],
    [ParticipantMode]
)
VALUES
(
    N'Spring Invitational 2026',
    1,
    '2026-05-10',
    '2026-05-12',
    150000.00,
    N'admin',
    N'Moscow',
    N'Single Elimination',
    2,
    N'Команды'
);

INSERT INTO [dbo].[TournamentStages] ([TournamentID], [StageName], [StageOrder], [BracketType])
VALUES (1, N'Playoffs', 1, N'Winner');

INSERT INTO [dbo].[TeamPlayers] ([TeamID], [PlayerID], [JoinDate], [IsActive], [Role])
VALUES
    (1, 1, '2026-01-15', 1, N'AWPer'),
    (2, 2, '2026-01-20', 1, N'Star Player');

INSERT INTO [dbo].[TournamentParticipants] ([TournamentID], [TeamID], [PlayerID], [Seed], [FinalPlace])
VALUES
    (1, 1, NULL, 1, NULL),
    (1, 2, NULL, 2, NULL);

INSERT INTO [dbo].[Matches]
(
    [TournamentID],
    [StageID],
    [MatchNumber],
    [Team1ID],
    [Team2ID],
    [WinnerTeamID],
    [Team1Score],
    [Team2Score],
    [MatchDate],
    [BestOf],
    [Status]
)
VALUES
(
    1,
    1,
    1,
    1,
    2,
    NULL,
    0,
    0,
    N'2026-05-10 18:00',
    3,
    N'Scheduled'
);

INSERT INTO [dbo].[Streams] ([TournamentID], [MatchID], [Platform], [StreamURL])
VALUES (1, 1, N'Twitch', N'https://twitch.tv/tournamentsdemo');

INSERT INTO [dbo].[TournamentSponsors] ([TournamentID], [SponsorID], [SponsorshipAmount], [Currency])
VALUES (1, 1, 50000.00, N'USD');
GO
