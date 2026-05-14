SET NOCOUNT ON;
GO

USE [master];
GO

IF DB_ID(N'SportsTournamentDB') IS NULL
BEGIN
    BEGIN TRY
        EXEC (N'CREATE DATABASE [SportsTournamentDB] COLLATE Cyrillic_General_CI_AS;');
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (5170, 1802)
        BEGIN
            THROW;
        END;

        DECLARE @DataPath NVARCHAR(4000) = CONVERT(NVARCHAR(4000), SERVERPROPERTY(N'InstanceDefaultDataPath'));
        DECLARE @LogPath NVARCHAR(4000) = CONVERT(NVARCHAR(4000), SERVERPROPERTY(N'InstanceDefaultLogPath'));

        IF NULLIF(@DataPath, N'') IS NULL
        BEGIN
            SELECT @DataPath = LEFT([physical_name], LEN([physical_name]) - CHARINDEX(N'\', REVERSE([physical_name])) + 1)
            FROM [master].[sys].[master_files]
            WHERE [database_id] = DB_ID(N'master') AND [file_id] = 1;
        END;

        IF NULLIF(@LogPath, N'') IS NULL
        BEGIN
            SELECT @LogPath = LEFT([physical_name], LEN([physical_name]) - CHARINDEX(N'\', REVERSE([physical_name])) + 1)
            FROM [master].[sys].[master_files]
            WHERE [database_id] = DB_ID(N'master') AND [file_id] = 2;
        END;

        IF RIGHT(@DataPath, 1) NOT IN (N'\', N'/') SET @DataPath += N'\';
        IF RIGHT(@LogPath, 1) NOT IN (N'\', N'/') SET @LogPath += N'\';

        DECLARE @Suffix NVARCHAR(32) = REPLACE(REPLACE(REPLACE(CONVERT(NVARCHAR(19), SYSDATETIME(), 126), N'-', N''), N':', N''), N'T', N'_');
        DECLARE @DataFile NVARCHAR(4000) = @DataPath + N'SportsTournamentDB_' + @Suffix + N'.mdf';
        DECLARE @LogFile NVARCHAR(4000) = @LogPath + N'SportsTournamentDB_log_' + @Suffix + N'.ldf';
        DECLARE @CreateSql NVARCHAR(MAX) =
N'CREATE DATABASE [SportsTournamentDB]
ON PRIMARY
(
    NAME = N''SportsTournamentDB'',
    FILENAME = N''' + REPLACE(@DataFile, N'''', N'''''') + N'''
)
LOG ON
(
    NAME = N''SportsTournamentDB_log'',
    FILENAME = N''' + REPLACE(@LogFile, N'''', N'''''') + N'''
)
COLLATE Cyrillic_General_CI_AS;';

        PRINT N'Файл базы данных с именем по умолчанию уже существует. Создается новая база с уникальными физическими именами файлов.';
        EXEC (@CreateSql);
    END CATCH;
END
GO

USE [SportsTournamentDB];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Скрипт разворачивает dev/demo-базу данных для проекта.
-- Физическая схема использует русские имена таблиц и столбцов.
-- Для совместимости со старым кодом ниже создаются английские VIEW-алиасы.
-- Демо-учётная запись администратора: admin / password

IF OBJECT_ID(N'[dbo].[Organizer]', N'V') IS NOT NULL DROP VIEW [dbo].[Organizer];
IF OBJECT_ID(N'[dbo].[GameTitles]', N'V') IS NOT NULL DROP VIEW [dbo].[GameTitles];
IF OBJECT_ID(N'[dbo].[Teams]', N'V') IS NOT NULL DROP VIEW [dbo].[Teams];
IF OBJECT_ID(N'[dbo].[Players]', N'V') IS NOT NULL DROP VIEW [dbo].[Players];
IF OBJECT_ID(N'[dbo].[Sponsors]', N'V') IS NOT NULL DROP VIEW [dbo].[Sponsors];
IF OBJECT_ID(N'[dbo].[Tournaments]', N'V') IS NOT NULL DROP VIEW [dbo].[Tournaments];
IF OBJECT_ID(N'[dbo].[TournamentStages]', N'V') IS NOT NULL DROP VIEW [dbo].[TournamentStages];
IF OBJECT_ID(N'[dbo].[TeamPlayers]', N'V') IS NOT NULL DROP VIEW [dbo].[TeamPlayers];
IF OBJECT_ID(N'[dbo].[TournamentParticipants]', N'V') IS NOT NULL DROP VIEW [dbo].[TournamentParticipants];
IF OBJECT_ID(N'[dbo].[Matches]', N'V') IS NOT NULL DROP VIEW [dbo].[Matches];
IF OBJECT_ID(N'[dbo].[Streams]', N'V') IS NOT NULL DROP VIEW [dbo].[Streams];
IF OBJECT_ID(N'[dbo].[TournamentSponsors]', N'V') IS NOT NULL DROP VIEW [dbo].[TournamentSponsors];
GO

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

DROP TABLE IF EXISTS [dbo].[Трансляции];
DROP TABLE IF EXISTS [dbo].[Матчи];
DROP TABLE IF EXISTS [dbo].[СпонсорыТурниров];
DROP TABLE IF EXISTS [dbo].[УчастникиТурниров];
DROP TABLE IF EXISTS [dbo].[СоставыКоманд];
DROP TABLE IF EXISTS [dbo].[ЭтапыТурниров];
DROP TABLE IF EXISTS [dbo].[Турниры];
DROP TABLE IF EXISTS [dbo].[Спонсоры];
DROP TABLE IF EXISTS [dbo].[Игроки];
DROP TABLE IF EXISTS [dbo].[Команды];
DROP TABLE IF EXISTS [dbo].[Игры];
DROP TABLE IF EXISTS [dbo].[Организаторы];
GO

CREATE TABLE [dbo].[Организаторы]
(
    [Логин] NVARCHAR(50) NOT NULL,
    [Пароль] NVARCHAR(50) NOT NULL,
    CONSTRAINT [PK_Organizer] PRIMARY KEY CLUSTERED ([Логин] ASC)
);
GO

CREATE TABLE [dbo].[Игры]
(
    [IDИгры] INT IDENTITY(1,1) NOT NULL,
    [НазваниеИгры] NVARCHAR(100) NOT NULL,
    [Разработчик] NVARCHAR(100) NOT NULL,
    [ГодВыпуска] INT NOT NULL,
    [МаксИгроковВКоманде] INT NOT NULL
        CONSTRAINT [DF_GameTitles_MaxPlayersPerTeam] DEFAULT (5),
    CONSTRAINT [PK_GameTitles] PRIMARY KEY CLUSTERED ([IDИгры] ASC),
    CONSTRAINT [UQ_GameTitles_GameName] UNIQUE ([НазваниеИгры]),
    CONSTRAINT [CHK_GameTitles_ReleaseYear] CHECK ([ГодВыпуска] >= 1970),
    CONSTRAINT [CHK_GameTitles_MaxPlayersPerTeam] CHECK ([МаксИгроковВКоманде] > 0)
);
GO

CREATE TABLE [dbo].[Команды]
(
    [IDКоманды] INT IDENTITY(1,1) NOT NULL,
    [НазваниеКоманды] NVARCHAR(100) NOT NULL,
    [ДатаОснования] DATE NOT NULL,
    [Страна] NVARCHAR(50) NOT NULL,
    [ИмяТренера] NVARCHAR(150) NOT NULL,
    [ДатаСоздания] DATETIME2(0) NOT NULL
        CONSTRAINT [DF_Teams_CreatedDate] DEFAULT (SYSDATETIME()),
    CONSTRAINT [PK_Teams] PRIMARY KEY CLUSTERED ([IDКоманды] ASC),
    CONSTRAINT [UQ_Teams_TeamName] UNIQUE ([НазваниеКоманды])
);
GO

CREATE TABLE [dbo].[Игроки]
(
    [IDИгрока] INT IDENTITY(1,1) NOT NULL,
    [Никнейм] NVARCHAR(100) NOT NULL,
    [НастоящееИмя] NVARCHAR(150) NOT NULL,
    [Страна] NVARCHAR(50) NOT NULL,
    [ДатаРождения] DATE NOT NULL,
    [Пароль] NVARCHAR(50) NULL,
    CONSTRAINT [PK_Players] PRIMARY KEY CLUSTERED ([IDИгрока] ASC),
    CONSTRAINT [UQ_Players_Nickname] UNIQUE ([Никнейм])
);
GO

CREATE TABLE [dbo].[Спонсоры]
(
    [IDСпонсора] INT IDENTITY(1,1) NOT NULL,
    [НазваниеСпонсора] NVARCHAR(150) NOT NULL,
    [Индустрия] NVARCHAR(100) NOT NULL,
    CONSTRAINT [PK_Sponsors] PRIMARY KEY CLUSTERED ([IDСпонсора] ASC),
    CONSTRAINT [UQ_Sponsors_SponsorName] UNIQUE ([НазваниеСпонсора])
);
GO

CREATE TABLE [dbo].[Турниры]
(
    [IDТурнира] INT IDENTITY(1,1) NOT NULL,
    [НазваниеТурнира] NVARCHAR(200) NOT NULL,
    [IDИгры] INT NOT NULL,
    [ДатаНачала] DATE NOT NULL,
    [ДатаОкончания] DATE NULL,
    [ПризовойФонд] DECIMAL(15,2) NULL,
    [Организатор] NVARCHAR(150) NULL,
    [МестоПроведения] NVARCHAR(200) NULL,
    [ТипФормата] NVARCHAR(50) NOT NULL,
    [МаксУчастников] INT NOT NULL,
    [ТипУчастников] NVARCHAR(20) NOT NULL
        CONSTRAINT [DF_Tournaments_ParticipantMode] DEFAULT (N'Команды'),
    CONSTRAINT [PK_Tournaments] PRIMARY KEY CLUSTERED ([IDТурнира] ASC),
    CONSTRAINT [FK_Tournaments_GameTitles] FOREIGN KEY ([IDИгры])
        REFERENCES [dbo].[Игры] ([IDИгры]),
    CONSTRAINT [CHK_Tournaments_MaxTeams] CHECK ([МаксУчастников] > 1),
    CONSTRAINT [CHK_Tournaments_Dates] CHECK ([ДатаОкончания] IS NULL OR [ДатаОкончания] >= [ДатаНачала]),
    CONSTRAINT [CHK_Tournaments_FormatType] CHECK ([ТипФормата] IN (N'Single Elimination', N'Double Elimination', N'League')),
    CONSTRAINT [CHK_Tournaments_ParticipantMode] CHECK ([ТипУчастников] IN (N'Команды', N'Игроки'))
);
GO

CREATE TABLE [dbo].[ЭтапыТурниров]
(
    [IDЭтапа] INT IDENTITY(1,1) NOT NULL,
    [IDТурнира] INT NOT NULL,
    [НазваниеЭтапа] NVARCHAR(100) NOT NULL,
    [ПорядокЭтапа] INT NOT NULL,
    [ТипСетки] NVARCHAR(20) NULL,
    CONSTRAINT [PK_TournamentStages] PRIMARY KEY CLUSTERED ([IDЭтапа] ASC),
    CONSTRAINT [FK_TournamentStages_Tournaments] FOREIGN KEY ([IDТурнира])
        REFERENCES [dbo].[Турниры] ([IDТурнира]),
    CONSTRAINT [UQ_TournamentStages_Tournament_StageOrder] UNIQUE ([IDТурнира], [ПорядокЭтапа]),
    CONSTRAINT [CHK_TournamentStages_BracketType] CHECK ([ТипСетки] IS NULL OR [ТипСетки] IN (N'Winner', N'Loser', N'Final'))
);
GO

CREATE TABLE [dbo].[СоставыКоманд]
(
    [IDСоставаКоманды] INT IDENTITY(1,1) NOT NULL,
    [IDКоманды] INT NOT NULL,
    [IDИгрока] INT NOT NULL,
    [ДатаПрисоединения] DATE NOT NULL,
    [ДатаУхода] DATE NULL,
    [Активен] BIT NOT NULL
        CONSTRAINT [DF_TeamPlayers_IsActive] DEFAULT ((1)),
    [Роль] NVARCHAR(50) NULL,
    CONSTRAINT [PK_TeamPlayers] PRIMARY KEY CLUSTERED ([IDСоставаКоманды] ASC),
    CONSTRAINT [FK_TeamPlayers_Teams] FOREIGN KEY ([IDКоманды])
        REFERENCES [dbo].[Команды] ([IDКоманды]),
    CONSTRAINT [FK_TeamPlayers_Players] FOREIGN KEY ([IDИгрока])
        REFERENCES [dbo].[Игроки] ([IDИгрока]),
    CONSTRAINT [CHK_TeamPlayers_LeaveDate] CHECK ([ДатаУхода] IS NULL OR [ДатаУхода] > [ДатаПрисоединения])
);
GO

CREATE UNIQUE INDEX [UX_TeamPlayers_ActivePair]
ON [dbo].[СоставыКоманд] ([IDКоманды], [IDИгрока])
WHERE [Активен] = 1;
GO

CREATE TABLE [dbo].[УчастникиТурниров]
(
    [IDУчастия] INT IDENTITY(1,1) NOT NULL,
    [IDТурнира] INT NOT NULL,
    [IDКоманды] INT NULL,
    [IDИгрока] INT NULL,
    [Посев] INT NULL,
    [ИтоговоеМесто] INT NULL,
    CONSTRAINT [PK_TournamentParticipants] PRIMARY KEY CLUSTERED ([IDУчастия] ASC),
    CONSTRAINT [FK_TournamentParticipants_Tournaments] FOREIGN KEY ([IDТурнира])
        REFERENCES [dbo].[Турниры] ([IDТурнира]),
    CONSTRAINT [FK_TournamentParticipants_Teams] FOREIGN KEY ([IDКоманды])
        REFERENCES [dbo].[Команды] ([IDКоманды]),
    CONSTRAINT [FK_TournamentParticipants_Players] FOREIGN KEY ([IDИгрока])
        REFERENCES [dbo].[Игроки] ([IDИгрока]),
    CONSTRAINT [CHK_TournamentParticipants_SingleEntity] CHECK
    (
        ([IDКоманды] IS NOT NULL AND [IDИгрока] IS NULL) OR
        ([IDКоманды] IS NULL AND [IDИгрока] IS NOT NULL)
    ),
    CONSTRAINT [CHK_TournamentParticipants_Seed] CHECK ([Посев] IS NULL OR [Посев] > 0),
    CONSTRAINT [CHK_TournamentParticipants_FinalPlace] CHECK ([ИтоговоеМесто] IS NULL OR [ИтоговоеМесто] > 0)
);
GO

CREATE UNIQUE INDEX [UX_TournamentParticipants_TournamentTeam]
ON [dbo].[УчастникиТурниров] ([IDТурнира], [IDКоманды])
WHERE [IDКоманды] IS NOT NULL;
GO

CREATE UNIQUE INDEX [UX_TournamentParticipants_TournamentPlayer]
ON [dbo].[УчастникиТурниров] ([IDТурнира], [IDИгрока])
WHERE [IDИгрока] IS NOT NULL;
GO

CREATE TABLE [dbo].[Матчи]
(
    [IDМатча] INT IDENTITY(1,1) NOT NULL,
    [IDТурнира] INT NOT NULL,
    [IDЭтапа] INT NOT NULL,
    [НомерМатча] INT NOT NULL,
    [IDКоманды1] INT NULL,
    [IDКоманды2] INT NULL,
    [IDПобедившейКоманды] INT NULL,
    [IDИгрока1] INT NULL,
    [IDИгрока2] INT NULL,
    [IDПобедившегоИгрока] INT NULL,
    [СчетКоманды1] INT NOT NULL
        CONSTRAINT [DF_Matches_Team1Score] DEFAULT ((0)),
    [СчетКоманды2] INT NOT NULL
        CONSTRAINT [DF_Matches_Team2Score] DEFAULT ((0)),
    [ДатаМатча] NVARCHAR(50) NULL,
    [ДоСколькихПобед] INT NOT NULL
        CONSTRAINT [DF_Matches_BestOf] DEFAULT ((3)),
    [Статус] NVARCHAR(20) NOT NULL
        CONSTRAINT [DF_Matches_Status] DEFAULT (N'Scheduled'),
    CONSTRAINT [PK_Matches] PRIMARY KEY CLUSTERED ([IDМатча] ASC),
    CONSTRAINT [FK_Matches_Tournaments] FOREIGN KEY ([IDТурнира])
        REFERENCES [dbo].[Турниры] ([IDТурнира]),
    CONSTRAINT [FK_Matches_TournamentStages] FOREIGN KEY ([IDЭтапа])
        REFERENCES [dbo].[ЭтапыТурниров] ([IDЭтапа]),
    CONSTRAINT [FK_Matches_Team1] FOREIGN KEY ([IDКоманды1])
        REFERENCES [dbo].[Команды] ([IDКоманды]),
    CONSTRAINT [FK_Matches_Team2] FOREIGN KEY ([IDКоманды2])
        REFERENCES [dbo].[Команды] ([IDКоманды]),
    CONSTRAINT [FK_Matches_WinnerTeam] FOREIGN KEY ([IDПобедившейКоманды])
        REFERENCES [dbo].[Команды] ([IDКоманды]),
    CONSTRAINT [FK_Matches_Player1] FOREIGN KEY ([IDИгрока1])
        REFERENCES [dbo].[Игроки] ([IDИгрока]),
    CONSTRAINT [FK_Matches_Player2] FOREIGN KEY ([IDИгрока2])
        REFERENCES [dbo].[Игроки] ([IDИгрока]),
    CONSTRAINT [FK_Matches_WinnerPlayer] FOREIGN KEY ([IDПобедившегоИгрока])
        REFERENCES [dbo].[Игроки] ([IDИгрока]),
    CONSTRAINT [UQ_Matches_Tournament_MatchNumber] UNIQUE ([IDТурнира], [НомерМатча]),
    CONSTRAINT [CHK_Matches_Status] CHECK ([Статус] IN (N'Scheduled', N'Completed')),
    CONSTRAINT [CHK_Matches_Teams] CHECK ([IDКоманды1] IS NULL OR [IDКоманды2] IS NULL OR [IDКоманды1] <> [IDКоманды2]),
    CONSTRAINT [CHK_Matches_Players] CHECK ([IDИгрока1] IS NULL OR [IDИгрока2] IS NULL OR [IDИгрока1] <> [IDИгрока2]),
    CONSTRAINT [CHK_Matches_Winner] CHECK ([IDПобедившейКоманды] IS NULL OR [IDПобедившейКоманды] = [IDКоманды1] OR [IDПобедившейКоманды] = [IDКоманды2]),
    CONSTRAINT [CHK_Matches_WinnerPlayer] CHECK ([IDПобедившегоИгрока] IS NULL OR [IDПобедившегоИгрока] = [IDИгрока1] OR [IDПобедившегоИгрока] = [IDИгрока2]),
    CONSTRAINT [CHK_Matches_ParticipantSource] CHECK
    (
        ([IDИгрока1] IS NULL AND [IDИгрока2] IS NULL AND [IDПобедившегоИгрока] IS NULL) OR
        ([IDКоманды1] IS NULL AND [IDКоманды2] IS NULL AND [IDПобедившейКоманды] IS NULL)
    ),
    CONSTRAINT [CHK_Matches_Score1] CHECK ([СчетКоманды1] >= 0),
    CONSTRAINT [CHK_Matches_Score2] CHECK ([СчетКоманды2] >= 0),
    CONSTRAINT [CHK_Matches_BestOf] CHECK ([ДоСколькихПобед] > 0 AND [ДоСколькихПобед] % 2 = 1)
);
GO

CREATE TABLE [dbo].[Трансляции]
(
    [IDТрансляции] INT IDENTITY(1,1) NOT NULL,
    [IDТурнира] INT NOT NULL,
    [IDМатча] INT NULL,
    [Платформа] NVARCHAR(50) NULL,
    [СсылкаТрансляции] NVARCHAR(500) NULL,
    CONSTRAINT [PK_Streams] PRIMARY KEY CLUSTERED ([IDТрансляции] ASC),
    CONSTRAINT [FK_Streams_Tournaments] FOREIGN KEY ([IDТурнира])
        REFERENCES [dbo].[Турниры] ([IDТурнира]),
    CONSTRAINT [FK_Streams_Matches] FOREIGN KEY ([IDМатча])
        REFERENCES [dbo].[Матчи] ([IDМатча])
);
GO

CREATE TABLE [dbo].[СпонсорыТурниров]
(
    [IDТурнира] INT NOT NULL,
    [IDСпонсора] INT NOT NULL,
    [СуммаСпонсорства] DECIMAL(15,2) NULL,
    [Валюта] NVARCHAR(10) NOT NULL
        CONSTRAINT [DF_TournamentSponsors_Currency] DEFAULT (N'USD'),
    CONSTRAINT [PK_TournamentSponsors] PRIMARY KEY CLUSTERED ([IDТурнира] ASC, [IDСпонсора] ASC),
    CONSTRAINT [FK_TournamentSponsors_Tournaments] FOREIGN KEY ([IDТурнира])
        REFERENCES [dbo].[Турниры] ([IDТурнира]),
    CONSTRAINT [FK_TournamentSponsors_Sponsors] FOREIGN KEY ([IDСпонсора])
        REFERENCES [dbo].[Спонсоры] ([IDСпонсора]),
    CONSTRAINT [CHK_TournamentSponsors_Amount] CHECK ([СуммаСпонсорства] IS NULL OR [СуммаСпонсорства] >= 0)
);
GO

INSERT INTO [dbo].[Организаторы] ([Логин], [Пароль])
VALUES (N'admin', N'password');

INSERT INTO [dbo].[Игры] ([НазваниеИгры], [Разработчик], [ГодВыпуска], [МаксИгроковВКоманде])
VALUES
    (N'Counter-Strike 2', N'Valve', 2023, 5),
    (N'Tiberium Wars', N'EA LA', 2007, 4),
    (N'Kane''s Wrath', N'EA LA', 2008, 4),
    (N'Red Alert 2', N'Westwood Studios, EA Pacific', 2000, 4),
    (N'Red Alert 3', N'EA LA', 2008, 4);

INSERT INTO [dbo].[Команды] ([НазваниеКоманды], [ДатаОснования], [Страна], [ИмяТренера])
VALUES
    (N'NAVI', '2009-12-17', N'Ukraine', N'B1ad3'),
    (N'Team Spirit', '2015-12-05', N'Russia', N'hally');

INSERT INTO [dbo].[Игроки] ([Никнейм], [НастоящееИмя], [Страна], [ДатаРождения])
VALUES
    (N's1mple', N'Oleksandr Kostyliev', N'Ukraine', '1997-10-02'),
    (N'donk', N'Danil Kryshkovets', N'Russia', '2007-01-25'),
    (N'DESolatorTrooper', N'Sergey Kornev', N'Russia', '2005-06-21'),
    (N'Bookuha', N'Andrey', N'Ukraine', '2000-01-01'),
    (N'Bikerushownz', N'Скрыто', N'United Kingdom', '2000-01-01'),
    (N'Hulk', N'Ivan', N'Russia', '2000-01-01'),
    (N'Mah_Boi', N'Скрыто', N'Blocked', '2000-01-01'),
    (N'Lamas', N'Скрыто', N'USA', '2026-04-01'),
    (N'Rildcom', N'Скрыто', N'Australia', '2000-01-01'),
    (N'Svenson', N'Скрыто', N'Nigerlands', '2000-01-01');

INSERT INTO [dbo].[Спонсоры] ([НазваниеСпонсора], [Индустрия])
VALUES (N'Red Bull', N'Energy Drinks');

INSERT INTO [dbo].[Турниры]
(
    [НазваниеТурнира],
    [IDИгры],
    [ДатаНачала],
    [ДатаОкончания],
    [ПризовойФонд],
    [Организатор],
    [МестоПроведения],
    [ТипФормата],
    [МаксУчастников],
    [ТипУчастников]
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
),
(
    N'WEC Season 1',
    3,
    '2026-02-01',
    NULL,
    1000.00,
    N'Bikerushownz',
    N'Online',
    N'League',
    8,
    N'Игроки'
),
(
    N'WEC Season 2',
    3,
    '2026-03-01',
    NULL,
    1000.00,
    N'Bikerushownz',
    N'Online',
    N'League',
    24,
    N'Игроки'
),
(
    N'Red Champions',
    5,
    '2024-07-05',
    '2024-08-16',
    1.00,
    N'MoscowCypersports',
    N'Online',
    N'Single Elimination',
    24,
    N'Игроки'
);

INSERT INTO [dbo].[ЭтапыТурниров] ([IDТурнира], [НазваниеЭтапа], [ПорядокЭтапа], [ТипСетки])
VALUES (1, N'Playoffs', 1, N'Winner');

INSERT INTO [dbo].[СоставыКоманд] ([IDКоманды], [IDИгрока], [ДатаПрисоединения], [Активен], [Роль])
VALUES
    (1, 1, '2026-01-15', 1, N'AWPer'),
    (2, 2, '2026-01-20', 1, N'Star Player');

SET IDENTITY_INSERT [dbo].[УчастникиТурниров] ON;

INSERT INTO [dbo].[УчастникиТурниров] ([IDУчастия], [IDТурнира], [IDКоманды], [IDИгрока], [Посев], [ИтоговоеМесто])
VALUES
    (1, 1, 1, NULL, 1, NULL),
    (2, 1, 2, NULL, 2, NULL),
    (3, 2, NULL, 3, 1, NULL),
    (4, 2, NULL, 4, 2, NULL),
    (6, 2, NULL, 6, 3, NULL),
    (7, 2, NULL, 8, 4, NULL),
    (8, 2, NULL, 9, 5, NULL),
    (9, 2, NULL, 10, 6, NULL),
    (11, 3, NULL, 1, 1, NULL),
    (12, 3, NULL, 2, 2, NULL),
    (13, 3, NULL, 4, 3, NULL),
    (14, 4, NULL, 5, 1, NULL),
    (15, 4, NULL, 3, 2, NULL);

SET IDENTITY_INSERT [dbo].[УчастникиТурниров] OFF;

INSERT INTO [dbo].[Матчи]
(
    [IDТурнира],
    [IDЭтапа],
    [НомерМатча],
    [IDКоманды1],
    [IDКоманды2],
    [IDПобедившейКоманды],
    [СчетКоманды1],
    [СчетКоманды2],
    [ДатаМатча],
    [ДоСколькихПобед],
    [Статус]
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

INSERT INTO [dbo].[Трансляции] ([IDТурнира], [IDМатча], [Платформа], [СсылкаТрансляции])
VALUES (1, 1, N'Twitch', N'https://twitch.tv/tournamentsdemo');

INSERT INTO [dbo].[СпонсорыТурниров] ([IDТурнира], [IDСпонсора], [СуммаСпонсорства], [Валюта])
VALUES (1, 1, 50000.00, N'USD');
GO

CREATE VIEW [dbo].[Organizer]
AS
SELECT
    [Логин] AS [Login],
    [Пароль] AS [Password]
FROM [dbo].[Организаторы];
GO

CREATE VIEW [dbo].[GameTitles]
AS
SELECT
    [IDИгры] AS [GameID],
    [НазваниеИгры] AS [GameName],
    [Разработчик] AS [Developer],
    [ГодВыпуска] AS [ReleaseYear],
    [МаксИгроковВКоманде] AS [MaxPlayersPerTeam]
FROM [dbo].[Игры];
GO

CREATE VIEW [dbo].[Teams]
AS
SELECT
    [IDКоманды] AS [TeamID],
    [НазваниеКоманды] AS [TeamName],
    [ДатаОснования] AS [FoundedDate],
    [Страна] AS [Country],
    [ИмяТренера] AS [CoachName],
    [ДатаСоздания] AS [CreatedDate]
FROM [dbo].[Команды];
GO

CREATE VIEW [dbo].[Players]
AS
SELECT
    [IDИгрока] AS [PlayerID],
    [Никнейм] AS [Nickname],
    [НастоящееИмя] AS [RealName],
    [Страна] AS [Country],
    [ДатаРождения] AS [BirthDate],
    [Пароль] AS [Password]
FROM [dbo].[Игроки];
GO

CREATE VIEW [dbo].[Sponsors]
AS
SELECT
    [IDСпонсора] AS [SponsorID],
    [НазваниеСпонсора] AS [SponsorName],
    [Индустрия] AS [Industry]
FROM [dbo].[Спонсоры];
GO

CREATE VIEW [dbo].[Tournaments]
AS
SELECT
    [IDТурнира] AS [TournamentID],
    [НазваниеТурнира] AS [TournamentName],
    [IDИгры] AS [GameID],
    [ДатаНачала] AS [StartDate],
    [ДатаОкончания] AS [EndDate],
    [ПризовойФонд] AS [PrizePool],
    [Организатор] AS [Organizer],
    [МестоПроведения] AS [Location],
    [ТипФормата] AS [FormatType],
    [МаксУчастников] AS [MaxTeams],
    [ТипУчастников] AS [ParticipantMode]
FROM [dbo].[Турниры];
GO

CREATE VIEW [dbo].[TournamentStages]
AS
SELECT
    [IDЭтапа] AS [StageID],
    [IDТурнира] AS [TournamentID],
    [НазваниеЭтапа] AS [StageName],
    [ПорядокЭтапа] AS [StageOrder],
    [ТипСетки] AS [BracketType]
FROM [dbo].[ЭтапыТурниров];
GO

CREATE VIEW [dbo].[TeamPlayers]
AS
SELECT
    [IDСоставаКоманды] AS [TeamPlayerID],
    [IDКоманды] AS [TeamID],
    [IDИгрока] AS [PlayerID],
    [ДатаПрисоединения] AS [JoinDate],
    [ДатаУхода] AS [LeaveDate],
    [Активен] AS [IsActive],
    [Роль] AS [Role]
FROM [dbo].[СоставыКоманд];
GO

CREATE VIEW [dbo].[TournamentParticipants]
AS
SELECT
    [IDУчастия] AS [ParticipationID],
    [IDТурнира] AS [TournamentID],
    [IDКоманды] AS [TeamID],
    [IDИгрока] AS [PlayerID],
    [Посев] AS [Seed],
    [ИтоговоеМесто] AS [FinalPlace]
FROM [dbo].[УчастникиТурниров];
GO

CREATE VIEW [dbo].[Matches]
AS
SELECT
    [IDМатча] AS [MatchID],
    [IDТурнира] AS [TournamentID],
    [IDЭтапа] AS [StageID],
    [НомерМатча] AS [MatchNumber],
    [IDКоманды1] AS [Team1ID],
    [IDКоманды2] AS [Team2ID],
    [IDПобедившейКоманды] AS [WinnerTeamID],
    [IDИгрока1] AS [Player1ID],
    [IDИгрока2] AS [Player2ID],
    [IDПобедившегоИгрока] AS [WinnerPlayerID],
    [СчетКоманды1] AS [Team1Score],
    [СчетКоманды2] AS [Team2Score],
    [ДатаМатча] AS [MatchDate],
    [ДоСколькихПобед] AS [BestOf],
    [Статус] AS [Status]
FROM [dbo].[Матчи];
GO

CREATE VIEW [dbo].[Streams]
AS
SELECT
    [IDТрансляции] AS [StreamID],
    [IDТурнира] AS [TournamentID],
    [IDМатча] AS [MatchID],
    [Платформа] AS [Platform],
    [СсылкаТрансляции] AS [StreamURL]
FROM [dbo].[Трансляции];
GO

CREATE VIEW [dbo].[TournamentSponsors]
AS
SELECT
    [IDТурнира] AS [TournamentID],
    [IDСпонсора] AS [SponsorID],
    [СуммаСпонсорства] AS [SponsorshipAmount],
    [Валюта] AS [Currency]
FROM [dbo].[СпонсорыТурниров];
GO
