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
using Microsoft.Extensions.Configuration;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Telegram.Bot.Types;

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
                (bool success, List<SearchResultModel>? searchResults) = await aniWordService.GetSeriesAsync(seriesName);
                return JsonConvert.SerializeObject(searchResults);
            }).WithOpenApi();

            app.MapGet("/getSeriesInfo", [Authorize] async (string seriesName) =>
            {
                SeriesInfoModel? seriesInfo = await aniWordService.GetSeriesInfoAsync(seriesName);

                return JsonConvert.SerializeObject(seriesInfo);
            }).WithOpenApi();

            app.MapPost("/verify", async ([FromBody] VerifyRequestModel verifyRequest) =>
            {
                if (verifyRequest is null || string.IsNullOrEmpty(verifyRequest.VerifyToken) || string.IsNullOrEmpty(verifyRequest.Username) || string.IsNullOrEmpty(verifyRequest.Password))
                    return Results.BadRequest();

                TokenValidationModel token = Helper.ValidateToken(verifyRequest.VerifyToken);

                if (!token.Validated)
                {
                    List<string> problems = new();

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

                UserModel? user = await DBService.GetUserByTelegramIdAsync(token.TelegramChatId!);

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

                string? jwt = authService.GenerateJSONWebToken();

                JwtResponseModel response = new(jwt!);

                return Results.Ok(response);
            });

            app.MapPost("/addReminder", [Authorize] async (AddReminderRequestModel addReminderRequest) =>
            {
                (bool success, List<SearchResultModel>? searchResults) = await aniWordService.GetSeriesAsync(addReminderRequest.SeriesName, strictSearch: true);

                if (!success || !searchResults.HasItems())
                    return Results.BadRequest();

                SeriesModel? series = await DBService.GetSeriesAsync(addReminderRequest.SeriesName);

                if (series is null)
                    await DBService.InsertSeries(addReminderRequest.SeriesName, aniWordService);

                UsersSeriesModel? usersSeries = await DBService.GetUsersSeriesAsync(addReminderRequest.Username, addReminderRequest.SeriesName);

                if (usersSeries is null)
                {
                    UserModel? user = await DBService.GetUserByUsernameAsync(addReminderRequest.Username);
                    series = await DBService.GetSeriesAsync(addReminderRequest.SeriesName);

                    usersSeries = new()
                    {
                        Users = user,
                        Series = series
                    };

                    await DBService.InsertUsersSeriesAsync(usersSeries);

                    return Results.Ok();
                }
                else
                {
                   return Results.BadRequest();
                }
            });

            app.Run();
        }
    }
}