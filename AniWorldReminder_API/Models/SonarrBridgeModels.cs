namespace AniWorldReminder_API.Models
{
    public class SonarrSystemStatusModel
    {
        public string Version { get; set; } = "3.0.0";
        public DateTime BuildTime { get; set; } = DateTime.UtcNow;
        public bool IsDebug { get; set; }
        public bool IsProduction { get; set; } = true;
        public bool IsAdmin { get; set; } = true;
        public bool IsUserInteractive { get; set; }
        public string StartupPath { get; set; } = AppContext.BaseDirectory;
        public string AppData { get; set; } = AppContext.BaseDirectory;
        public string OsName { get; set; } = Environment.OSVersion.Platform.ToString();
        public string OsVersion { get; set; } = Environment.OSVersion.VersionString;
        public bool IsNetCore { get; set; } = true;
        public bool IsMono { get; set; }
        public bool IsLinux { get; set; } = OperatingSystem.IsLinux();
        public bool IsOsx { get; set; } = OperatingSystem.IsMacOS();
        public bool IsWindows { get; set; } = OperatingSystem.IsWindows();
        public bool IsDocker { get; set; }
        public string Mode { get; set; } = "bridge";
        public string Branch { get; set; } = "main";
        public string Authentication { get; set; } = "external";
        public string SqliteVersion { get; set; } = string.Empty;
        public int MigrationVersion { get; set; }
        public string UrlBase { get; set; } = string.Empty;
        public string RuntimeVersion { get; set; } = Environment.Version.ToString();
        public string RuntimeName { get; set; } = ".NET";
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public string PackageUpdateMechanism { get; set; } = "manual";
    }

    public class SonarrQualityProfileModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SonarrRootFolderModel
    {
        public int Id { get; set; }
        public string Path { get; set; } = string.Empty;
        public long FreeSpace { get; set; }
        public long TotalSpace { get; set; }
        public List<SonarrUnmappedFolderModel> UnmappedFolders { get; set; } = [];
    }

    public class SonarrUnmappedFolderModel
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public class SonarrLanguageProfileModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SonarrTagModel
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    public class SonarrSeriesLookupModel
    {
        public int? Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SortTitle { get; set; } = string.Empty;
        public int SeasonCount { get; set; }
        public string Status { get; set; } = "continuing";
        public string Overview { get; set; } = string.Empty;
        public string Network { get; set; } = string.Empty;
        public string AirTime { get; set; } = string.Empty;
        public List<object> Images { get; set; } = [];
        public string RemotePoster { get; set; } = string.Empty;
        public List<SonarrSeasonLookupModel> Seasons { get; set; } = [];
        public int Year { get; set; }
        public string Path { get; set; } = string.Empty;
        public int ProfileId { get; set; }
        public int LanguageProfileId { get; set; }
        public bool SeasonFolder { get; set; } = true;
        public bool Monitored { get; set; } = true;
        public string MonitorNewItems { get; set; } = "all";
        public bool UseSceneNumbering { get; set; }
        public int Runtime { get; set; } = 24;
        public int TvdbId { get; set; }
        public int TvRageId { get; set; }
        public int TvMazeId { get; set; }
        public string FirstAired { get; set; } = string.Empty;
        public string SeriesType { get; set; } = "standard";
        public string CleanTitle { get; set; } = string.Empty;
        public string ImdbId { get; set; } = string.Empty;
        public string TitleSlug { get; set; } = string.Empty;
        public string Certification { get; set; } = string.Empty;
        public List<string> Genres { get; set; } = [];
        public List<int> Tags { get; set; } = [];
        public string Added { get; set; } = DateTime.UtcNow.ToString("O");
        public SonarrRatingsModel Ratings { get; set; } = new();
    }

    public class SonarrSeasonLookupModel
    {
        public int SeasonNumber { get; set; }
        public bool Monitored { get; set; }
        public SonarrSeasonStatisticsModel Statistics { get; set; } = new();
    }

    public class SonarrSeasonStatisticsModel
    {
        public int EpisodeFileCount { get; set; }
        public int EpisodeCount { get; set; }
        public int TotalEpisodeCount { get; set; }
        public long SizeOnDisk { get; set; }
        public decimal PercentOfEpisodes { get; set; }
    }

    public class SonarrRatingsModel
    {
        public int Votes { get; set; }
        public decimal Value { get; set; }
    }

    public class SonarrAddSeriesRequestModel
    {
        public int TvdbId { get; set; }
        public string? Title { get; set; }
        public int QualityProfileId { get; set; }
        public int LanguageProfileId { get; set; }
        public List<SonarrSeasonLookupModel> Seasons { get; set; } = [];
        public List<int> Tags { get; set; } = [];
        public bool SeasonFolder { get; set; }
        public bool Monitored { get; set; }
        public string MonitorNewItems { get; set; } = "all";
        public string? RootFolderPath { get; set; }
        public string SeriesType { get; set; } = "standard";
        public SonarrAddOptionsModel? AddOptions { get; set; }
    }

    public class SonarrAddOptionsModel
    {
        public bool IgnoreEpisodesWithFiles { get; set; }
        public bool IgnoreEpisodesWithoutFiles { get; set; }
        public bool SearchForMissingEpisodes { get; set; }
    }

    public class SonarrEpisodeModel
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }
        public int EpisodeFileId { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string AirDate { get; set; } = string.Empty;
        public string AirDateUtc { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public bool HasFile { get; set; }
        public bool Monitored { get; set; } = true;
        public int AbsoluteEpisodeNumber { get; set; }
        public bool UnverifiedSceneNumbering { get; set; }
    }

    public class SonarrEpisodeMonitorRequestModel
    {
        public List<int> EpisodeIds { get; set; } = [];
        public bool Monitored { get; set; }
    }

    public class SonarrCommandRequestModel
    {
        public string Name { get; set; } = string.Empty;
        public int? SeriesId { get; set; }
    }
}
