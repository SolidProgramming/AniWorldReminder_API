global using AniWorldReminder_API.Models;
global using AniWorldReminder_API.Classes;
global using AniWorldReminder_API.Interfaces;
global using AniWorldReminder_API.Enums;
global using AniWorldReminder_API.Misc;
global using AniWorldReminder_API.Factories;
global using AniWorldReminder_API.Services;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace AniWorldReminder_API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            WebApplicationBuilder? builder = WebApplication.CreateBuilder(args);

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

            builder.Services.AddSingleton<IStreamingPortalServiceFactory>(_ =>
            {
                StreamingPortalServiceFactory streamingPortalServiceFactory = new();
                streamingPortalServiceFactory.AddService(StreamingPortal.AniWorld, _);
                streamingPortalServiceFactory.AddService(StreamingPortal.STO, _);

                return streamingPortalServiceFactory;
            });

            WebApplication? app = builder.Build();

            IAuthService authService = app.Services.GetRequiredService<IAuthService>();

            IDBService DBService = app.Services.GetRequiredService<IDBService>();
            if (!await DBService.InitAsync())
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

            IStreamingPortalService aniWordService = streamingPortalServiceFactory.GetService(StreamingPortal.AniWorld);
            IStreamingPortalService sTOService = streamingPortalServiceFactory.GetService(StreamingPortal.STO);

            if (!await aniWordService.InitAsync(proxy))
                return;

            if (!await sTOService.InitAsync(proxy))
                return;

            if (appSettings is not null && appSettings.AddSwagger)
            {
                app.UseSwagger();
                app.UseSwaggerUI(_ => _.EnableTryItOutByDefault());
            }

            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapGet("/getSeries", [Authorize] async (string seriesName) =>
            {
                List<SearchResultModel> allSearchResults = [];

                (bool _, List<SearchResultModel>? searchResultsAniWorld) = await aniWordService.GetSeriesAsync(seriesName);
                (bool _, List<SearchResultModel>? searchResultsSTO) = await sTOService.GetSeriesAsync(seriesName);

                if (searchResultsAniWorld.HasItems())
                    allSearchResults.AddRange(searchResultsAniWorld);

                if (searchResultsSTO.HasItems())
                    allSearchResults.AddRange(searchResultsSTO);

                allSearchResults = allSearchResults.DistinctBy(_ => _.Title).ToList();

                return JsonConvert.SerializeObject(allSearchResults);
            }).WithOpenApi();

            app.MapGet("/getSeriesInfo", [Authorize] async (string seriesName) =>
            {
                SeriesInfoModel? seriesInfo = await aniWordService.GetSeriesInfoAsync(seriesName);

                if (seriesInfo is not null)
                    return JsonConvert.SerializeObject(seriesInfo);

                seriesInfo = await sTOService.GetSeriesInfoAsync(seriesName);

                return JsonConvert.SerializeObject(seriesInfo);
            }).WithOpenApi();

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

                    await DBService.UpdateVerificationStatusAsync(token.TelegramChatId, VerificationStatus.NotVerified);

                    return Results.ValidationProblem(problemsList);
                }

                UserModel? user = await DBService.GetUserByTelegramIdAsync(token.TelegramChatId);

                if (user is null || string.IsNullOrEmpty(user.TelegramChatId))
                    return Results.NotFound("User not found!");

                if (user.Verified == VerificationStatus.Verified)
                    return Results.BadRequest("You are already verified!");

                user.Username = verifyRequest.Username;
                user.Password = SecretHasher.Hash(verifyRequest.Password);

                await DBService.DeleteVerifyTokenAsync(user.TelegramChatId);
                await DBService.SetVerifyStatusAsync(user);

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

            app.MapGet("/getUserSeries", [Authorize] async (HttpContext httpContext, string seriesName) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                UsersSeriesModel? usersSeries = await DBService.GetUsersSeriesAsync(userId, seriesName);

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

                SeriesModel? series = await DBService.GetSeriesAsync(addReminderRequest.SeriesName);

                if (series is null)
                {
                    switch (addReminderRequest.StreamingPortal)
                    {
                        case StreamingPortal.Undefined:
                            return Results.BadRequest();
                        case StreamingPortal.AniWorld:
                            await DBService.InsertSeries(addReminderRequest.SeriesName, aniWordService);
                            break;
                        case StreamingPortal.STO:
                            await DBService.InsertSeries(addReminderRequest.SeriesName, sTOService);
                            break;
                        default:
                            return Results.BadRequest();
                    }
                }

                UsersSeriesModel? usersSeries = await DBService.GetUsersSeriesAsync(userId, addReminderRequest.SeriesName);

                if (usersSeries is null)
                {
                    string? username = httpContext.GetClaim(CustomClaimType.Username);

                    if (string.IsNullOrEmpty(username))
                        return Results.Unauthorized();

                    UserModel? user = await DBService.GetAuthUserAsync(username);

                    if (user is null)
                        return Results.BadRequest();

                    series = await DBService.GetSeriesAsync(addReminderRequest.SeriesName);

                    usersSeries = new()
                    {
                        Users = user,
                        Series = series,
                        LanguageFlag = addReminderRequest.Language
                    };

                    await DBService.InsertUsersSeriesAsync(usersSeries);

                    string messageText = $"{Emoji.Checkmark} Dein Reminder für <b>{series.Name}</b> wurde hinzugefügt.";

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

            app.MapGet("/removeReminder", [Authorize] async (HttpContext httpContext, string seriesName) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                UsersSeriesModel? usersSeries = await DBService.GetUsersSeriesAsync(userId, seriesName);

                if (usersSeries is null)
                    return Results.BadRequest();

                await DBService.DeleteUsersSeriesAsync(usersSeries);

                string messageText = $"{Emoji.Checkmark} Reminder für <b>{usersSeries.Series.Name}</b> wurde gelöscht.";
                await telegramBotService.SendMessageAsync(long.Parse(usersSeries.Users.TelegramChatId), messageText);

                return Results.Ok();
            }).WithOpenApi();

            app.MapGet("/getAllUserSeries", [Authorize] async (HttpContext httpContext) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                List<UsersSeriesModel>? usersSeries = await DBService.GetUsersSeriesAsync(userId);

                return Results.Ok(usersSeries?.Select(_ => _.Series));
            }).WithOpenApi();

            app.MapGet("/getUserSettings", [Authorize] async (HttpContext httpContext) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                UserWebsiteSettings? userWebsiteSettings = await DBService.GetUserWebsiteSettings(userId);

                if (userWebsiteSettings is null)
                {
                    UserModel? user = await DBService.GetAuthUserAsync(userId);

                    if (user is null)
                        return Results.Unauthorized();

                    await DBService.CreateUserWebsiteSettings(user.Id.ToString());
                    userWebsiteSettings = await DBService.GetUserWebsiteSettings(userId);
                }                    

                return Results.Ok(userWebsiteSettings);
            }).WithOpenApi();

            app.MapPost("/setUserSettings", [Authorize] async (HttpContext httpContext, UserWebsiteSettings userWebsiteSettings) =>
            {
                string? username = httpContext.GetClaim(CustomClaimType.Username);

                if (string.IsNullOrEmpty(username))
                    return Results.Unauthorized();

                UserModel? user = await DBService.GetAuthUserAsync(username);

                if (user is null)
                    return Results.Unauthorized();

                userWebsiteSettings.UserId = user.Id;

                await DBService.UpdateUserWebsiteSettings(userWebsiteSettings);

                return Results.Ok();
            }).WithOpenApi();

            app.MapGet("/getSeasonEpisodesLinks", [Authorize] async (string seriesName, string streamingPortal, [FromBody] SeasonModel season) =>
            {
                if (streamingPortal.ToLower() == StreamingPortal.STO.ToString().ToLower())
                {
                    return await sTOService.GetSeasonEpisodesLinksAsync(seriesName, season);
                }
                else if (streamingPortal.ToLower() == StreamingPortal.AniWorld.ToString().ToLower())
                {
                    return await aniWordService.GetSeasonEpisodesLinksAsync(seriesName, season);
                }

                return default;
            }).WithOpenApi();

            app.MapGet("/getDownloads", [Authorize] async (HttpContext httpContext) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                IEnumerable<EpisodeDownloadModel>? downloads = await DBService.GetDownloadEpisodes(userId);

                return Results.Ok(downloads);
            }).WithOpenApi();

            app.MapPost("/removeFinishedDownload", [Authorize] async (HttpContext httpContext, [FromBody] string downloadId) =>
            {
                string? userId = httpContext.GetClaim(CustomClaimType.UserId);

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();
                
                await DBService.RemoveFinishedDownload(userId, downloadId);

                return Results.Ok();

            }).WithOpenApi();

            app.Run();
        }
    }
}