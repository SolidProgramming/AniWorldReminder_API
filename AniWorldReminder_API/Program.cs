global using AniWorldReminder_API.Models;
global using AniWorldReminder_API.Classes;
global using AniWorldReminder_API.Interfaces;
global using AniWorldReminder_API.Enums;
global using AniWorldReminder_API.Misc;
global using AniWorldReminder_API.Factories;
global using AniWorldReminder_API.Services;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using Org.BouncyCastle.Tls;

namespace AniWorldReminder_API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            WebApplicationBuilder? builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDistributedMemoryCache();

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
                // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();
            }
            builder.Services.AddSingleton<IAuthService, AuthService>();
            builder.Services.AddSingleton<IDBService, DBService>();
            builder.Services.AddSingleton<Interfaces.IHttpClientFactory, HttpClientFactory>();
            builder.Services.AddSingleton<ITelegramBotService, TelegramBotService>();
            builder.Services.AddSingleton<ITMDBService, TMDBService>();

            builder.Services.AddSingleton<IStreamingPortalServiceFactory>(_ =>
            {
                StreamingPortalServiceFactory streamingPortalServiceFactory = new();
                streamingPortalServiceFactory.AddService(StreamingPortal.AniWorld, _);
                streamingPortalServiceFactory.AddService(StreamingPortal.STO, _);
                streamingPortalServiceFactory.AddService(StreamingPortal.MegaKino, _);

                return streamingPortalServiceFactory;
            });

            WebApplication? app = builder.Build();

            IAuthService authService = app.Services.GetRequiredService<IAuthService>();

            IDBService dbService = app.Services.GetRequiredService<IDBService>();
            if (!await dbService.InitAsync())
                return;

            ITelegramBotService? telegramBotService = app.Services.GetRequiredService<ITelegramBotService>();
            if (!await telegramBotService.Init())
                return;

            Interfaces.IHttpClientFactory? httpClientFactory = app.Services.GetRequiredService<Interfaces.IHttpClientFactory>();
            HttpClient? noProxyClient = httpClientFactory.CreateHttpClient<Program>();

            (bool successNoProxy, string? ipv4NoProxy) = await noProxyClient.GetIPv4();
            if (!successNoProxy)
            {
                app.Logger.LogError($"{DateTime.Now} | HttpClient could not retrieve WAN IP Address. Shutting down...");
                return;
            }

            app.Logger.LogInformation($"{DateTime.Now} | Your WAN IP: {ipv4NoProxy}");

            ProxyAccountModel? proxyAccount = SettingsHelper.ReadSettings<ProxyAccountModel>();
            WebProxy? proxy = null;

            if (proxyAccount is not null)
                proxy = ProxyFactory.CreateProxy(proxyAccount);

            IStreamingPortalServiceFactory streamingPortalServiceFactory = app.Services.GetRequiredService<IStreamingPortalServiceFactory>();

            IStreamingPortalService aniWorldService = streamingPortalServiceFactory.GetService(StreamingPortal.AniWorld);
            IStreamingPortalService sTOService = streamingPortalServiceFactory.GetService(StreamingPortal.STO);
            IStreamingPortalService megaKinoService = streamingPortalServiceFactory.GetService(StreamingPortal.MegaKino);

            if (!await aniWorldService.InitAsync(proxy))
                return;

            if (!await sTOService.InitAsync(proxy))
                return;

            if (!await megaKinoService.InitAsync(proxy))
                return;

            if (appSettings is not null && appSettings.AddSwagger)
            {
                app.UseSwagger();
                app.UseSwaggerUI(_ => _.EnableTryItOutByDefault());
            }

            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapGet("/getSeries", [AllowAnonymous] async (string seriesName, MediaType mediaType) =>
            {
                List<SearchResultModel> allSearchResults = [];

                if (mediaType.HasFlag(MediaType.Films))
                {
                   List<SearchResultModel>? searchResultsMegaKino = await megaKinoService.GetMediaAsync(seriesName);

                    if (searchResultsMegaKino.HasItems())
                        allSearchResults.AddRange(searchResultsMegaKino!);
                }

                if (mediaType.HasFlag(MediaType.Series))
                {
                    List<Task<List<SearchResultModel>?>> tasks = [];

                    tasks.Add(aniWorldService.GetMediaAsync(seriesName));
                    tasks.Add(sTOService.GetMediaAsync(seriesName));

                    List<SearchResultModel>?[] taskResults = await Task.WhenAll(tasks);

                    foreach (List<SearchResultModel>? searchResult in taskResults)
                    {
                        if (searchResult.HasItems())
                            allSearchResults.AddRange(searchResult!);
                    }
                }

                return Results.Ok(allSearchResults);

            }).WithOpenApi();

            app.MapGet("/getSeriesInfo", [AllowAnonymous] async (IDistributedCache cache, string seriesPath, string hoster) =>
            {
                StreamingPortal streamingPortal = StreamingPortalHelper.GetStreamingPortalByName(hoster);

                SeriesInfoModel? seriesInfo = default;

                string cachePath = $"{seriesPath}@{hoster}";
                var cachedSeriesInfo = await cache.GetAsync(cachePath);

                if (cachedSeriesInfo is not null)
                    return Results.Ok(JsonSerializer.Deserialize<SeriesInfoModel>(cachedSeriesInfo));

                switch (streamingPortal)
                {
                    case StreamingPortal.AniWorld:
                        seriesInfo = await aniWorldService.GetMediaInfoAsync(seriesPath);
                        break;
                    case StreamingPortal.STO:
                        seriesInfo = await sTOService.GetMediaInfoAsync(seriesPath);
                        break;
                    case StreamingPortal.MegaKino:
                        seriesInfo = await megaKinoService.GetMediaInfoAsync(seriesPath, getMovieCoverArtUrl: true);
                        break;

                    case StreamingPortal.Undefined:
                    default:
                        return Results.BadRequest();
                }

                await cache.SetAsync(cachePath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(seriesInfo)), new DistributedCacheEntryOptions()
                {
                    SlidingExpiration = TimeSpan.FromMinutes(180)
                });

                return Results.Ok(seriesInfo);

            }).WithOpenApi();

            app.MapGet("/getPopular", [AllowAnonymous] async (IDistributedCache cache) =>
            {
                string cachePath = "popularAtHosters";

                var cachedPopularSeries = await cache.GetAsync(cachePath);

                if (cachedPopularSeries is not null)
                    return Results.Ok(JsonSerializer.Deserialize<List<SearchResultModel>?>(cachedPopularSeries));

                List<Task<List<SearchResultModel>?>> tasks = [];

                tasks.Add(aniWorldService.GetPopularAsync());
                tasks.Add(sTOService.GetPopularAsync());

                List<SearchResultModel>?[] taskResults = await Task.WhenAll(tasks);
                List<SearchResultModel> allPopularSeries = [];

                foreach (List<SearchResultModel>? popularSeries in taskResults)
                {
                    if (popularSeries.HasItems())
                        allPopularSeries.AddRange(popularSeries!);
                }

                await cache.SetAsync(cachePath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(allPopularSeries)), new DistributedCacheEntryOptions()
                {
                    SlidingExpiration = TimeSpan.FromHours(12)
                });

                allPopularSeries.Shuffle();

                return Results.Ok(allPopularSeries);
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

                    await dbService.UpdateVerificationStatusAsync(token.TelegramChatId, VerificationStatus.NotVerified);

                    return Results.ValidationProblem(problemsList);
                }

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

            }).WithOpenApi();

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
            }).WithOpenApi();

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
            }).WithOpenApi();

            app.MapPost("/addReminder", [Authorize] async (HttpContext httpContext, AddReminderRequestModel addReminderRequest) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

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
            }).WithOpenApi();

            app.MapGet("/removeReminder", [Authorize] async (HttpContext httpContext, string seriesPath) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                UsersSeriesModel? usersSeries = await dbService.GetUsersSeriesAsync(userId, seriesPath);

                if (usersSeries is null)
                    return Results.BadRequest();

                await dbService.DeleteUsersSeriesAsync(usersSeries);

                string messageText = $"{Emoji.Checkmark} Reminder f&uuml;r <b>{usersSeries.Series.Name}</b> wurde gel&ouml;scht.";
                await telegramBotService.SendMessageAsync(long.Parse(usersSeries.Users.TelegramChatId), messageText);

                return Results.Ok();
            }).WithOpenApi();

            app.MapGet("/getAllUserSeries", [Authorize] async (HttpContext httpContext) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                List<UsersSeriesModel>? usersSeries = await dbService.GetUsersSeriesAsync(userId);

                return Results.Ok(usersSeries?.Select(_ => _.Series));
            }).WithOpenApi();

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
            }).WithOpenApi();

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
            }).WithOpenApi();

            app.MapGet("/getSeasonEpisodesLinks", [AllowAnonymous] async (string seriesPath, string streamingPortal, [FromBody] SeasonModel seasonRequest, IDistributedCache cache) =>
            {
                string cachePath = $"{seriesPath}@{streamingPortal}:{seasonRequest.Id}";
                var cachedEpisodeLinks = await cache.GetAsync(cachePath);

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

            }).WithOpenApi();

            app.MapGet("/getDownloads", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                IEnumerable<EpisodeDownloadModel>? downloads = await dbService.GetDownloads(apiKey);

                return Results.Ok(downloads);
            }).WithOpenApi();

            app.MapPost("/removeFinishedDownload", [AllowAnonymous] async (HttpContext httpContext, [FromBody] EpisodeDownloadModel episode) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                await dbService.RemoveFinishedDownload(apiKey, episode);

                return Results.Ok();
            }).WithOpenApi();

            app.MapPost("/addDownloads", [Authorize] async (HttpContext httpContext, [FromBody] AddDownloadsRequestModel downloads) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                if (downloads is null || string.IsNullOrEmpty(downloads.SeriesId) || downloads.Episodes is null)
                    return Results.BadRequest();

                int episdesAdded = await dbService.InsertDownloadAsync(userId, downloads.SeriesId, downloads.Episodes);

                return Results.Ok(episdesAdded);
            }).WithOpenApi();

            app.MapGet("/getAPIKey", [Authorize] async (HttpContext httpContext) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                string? apiKey = await authService.GetAPIKey(userId);

                return Results.Ok(apiKey);
            }).WithOpenApi();

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
            }).WithOpenApi();

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
            }).WithOpenApi();

            app.MapPost("/setDownloaderPreferences", [AllowAnonymous] async (HttpContext httpContext, [FromBody] DownloaderPreferencesModel downloaderPreferences) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                await dbService.SetDownloaderPreferences(apiKey, downloaderPreferences);

                return Results.Ok();
            }).WithOpenApi();

            app.MapGet("/getDownloaderPreferences", [AllowAnonymous] async (HttpContext httpContext) =>
            {
                string? apiKey = httpContext.Request.Headers["X-API-KEY"];

                if (string.IsNullOrEmpty(apiKey))
                    return Results.Unauthorized();

                DownloaderPreferencesModel? downloaderPreferences = await dbService.GetDownloaderPreferences(apiKey);

                return Results.Ok(downloaderPreferences);
            }).WithOpenApi();

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
            }).WithOpenApi();

            app.Run();
        }
    }
}
