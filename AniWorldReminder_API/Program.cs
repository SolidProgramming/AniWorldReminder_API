global using AniWorldReminder_API.Classes;
global using AniWorldReminder_API.Enums;
global using AniWorldReminder_API.Factories;
global using AniWorldReminder_API.Interfaces;
global using AniWorldReminder_API.Models;
global using AniWorldReminder_API.Services;
global using AniWorldReminder_API.Misc;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AniWorldReminder_API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            WebApplicationBuilder? builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddHangfire(configuration => configuration
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseMemoryStorage());
            builder.Services.AddHangfireServer();
            builder.Services.AddTransient<AdminHttpFailureNotificationHandler>();
            builder.Services.ConfigureHttpClientDefaults(httpClientBuilder =>
            {
                httpClientBuilder.ConfigureHttpClient(client =>
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
                });
                httpClientBuilder.AddHttpMessageHandler<AdminHttpFailureNotificationHandler>();

                httpClientBuilder.AddResilienceHandler("default", (pipeline, context) =>
                {
                    ILoggerFactory loggerFactory = context.ServiceProvider.GetRequiredService<ILoggerFactory>();

                    HttpResiliencePipelineConfigurator.Configure(pipeline, loggerFactory, context.BuilderName);
                });
            });

            JwtSettingsModel? jwtSettings = SettingsHelper.ReadSettings<JwtSettingsModel>();

            if (jwtSettings is null || string.IsNullOrEmpty(jwtSettings.Key) || string.IsNullOrEmpty(jwtSettings.Issuer))
                return;

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtSettings.Issuer,
                        ValidAudience = jwtSettings.Issuer,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
                    };
                    options.SaveToken = true;
                });

            builder.Services.AddAuthorization();

            AppSettingsModel? appSettings = SettingsHelper.ReadSettings<AppSettingsModel>();

            if (appSettings is not null && appSettings.AddSwagger)
            {
                builder.Services.AddOpenApi();
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen(options =>
                {
                    options.SwaggerDoc("v1", new OpenApiInfo
                    {
                        Title = "AniWorldReminder API",
                        Version = "v1"
                    });

                    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                    {
                        Name = "Authorization",
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        In = ParameterLocation.Header,
                        Description = "JWT als 'Bearer {token}' eintragen."
                    });

                    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                    {
                        Name = "X-API-KEY",
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Header,
                        Description = "API-Key fuer Downloader- und Bridge-Endpunkte."
                    });

                    options.OperationFilter<SwaggerAuthOperationFilter>();
                });
            }
            builder.Services.AddSingleton<IAuthService, AuthService>();
            builder.Services.AddSingleton<IDBService, DBService>();
            builder.Services.AddSingleton<IDelayExecutor, TaskDelayExecutor>();
            builder.Services.AddSingleton<IEpisodeReminderDelayService, EpisodeReminderDelayService>();
            builder.Services.AddSingleton<Interfaces.IHttpClientFactory, HttpClientFactory>();
            builder.Services.AddSingleton<ITelegramBotService, TelegramBotService>();
            builder.Services.AddSingleton<ITMDBService, TMDBService>();
            builder.Services.AddSingleton<ICacheHelperService, CacheHelperService>();
            builder.Services.AddTransient<UpsertEpisodeInfoJob>();

            builder.Services.AddSingleton<IStreamingPortalServiceFactory>(_ =>
            {
                StreamingPortalServiceFactory streamingPortalServiceFactory = new();
                streamingPortalServiceFactory.AddService(StreamingPortal.AniWorld, _);
                streamingPortalServiceFactory.AddService(StreamingPortal.STO, _);

                return streamingPortalServiceFactory;
            });

            WebApplication? app = builder.Build();
            MethodTimeLogger.Configure(app.Services.GetRequiredService<ILoggerFactory>());

            IAuthService authService = app.Services.GetRequiredService<IAuthService>();

            IDBService dbService = app.Services.GetRequiredService<IDBService>();
            ITMDBService tmdbService = app.Services.GetRequiredService<ITMDBService>();
            if (!await dbService.InitAsync())
                return;

            ITelegramBotService? telegramBotService = app.Services.GetRequiredService<ITelegramBotService>();
            if (!await telegramBotService.Init())
                return;

            Interfaces.IHttpClientFactory? httpClientFactory = app.Services.GetRequiredService<Interfaces.IHttpClientFactory>();
            HttpClient? noProxyClient = httpClientFactory.CreateHttpClient<Program>();

            try
            {
                (bool successNoProxy, string? ipv4NoProxy) = await noProxyClient.GetIPv4();
                if (!successNoProxy)
                {
                    app.Logger.LogWarning("{Timestamp} | HttpClient could not retrieve WAN IP Address. Continuing startup without it.", DateTime.Now);
                }
                else
                {
                    app.Logger.LogInformation("{Timestamp} | Your WAN IP: {WanIp}", DateTime.Now, ipv4NoProxy);
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "{Timestamp} | Retrieving WAN IP failed. Continuing startup.", DateTime.Now);
            }

            ProxyAccountModel? proxyAccount = SettingsHelper.ReadSettings<ProxyAccountModel>();
            WebProxy? proxy = null;

            if (proxyAccount is not null)
                proxy = ProxyFactory.CreateProxy(proxyAccount);

            IStreamingPortalServiceFactory streamingPortalServiceFactory = app.Services.GetRequiredService<IStreamingPortalServiceFactory>();

            IStreamingPortalService aniWorldService = streamingPortalServiceFactory.GetService(StreamingPortal.AniWorld);
            IStreamingPortalService sTOService = streamingPortalServiceFactory.GetService(StreamingPortal.STO);

            bool aniWorldAvailable = await TryInitializeStreamingPortalAsync(aniWorldService, proxy);
            bool stoAvailable = await TryInitializeStreamingPortalAsync(sTOService, proxy);

            if (!aniWorldAvailable && !stoAvailable)
            {
                app.Logger.LogCritical("{Timestamp} | No streaming portal could be initialized. Shutting down...", DateTime.Now);
                return;
            }

            await InitPopularSeriesCache();

            if (appSettings is not null && appSettings.AddSwagger)
            {
                app.MapOpenApi();
                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint("/swagger/v1/swagger.json", "AniWorldReminder API v1");
                    options.EnableTryItOutByDefault();
                });
            }

            if (appSettings?.EnableHangfireDashboard == true)
            {
                app.UseHangfireDashboard(appSettings.HangfireDashboardPath);
            }

            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();

            if (appSettings?.EnableEpisodeReminderJob != false)
            {
                IRecurringJobManager recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
                IBackgroundJobClient backgroundJobClient = app.Services.GetRequiredService<IBackgroundJobClient>();

                recurringJobManager.AddOrUpdate<UpsertEpisodeInfoJob>(
                    "episode-reminder-scan",
                    job => job.ExecuteAsync(),
                    appSettings?.EpisodeReminderCron,
                    new RecurringJobOptions
                    {
                        TimeZone = TimeZoneInfo.Local
                    });

                backgroundJobClient.Enqueue<UpsertEpisodeInfoJob>(job => job.ExecuteAsync());
            }

            async Task InitPopularSeriesCache()
            {
                app.Logger.LogInformation("{Timestamp} | Fetching cache data of popular series...", DateTime.Now);

                ICacheHelperService cacheHelperService = app.Services.GetRequiredService<ICacheHelperService>();
                List<SearchResultModel> allPopularSeries = [];

                await AddPortalResultsAsync(
                    allPopularSeries,
                    TryExecutePortalRequestIfAvailableAsync(aniWorldAvailable, aniWorldService, "popular cache warmup", () => aniWorldService.GetPopularAsync()),
                    TryExecutePortalRequestIfAvailableAsync(stoAvailable, sTOService, "popular cache warmup", () => sTOService.GetPopularAsync()));

                if (!allPopularSeries.HasItems())
                {
                    app.Logger.LogWarning("{Timestamp} | Popular series cache warmup returned no data from any portal.", DateTime.Now);
                    return;
                }

                allPopularSeries.Shuffle();

                await cacheHelperService.SetCacheAsync(Global.Cache.Path.PopularSeries, allPopularSeries, 12 * 60);
                app.Logger.LogInformation("{Timestamp} | Popular series cache warmup completed with {Count} items.", DateTime.Now, allPopularSeries.Count);
            }

            app.MapGet("/getSeries", [AllowAnonymous] async (string seriesName, MediaType mediaType) =>
            {
                List<SearchResultModel> allSearchResults = [];

                if (mediaType.HasFlag(MediaType.Series))
                {
                    await AddPortalResultsAsync(
                        allSearchResults,
                        TryExecutePortalRequestIfAvailableAsync(aniWorldAvailable, aniWorldService, $"search '{seriesName}'", () => aniWorldService.GetMediaAsync(seriesName)),
                        TryExecutePortalRequestIfAvailableAsync(stoAvailable, sTOService, $"search '{seriesName}'", () => sTOService.GetMediaAsync(seriesName)));
                }

                return Results.Ok(allSearchResults);

            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Search for series or films";
                operation.Description = "Searches across multiple streaming portals for series or films matching the given name and media type.";
                return Task.CompletedTask;
            });

            app.MapGet("/getSeriesInfo", [AllowAnonymous] async (ICacheHelperService cacheHelper, string seriesPath, string hoster) =>
            {
                StreamingPortal streamingPortal = StreamingPortalHelper.GetStreamingPortalByName(hoster);

                SeriesInfoModel? seriesInfo;

                string cachePath = $"{seriesPath}@{hoster}";
                SeriesInfoModel? cachedSeriesInfo = await cacheHelper.GetCacheAsync<SeriesInfoModel>(cachePath);

                if (cachedSeriesInfo is not null && cachedSeriesInfo.Seasons is { Count: > 0 } && !cachedSeriesInfo.Seasons.All(_ => _.Episodes is { Count: 0 }))
                    return Results.Ok(cachedSeriesInfo);

                switch (streamingPortal)
                {
                    case StreamingPortal.AniWorld:
                        if (!aniWorldAvailable)
                            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                        seriesInfo = await TryExecutePortalRequestAsync(aniWorldService, $"series info '{seriesPath}'", () => aniWorldService.GetMediaInfoAsync(seriesPath));
                        break;
                    case StreamingPortal.STO:
                        if (!stoAvailable)
                            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                        seriesInfo = await TryExecutePortalRequestAsync(sTOService, $"series info '{seriesPath}'", () => sTOService.GetMediaInfoAsync(seriesPath));
                        break;
                    case StreamingPortal.Undefined:
                    default:
                        return Results.BadRequest();
                }

                if (seriesInfo is null)
                    return Results.NotFound();

                if (seriesInfo.Seasons is { Count: > 0 } && !seriesInfo.Seasons.All(_ => _.Episodes is { Count: 0 }))
                {
                    await cacheHelper.SetCacheAsync(cachePath, seriesInfo, 180);
                }

                return Results.Ok(seriesInfo);

            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get detailed series information";
                operation.Description = "Retrieves detailed information about a series including seasons and episodes from the specified streaming portal. Results are cached for 180 minutes.";
                return Task.CompletedTask;
            });

            app.MapGet("/getPopular", [AllowAnonymous] async (ICacheHelperService cacheHelper) =>
            {
                List<SearchResultModel>? cachedPopularSeries = await cacheHelper.GetCacheAsync<List<SearchResultModel>>(Global.Cache.Path.PopularSeries);

                if (cachedPopularSeries is not null)
                    return Results.Ok(cachedPopularSeries);
                List<SearchResultModel> allPopularSeries = [];

                await AddPortalResultsAsync(
                    allPopularSeries,
                    TryExecutePortalRequestIfAvailableAsync(aniWorldAvailable, aniWorldService, "popular series request", () => aniWorldService.GetPopularAsync()),
                    TryExecutePortalRequestIfAvailableAsync(stoAvailable, sTOService, "popular series request", () => sTOService.GetPopularAsync()));

                if (!allPopularSeries.HasItems())
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                allPopularSeries.Shuffle();

                await cacheHelper.SetCacheAsync(Global.Cache.Path.PopularSeries, allPopularSeries, 12 * 60);

                return Results.Ok(allPopularSeries);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get popular series";
                operation.Description = "Returns a shuffled list of popular series from AniWorld and STO portals. Results are cached for 12 hours.";
                return Task.CompletedTask;
            });

            async Task<bool> TryInitializeStreamingPortalAsync(IStreamingPortalService streamingPortalService, WebProxy? configuredProxy)
            {
                try
                {
                    bool initialized = await streamingPortalService.InitAsync(configuredProxy);

                    if (!initialized)
                    {
                        app.Logger.LogWarning(
                            "{Timestamp} | {PortalName} service could not be initialized. Requests for this portal may be unavailable.",
                            DateTime.Now,
                            streamingPortalService.Name);
                    }

                    return initialized;
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(
                        ex,
                        "{Timestamp} | {PortalName} service initialization failed unexpectedly.",
                        DateTime.Now,
                        streamingPortalService.Name);

                    return false;
                }
            }

            async Task<T?> TryExecutePortalRequestAsync<T>(IStreamingPortalService streamingPortalService, string operationName, Func<Task<T?>> action)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(
                        ex,
                        "{Timestamp} | {PortalName} request failed during {OperationName}.",
                        DateTime.Now,
                        streamingPortalService.Name,
                        operationName);

                    return default;
                }
            }

            async Task<T?> TryExecutePortalRequestIfAvailableAsync<T>(bool portalAvailable, IStreamingPortalService streamingPortalService, string operationName, Func<Task<T?>> action)
            {
                if (!portalAvailable)
                {
                    app.Logger.LogDebug(
                        "{Timestamp} | Skipping {PortalName} request during {OperationName} because the portal is marked unavailable.",
                        DateTime.Now,
                        streamingPortalService.Name,
                        operationName);

                    return default;
                }

                return await TryExecutePortalRequestAsync(streamingPortalService, operationName, action);
            }

            async Task AddPortalResultsAsync(List<SearchResultModel> target, params Task<List<SearchResultModel>?>[] tasks)
            {
                if (tasks.Length == 0)
                    return;

                List<SearchResultModel>?[] results = await Task.WhenAll(tasks);

                foreach (List<SearchResultModel>? result in results)
                {
                    if (result.HasItems())
                        target.AddRange(result!);
                }
            }

            async Task<UserModel?> GetApiUserAsync(HttpContext httpContext)
            {
                string? apiKey = httpContext.Request.Query["apikey"].FirstOrDefault();

                if (string.IsNullOrWhiteSpace(apiKey))
                    apiKey = httpContext.Request.Headers["X-API-KEY"].FirstOrDefault();

                return string.IsNullOrWhiteSpace(apiKey)
                    ? null
                    : await dbService.GetUserByAPIKey(apiKey);
            }

            string NormalizeSeriesPath(string? path)
            {
                string normalizedPath = path?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(normalizedPath))
                    return string.Empty;

                return $"/{normalizedPath.TrimStart('/')}";
            }

            string NormalizeTitle(string? title)
            {
                if (string.IsNullOrWhiteSpace(title))
                    return string.Empty;

                return new string(title
                    .Trim()
                    .ToLowerInvariant()
                    .Where(char.IsLetterOrDigit)
                    .ToArray());
            }

            IStreamingPortalService? GetStreamingPortalService(StreamingPortal streamingPortal) => streamingPortal switch
            {
                StreamingPortal.AniWorld => aniWorldService,
                StreamingPortal.STO => sTOService,
                _ => null
            };

            async Task<List<SearchResultModel>> SearchSeriesAcrossPortalsAsync(string title, bool strictSearch)
            {
                List<SearchResultModel> allSearchResults = [];

                await AddPortalResultsAsync(
                    allSearchResults,
                    TryExecutePortalRequestIfAvailableAsync(aniWorldAvailable, aniWorldService, $"bridge search '{title}'", () => aniWorldService.GetMediaAsync(title, strictSearch)),
                    TryExecutePortalRequestIfAvailableAsync(stoAvailable, sTOService, $"bridge search '{title}'", () => sTOService.GetMediaAsync(title, strictSearch)));

                return allSearchResults;
            }

            SearchResultModel? PickBestSeriesMatch(IEnumerable<SearchResultModel> results, string title)
            {
                string normalizedTitle = NormalizeTitle(title);

                SearchResultModel? exactMatch = results.FirstOrDefault(_ => NormalizeTitle(_.Name) == normalizedTitle);
                return exactMatch ?? results.FirstOrDefault();
            }

            async Task<SearchResultModel?> FindBestSeriesMatchAsync(string title)
            {
                List<SearchResultModel> strictResults = await SearchSeriesAcrossPortalsAsync(title, strictSearch: true);
                SearchResultModel? exactStrictMatch = PickBestSeriesMatch(strictResults, title);

                if (exactStrictMatch is not null)
                    return exactStrictMatch;

                List<SearchResultModel> relaxedResults = await SearchSeriesAcrossPortalsAsync(title, strictSearch: false);
                return PickBestSeriesMatch(relaxedResults, title);
            }

            async Task<SeriesModel?> EnsureSeriesAsync(SearchResultModel searchResult)
            {
                string normalizedSeriesPath = NormalizeSeriesPath(searchResult.Path);

                if (string.IsNullOrEmpty(normalizedSeriesPath))
                    return null;

                SeriesModel? existingSeries = await dbService.GetSeriesAsync(normalizedSeriesPath);

                if (existingSeries is not null)
                    return existingSeries;

                IStreamingPortalService? portalService = GetStreamingPortalService(searchResult.StreamingPortal);

                if (portalService is null)
                    return null;

                await dbService.InsertSeries(normalizedSeriesPath, portalService);

                return await dbService.GetSeriesAsync(normalizedSeriesPath);
            }

            Language SelectPreferredLanguage(Language availableLanguages)
            {
                if (availableLanguages == Language.EngDubGerSub)
                    return Language.GerSub;

                if (availableLanguages.HasFlag(Language.GerDub))
                    return Language.GerDub;

                if (availableLanguages.HasFlag(Language.GerSub))
                    return Language.GerSub;

                if (availableLanguages.HasFlag(Language.EngDub))
                    return Language.EngDub;

                if (availableLanguages.HasFlag(Language.EngSub))
                    return Language.EngSub;

                return availableLanguages == Language.None
                    ? Language.None
                    : availableLanguages.GetFlags(Language.None).FirstOrDefault();
            }

            SonarrSeriesLookupModel BuildSonarrSeriesResponse(
                string title,
                int tvdbId,
                IEnumerable<int> seasonNumbers,
                string overview = "",
                string seriesType = "standard",
                string? path = null,
                int? id = null,
                int? year = null)
            {
                List<int> distinctSeasonNumbers = seasonNumbers
                    .Where(_ => _ > 0)
                    .Distinct()
                    .OrderBy(_ => _)
                    .ToList();

                return new SonarrSeriesLookupModel
                {
                    Id = id,
                    Title = title,
                    SortTitle = title,
                    SeasonCount = distinctSeasonNumbers.Count,
                    Overview = overview,
                    Seasons = distinctSeasonNumbers
                        .Select(_ => new SonarrSeasonLookupModel
                        {
                            SeasonNumber = _,
                            Monitored = false,
                            Statistics = new SonarrSeasonStatisticsModel()
                        })
                        .ToList(),
                    Year = year ?? DateTime.UtcNow.Year,
                    Path = NormalizeSeriesPath(path),
                    ProfileId = 1,
                    LanguageProfileId = 1,
                    TvdbId = tvdbId,
                    SeriesType = seriesType,
                    CleanTitle = title.UrlSanitize().ToLowerInvariant(),
                    TitleSlug = title.UrlSanitize().ToLowerInvariant()
                };
            }

            async Task<SonarrSeriesLookupModel?> BuildLookupFromTvdbIdAsync(int tvdbId, string? fallbackTitle = null)
            {
                TMDBSearchTVByIdModel? tmdbSeries = await tmdbService.SearchTVShowByTvdbId(tvdbId);

                if (tmdbSeries is null)
                {
                    return string.IsNullOrWhiteSpace(fallbackTitle)
                        ? null
                        : BuildSonarrSeriesResponse(fallbackTitle, tvdbId, Enumerable.Range(1, 1));
                }

                IEnumerable<int> seasonNumbers = tmdbSeries.Seasons
                    .Where(_ => (_.SeasonNumber ?? 0) > 0)
                    .Select(_ => _.SeasonNumber ?? 0);

                int? year = null;

                if (DateTime.TryParse(tmdbSeries.FirstAirDate, out DateTime firstAirDate))
                    year = firstAirDate.Year;

                return BuildSonarrSeriesResponse(
                    tmdbSeries.Name ?? fallbackTitle ?? $"TVDB {tvdbId}",
                    tvdbId,
                    seasonNumbers,
                    tmdbSeries.Overview ?? string.Empty,
                    path: fallbackTitle,
                    year: year);
            }

            async Task<SonarrSeriesLookupModel?> BuildLookupFromSearchResultAsync(SearchResultModel searchResult)
            {
                IStreamingPortalService? portalService = GetStreamingPortalService(searchResult.StreamingPortal);

                if (portalService is null || string.IsNullOrWhiteSpace(searchResult.Path))
                    return null;

                SeriesInfoModel? seriesInfo = await portalService.GetMediaInfoAsync(searchResult.Path);

                if (seriesInfo is null || string.IsNullOrWhiteSpace(seriesInfo.Name))
                    return null;

                int tvdbId = seriesInfo.TMDBSearchTVById?.Id ?? 0;
                IEnumerable<int> seasonNumbers = seriesInfo.Seasons
                    .Select(_ => _.Id)
                    .Where(_ => _ > 0);

                int? year = null;

                if (DateTime.TryParse(seriesInfo.TMDBSearchTVById?.FirstAirDate, out DateTime firstAirDate))
                    year = firstAirDate.Year;

                return BuildSonarrSeriesResponse(
                    seriesInfo.Name,
                    tvdbId,
                    seasonNumbers,
                    seriesInfo.Description ?? string.Empty,
                    path: searchResult.Path,
                    year: year);
            }

            async Task<IResult> QueueSeriesRequestAsync(UserModel apiUser, SonarrAddSeriesRequestModel request)
            {
                string seriesTitle = request.Title?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(seriesTitle) && request.TvdbId > 0)
                {
                    TMDBSearchTVByIdModel? tmdbSeries = await tmdbService.SearchTVShowByTvdbId(request.TvdbId);
                    seriesTitle = tmdbSeries?.Name?.Trim() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(seriesTitle))
                    return Results.BadRequest("Series title could not be resolved.");

                SearchResultModel? searchResult = await FindBestSeriesMatchAsync(seriesTitle);

                if (searchResult is null)
                    return Results.NotFound($"No portal match found for '{seriesTitle}'.");

                SeriesModel? series = await EnsureSeriesAsync(searchResult);

                if (series is null || string.IsNullOrWhiteSpace(series.Path))
                    return Results.NotFound("Series could not be initialized in the local catalog.");

                UsersSeriesModel? existingUserSeries = await dbService.GetUsersSeriesAsync(apiUser.Id.ToString(), series.Path);

                if (existingUserSeries is null)
                {
                    await dbService.InsertUsersSeriesAsync(new UsersSeriesModel
                    {
                        Users = apiUser,
                        Series = series,
                        LanguageFlag = Language.GerDub
                    });
                }

                List<EpisodeModel> allEpisodes = await dbService.GetSeriesEpisodesAsync(series.Id) ?? [];
                HashSet<int> requestedSeasons = request.Seasons
                    .Where(_ => _.Monitored)
                    .Select(_ => _.SeasonNumber)
                    .ToHashSet();

                if (requestedSeasons.Count == 0)
                {
                    requestedSeasons = allEpisodes
                        .Select(_ => _.Season)
                        .Where(_ => _ > 0)
                        .ToHashSet();
                }

                List<EpisodeModel> episodesToQueue = allEpisodes
                    .Where(_ => requestedSeasons.Contains(_.Season))
                    .Select(_ => new EpisodeModel
                    {
                        Id = _.Id,
                        SeriesId = _.SeriesId,
                        Season = _.Season,
                        Episode = _.Episode,
                        Name = _.Name,
                        Languages = SelectPreferredLanguage(_.Languages)
                    })
                    .ToList();

                if (!episodesToQueue.HasItems())
                    return Results.NotFound("No episodes found for the requested seasons.");

                int queuedEpisodes = await dbService.InsertDownloadAsync(apiUser.Id.ToString(), series.Id.ToString(), episodesToQueue);

                SonarrSeriesLookupModel response = BuildSonarrSeriesResponse(
                    series.Name ?? seriesTitle,
                    request.TvdbId,
                    requestedSeasons,
                    path: series.Path,
                    id: series.Id,
                    seriesType: request.SeriesType);

                response.Monitored = queuedEpisodes > 0;

                return Results.Ok(response);
            }

            app.MapGet("/api/v3/system/status", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);
                return apiUser is null
                    ? Results.Unauthorized()
                    : Results.Ok(new SonarrSystemStatusModel());
            });

            app.MapGet("/api/v3/qualityProfile", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);
                return apiUser is null
                    ? Results.Unauthorized()
                    : Results.Ok(new List<SonarrQualityProfileModel>
                    {
                        new()
                        {
                            Id = 1,
                            Name = "AniWorld / STO"
                        }
                    });
            });

            app.MapGet("/api/v3/rootfolder", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);
                return apiUser is null
                    ? Results.Unauthorized()
                    : Results.Ok(new List<SonarrRootFolderModel>
                    {
                        new()
                        {
                            Id = 1,
                            Path = "/aniworld-bridge",
                            FreeSpace = 0,
                            TotalSpace = 0
                        }
                    });
            });

            app.MapGet("/api/v3/languageprofile", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);
                return apiUser is null
                    ? Results.Unauthorized()
                    : Results.Ok(new List<SonarrLanguageProfileModel>
                    {
                        new()
                        {
                            Id = 1,
                            Name = "Default"
                        }
                    });
            });

            app.MapGet("/api/v3/tag", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);
                return apiUser is null
                    ? Results.Unauthorized()
                    : Results.Ok(new List<SonarrTagModel>());
            });

            app.MapGet("/api/v3/series", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);
                return apiUser is null
                    ? Results.Unauthorized()
                    : Results.Ok(new List<SonarrSeriesLookupModel>());
            });

            app.MapGet("/api/v3/series/lookup", [AllowAnonymous] async (HttpContext httpContext, string term) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);

                if (apiUser is null)
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(term))
                    return Results.Ok(new List<SonarrSeriesLookupModel>());

                if (term.StartsWith("tvdb:", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(term["tvdb:".Length..], out int tvdbId))
                {
                    SonarrSeriesLookupModel? lookupByTvdb = await BuildLookupFromTvdbIdAsync(tvdbId);

                    return lookupByTvdb is null
                        ? Results.Ok(new List<SonarrSeriesLookupModel>())
                        : Results.Ok(new List<SonarrSeriesLookupModel> { lookupByTvdb });
                }

                List<SearchResultModel> searchResults = await SearchSeriesAcrossPortalsAsync(term, strictSearch: false);
                List<SonarrSeriesLookupModel> lookupResults = [];

                foreach (SearchResultModel searchResult in searchResults.Take(5))
                {
                    SonarrSeriesLookupModel? lookupResult = await BuildLookupFromSearchResultAsync(searchResult);

                    if (lookupResult is not null)
                        lookupResults.Add(lookupResult);
                }

                return Results.Ok(lookupResults);
            });

            app.MapPost("/api/v3/series", [AllowAnonymous] async (HttpContext httpContext, [FromBody] SonarrAddSeriesRequestModel request) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);

                if (apiUser is null)
                    return Results.Unauthorized();

                return await QueueSeriesRequestAsync(apiUser, request);
            });

            app.MapPut("/api/v3/series", [AllowAnonymous] async (HttpContext httpContext, [FromBody] SonarrAddSeriesRequestModel request) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);

                if (apiUser is null)
                    return Results.Unauthorized();

                return await QueueSeriesRequestAsync(apiUser, request);
            });

            app.MapGet("/api/v3/episode", [AllowAnonymous] async (HttpContext httpContext, int seriesId) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);

                if (apiUser is null)
                    return Results.Unauthorized();

                List<EpisodeModel> episodes = await dbService.GetSeriesEpisodesAsync(seriesId) ?? [];

                return Results.Ok(episodes.Select((episode, index) => new SonarrEpisodeModel
                {
                    Id = index + 1,
                    SeriesId = seriesId,
                    SeasonNumber = episode.Season,
                    EpisodeNumber = episode.Episode,
                    Title = episode.Name ?? string.Empty
                }));
            });

            app.MapPut("/api/v3/episode/monitor", [AllowAnonymous] async (HttpContext httpContext, [FromBody] SonarrEpisodeMonitorRequestModel request) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);
                return apiUser is null ? Results.Unauthorized() : Results.Ok(request);
            });

            app.MapPost("/api/v3/command", [AllowAnonymous] async (HttpContext httpContext, [FromBody] SonarrCommandRequestModel request) =>
            {
                UserModel? apiUser = await GetApiUserAsync(httpContext);
                return apiUser is null ? Results.Unauthorized() : Results.Ok(request);
            });

            app.MapPost("/verify", async ([FromBody] VerifyRequestModel verifyRequest) =>
            {
                if (verifyRequest is null || string.IsNullOrEmpty(verifyRequest.VerifyToken) || string.IsNullOrEmpty(verifyRequest.Username) || string.IsNullOrEmpty(verifyRequest.Password))
                    return Results.BadRequest();

                TokenValidationModel token = Helper.ValidateToken(verifyRequest.VerifyToken);

                if (!token.Validated)
                {
                    List<string> problems = [];

                    foreach (TokenValidationStatus tokenStatus in token.Errors)
                    {
                        problems.Add(tokenStatus.ToString());
                    }

                    Dictionary<string, string[]> problemsList = new()
                    {
                        { "Validation", problems.ToArray() }
                    };

                    if (!string.IsNullOrEmpty(token.TelegramChatId))
                    {
                        await dbService.UpdateVerificationStatusAsync(token.TelegramChatId, VerificationStatus.NotVerified);
                    }

                    return Results.ValidationProblem(problemsList);
                }

                if (string.IsNullOrEmpty(token.TelegramChatId))
                    return Results.BadRequest("Invalid Telegram chat id.");

                UserModel? user = await dbService.GetUserByTelegramIdAsync(token.TelegramChatId);

                if (user is null || string.IsNullOrEmpty(user.TelegramChatId))
                    return Results.NotFound("User not found!");

                if (user.Verified == VerificationStatus.Verified)
                    return Results.BadRequest("You are already verified!");

                user.Username = verifyRequest.Username;
                user.Password = SecretHasher.Hash(verifyRequest.Password);

                await dbService.DeleteVerifyTokenAsync(user.TelegramChatId);
                await dbService.SetVerifyStatusAsync(user);

                StringBuilder sb = new();

                sb.AppendLine($"{Emoji.Confetti} <b>Dein Account({verifyRequest.Username}) wurde erfolgreich verifiziert.</b> {Emoji.Confetti}\n");
                sb.AppendLine($"{Emoji.Checkmark} Du kannst dich ab jetzt auf der Webseite einloggen und deine Reminder verwalten.");

                await telegramBotService.SendMessageAsync(long.Parse(user.TelegramChatId), sb.ToString());

                return Results.Ok("Your Account is now verified.");

            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Verify user account";
                operation.Description = "Verifies a user account using a token received via Telegram. Sets up username and password for web login.";
                return Task.CompletedTask;
            });

            app.MapPost("/login", [AllowAnonymous] async (AuthUserModel authUser2) =>
            {
                if (authUser2 is null || string.IsNullOrEmpty(authUser2.Username) || string.IsNullOrEmpty(authUser2.Password))
                    return Results.BadRequest();

                UserModel? user = await authService.Authenticate(authUser2.Username, authUser2.Password);

                if (user == null)
                    return Results.Unauthorized();

                string? jwt = authService.GenerateJSONWebToken(user);

                AuthResponseModel response = new(jwt!);

                return Results.Ok(response);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "User login";
                operation.Description = "Authenticates a user with username and password, returning a JWT token for subsequent authorized requests.";
                return Task.CompletedTask;
            });

            app.MapGet("/getUserSeries", [Authorize] async (HttpContext httpContext, string seriesPath) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                UsersSeriesModel? usersSeries = await dbService.GetUsersSeriesAsync(userId, seriesPath);

                if (usersSeries is null || usersSeries.Series is null)
                    return Results.Ok(default);

                usersSeries.Series.LanguageFlag = usersSeries.LanguageFlag;

                return Results.Ok(usersSeries?.Series);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get user's series subscription";
                operation.Description = "Retrieves a specific series subscription for the authenticated user including language preferences.";
                return Task.CompletedTask;
            });

            app.MapPost("/addReminder", [Authorize] async (HttpContext httpContext, AddReminderRequestModel addReminderRequest) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                if (string.IsNullOrEmpty(addReminderRequest.SeriesPath))
                    return Results.BadRequest();

                SeriesModel? series = await dbService.GetSeriesAsync(addReminderRequest.SeriesPath);

                if (series is null)
                {
                    switch (addReminderRequest.StreamingPortal)
                    {
                        case StreamingPortal.Undefined:
                            return Results.BadRequest();
                        case StreamingPortal.AniWorld:
                            await dbService.InsertSeries(addReminderRequest.SeriesPath, aniWorldService);
                            break;
                        case StreamingPortal.STO:
                            await dbService.InsertSeries(addReminderRequest.SeriesPath, sTOService);
                            break;
                        default:
                            return Results.BadRequest();
                    }
                }

                UsersSeriesModel? usersSeries = await dbService.GetUsersSeriesAsync(userId, addReminderRequest.SeriesPath);

                if (usersSeries is null)
                {
                    UserModel? user = await dbService.GetAuthUserByIdAsync(userId);

                    if (user is null)
                        return Results.Unauthorized();

                    series = await dbService.GetSeriesAsync(addReminderRequest.SeriesPath);

                    usersSeries = new()
                    {
                        Users = user,
                        Series = series,
                        LanguageFlag = addReminderRequest.Language
                    };

                    await dbService.InsertUsersSeriesAsync(usersSeries);

                    if (series is null || string.IsNullOrEmpty(user.TelegramChatId))
                        return Results.Unauthorized();

                    string messageText = $"{Emoji.Checkmark} Dein Reminder f&uuml;r <b>{series.Name}</b> wurde hinzugef&uuml;gt.";

                    if (string.IsNullOrEmpty(series.CoverArtUrl))
                    {
                        await telegramBotService.SendMessageAsync(long.Parse(user.TelegramChatId), messageText);
                    }
                    else
                    {
                        await telegramBotService.SendPhotoAsync(long.Parse(user.TelegramChatId), series.CoverArtUrl, messageText);
                    }

                    return Results.Ok();
                }
                else
                {
                    return Results.BadRequest();
                }
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Add series reminder";
                operation.Description = "Creates a new reminder subscription for a series. Notifies the user via Telegram when new episodes are available.";
                return Task.CompletedTask;
            });

            app.MapGet("/removeReminder", [Authorize] async (HttpContext httpContext, string seriesPath) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                UsersSeriesModel? usersSeries = await dbService.GetUsersSeriesAsync(userId, seriesPath);

                if (usersSeries is null)
                    return Results.BadRequest();

                await dbService.DeleteUsersSeriesAsync(usersSeries);

                if (usersSeries.Series is null || usersSeries.Users is null || string.IsNullOrEmpty(usersSeries.Users.TelegramChatId))
                    return Results.BadRequest();

                string messageText = $"{Emoji.Checkmark} Reminder f&uuml;r <b>{usersSeries.Series.Name}</b> wurde gel&ouml;scht.";
                await telegramBotService.SendMessageAsync(long.Parse(usersSeries.Users.TelegramChatId), messageText);

                return Results.Ok();
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Remove series reminder";
                operation.Description = "Removes an existing reminder subscription for a series. Sends confirmation via Telegram.";
                return Task.CompletedTask;
            });

            app.MapGet("/getAllUserSeries", [Authorize] async (HttpContext httpContext) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                List<UsersSeriesModel>? usersSeries = await dbService.GetUsersSeriesAsync(userId);

                return Results.Ok(usersSeries?.Select(_ => _.Series));
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get all user series subscriptions";
                operation.Description = "Retrieves all series that the authenticated user has subscribed to for reminders.";
                return Task.CompletedTask;
            });

            app.MapGet("/getUserSettings", [Authorize] async (HttpContext httpContext) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                UserWebsiteSettings? userWebsiteSettings = await dbService.GetUserWebsiteSettings(userId);

                if (userWebsiteSettings is null)
                {
                    UserModel? user = await dbService.GetAuthUserByIdAsync(userId);

                    if (user is null)
                        return Results.Unauthorized();

                    await dbService.CreateUserWebsiteSettings(user.Id.ToString());
                    userWebsiteSettings = await dbService.GetUserWebsiteSettings(userId);
                }

                return Results.Ok(userWebsiteSettings);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get user website settings";
                operation.Description = "Retrieves the authenticated user's website preferences. Creates default settings if none exist.";
                return Task.CompletedTask;
            });

            app.MapPost("/setUserSettings", [Authorize] async (HttpContext httpContext, UserWebsiteSettings userWebsiteSettings) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                UserModel? user = await dbService.GetAuthUserByIdAsync(userId);

                if (user is null)
                    return Results.Unauthorized();

                await dbService.UpdateUserWebsiteSettings(userId, userWebsiteSettings);

                return Results.Ok();
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Update user website settings";
                operation.Description = "Updates the authenticated user's website preferences and settings.";
                return Task.CompletedTask;
            });

            app.MapGet("/getSeasonEpisodesLinks", [AllowAnonymous] async (string seriesPath, string streamingPortal, [FromBody] SeasonModel seasonRequest, IDistributedCache cache) =>
            {
                string cachePath = $"{seriesPath}@{streamingPortal}:{seasonRequest.Id}";
                byte[]? cachedEpisodeLinks = await cache.GetAsync(cachePath);

                if (cachedEpisodeLinks is not null)
                    return Results.Ok(JsonSerializer.Deserialize<SeasonModel>(cachedEpisodeLinks));

                SeasonModel? seasonData = default;

                if (streamingPortal.ToLower() == StreamingPortal.STO.ToString().ToLower())
                {
                    seasonData = await sTOService.GetSeasonEpisodesLinksAsync(seriesPath, seasonRequest);
                }
                else if (streamingPortal.ToLower() == StreamingPortal.AniWorld.ToString().ToLower())
                {
                    seasonData = await aniWorldService.GetSeasonEpisodesLinksAsync(seriesPath, seasonRequest);
                }

                await cache.SetAsync(cachePath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(seasonData)), new()
                {
                    SlidingExpiration = TimeSpan.FromMinutes(180)
                });

                return Results.Ok(seasonData);

            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get season episode links";
                operation.Description = "Retrieves download/streaming links for all episodes in a specific season. Results are cached with a 180-minute sliding expiration.";
                return Task.CompletedTask;
            });

            app.MapGet("/getDownloads", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                IEnumerable<EpisodeDownloadModel>? downloads = await dbService.GetDownloads(apiKey);

                return Results.Ok(downloads);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get pending downloads";
                operation.Description = "Retrieves all pending episode downloads for the user identified by the X-API-KEY header.";
                return Task.CompletedTask;
            });

            app.MapPost("/removeFinishedDownload", [AllowAnonymous] async (HttpContext httpContext, [FromBody] EpisodeDownloadModel episode) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                await dbService.RemoveFinishedDownload(apiKey, episode);

                return Results.Ok();
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Remove finished download";
                operation.Description = "Marks an episode download as complete and removes it from the pending downloads queue.";
                return Task.CompletedTask;
            });

            app.MapPost("/addDownloads", [Authorize] async (HttpContext httpContext, [FromBody] AddDownloadsRequestModel downloads) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                if (downloads is null || string.IsNullOrEmpty(downloads.SeriesId) || downloads.Episodes is null)
                    return Results.BadRequest();

                int episdesAdded = await dbService.InsertDownloadAsync(userId, downloads.SeriesId, downloads.Episodes);

                return Results.Ok(episdesAdded);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Add episodes to download queue";
                operation.Description = "Adds multiple episodes to the authenticated user's download queue. Returns the number of episodes successfully added.";
                return Task.CompletedTask;
            });

            app.MapGet("/getAPIKey", [Authorize] async (HttpContext httpContext) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                string? apiKey = await authService.GetAPIKey(userId);

                return Results.Ok(apiKey);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get user API key";
                operation.Description = "Retrieves the API key for the authenticated user, used for external client authentication.";
                return Task.CompletedTask;
            });

            app.MapGet("/getDownloadsCount", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                int downloadCount = await dbService.GetDownloadsCount(apiKey);

                DownloadCountModel downloadsCount = new()
                {
                    DownloadsCount = downloadCount
                };

                return Results.Ok(downloadsCount);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get pending downloads count";
                operation.Description = "Returns the total number of pending downloads for the user identified by the X-API-KEY header.";
                return Task.CompletedTask;
            });

            app.MapGet("/captchaNotify", [AllowAnonymous] async (HttpContext httpContext, string streamingPortal) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                UserModel? user = await dbService.GetUserByAPIKey(apiKey);

                if (user is null || string.IsNullOrEmpty(user.TelegramChatId))
                    return Results.NotFound();

                string messageText = $"{Emoji.ExclamationmarkRed}{Emoji.ExclamationmarkRed} Der AutoDLClient ist auf ein Captcha gelaufen{Emoji.ExclamationmarkRed}{Emoji.ExclamationmarkRed}\nDas Captcha muss gel&ouml;st werden damit der Downloader weiter machen kann{Emoji.ExclamationmarkRed}\nNavigiere auf dem PC mit dem Browser auf {streamingPortal} um das Captcha zu l&ouml;sen.";
                await telegramBotService.SendMessageAsync(long.Parse(user.TelegramChatId), messageText);

                return Results.Ok();
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Send captcha notification";
                operation.Description = "Sends a Telegram notification to the user when the auto-download client encounters a captcha that requires manual solving.";
                return Task.CompletedTask;
            });

            app.MapPost("/setDownloaderPreferences", [AllowAnonymous] async (HttpContext httpContext, [FromBody] DownloaderPreferencesModel downloaderPreferences) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                await dbService.SetDownloaderPreferences(apiKey, downloaderPreferences);

                return Results.Ok();
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Set downloader preferences";
                operation.Description = "Updates the download client preferences for the user identified by the X-API-KEY header.";
                return Task.CompletedTask;
            });

            app.MapGet("/getDownloaderPreferences", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                DownloaderPreferencesModel? downloaderPreferences = await dbService.GetDownloaderPreferences(apiKey);

                return Results.Ok(downloaderPreferences);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get downloader preferences";
                operation.Description = "Retrieves the download client preferences for the user identified by the X-API-KEY header.";
                return Task.CompletedTask;
            });

            app.MapPost("/addMovieDownload", [Authorize] async (HttpContext httpContext, [FromBody] AddMovieDownloadRequestModel download) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                if (string.IsNullOrEmpty(download.DirectUrl))
                    return Results.BadRequest();

                download.UserId = userId;

                await dbService.InsertMovieDownloadAsync(download);

                return Results.Ok();
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Add movie to download queue";
                operation.Description = "Adds a movie to the authenticated user's download queue using a direct URL.";
                return Task.CompletedTask;
            });

            app.MapPost("/createWatchlist", [Authorize] async (HttpContext httpContext, string watchlistName, [FromBody] List<SeriesModel> watchlist) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                string? watchlistIdent = await dbService.CreateWatchlist(watchlistName, userId, watchlist);

                if (string.IsNullOrEmpty(watchlistIdent))
                    return Results.InternalServerError();

                return Results.Ok(watchlistIdent);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Create a new watchlist";
                operation.Description = "Creates a new named watchlist with the provided series for the authenticated user. Returns the watchlist identifier.";
                return Task.CompletedTask;
            });

            app.MapGet("/getUserWatchlists", [Authorize] async (HttpContext httpContext) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();


                List<WatchlistModel>? watchlists = await dbService.GetUserWatchlists(userId);

                if (watchlists is null)
                    return Results.InternalServerError();

                return Results.Ok(watchlists);
            }).AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Summary = "Get user watchlists";
                operation.Description = "Retrieves all watchlists created by the authenticated user.";
                return Task.CompletedTask;
            });

            app.Run();
        }
    }
}
