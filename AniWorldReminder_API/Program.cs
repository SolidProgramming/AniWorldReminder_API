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

namespace AniWorldReminder_API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            WebApplicationBuilder? builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddAuthorization();

            AppSettingsModel? appSettings = SettingsHelper.ReadSettings<AppSettingsModel>();

            if (appSettings is not null && appSettings.AddSwagger)
            {
                // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();
            }

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
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();

            app.MapGet("getSeriesInfo", async (string seriesName) =>
            {
                (bool success, List<SearchResultModel>? searchResults) = await aniWordService.GetSeriesAsync(seriesName);
                return JsonConvert.SerializeObject(searchResults);
            }).WithOpenApi();

            app.MapGet("verify", async (string telegramChatid, string verifyToken) =>
            {
                UserModel? user = await DBService.GetUserAsync(telegramChatid);

                if (user is null)
                    return Results.NotFound("User not found!");

                if (user.Verified == VerificationStatus.Verified  && string.IsNullOrEmpty(user.VerifyToken))
                    return Results.BadRequest("You are already verified!");

                user.VerifyToken = verifyToken;
                TokenValidationModel token = Helper.ValidateToken(user);

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

                    await DBService.UpdateVerificationStatusAsync(telegramChatid, VerificationStatus.NotVerified);

                    return Results.ValidationProblem(problemsList);
                }

                await DBService.DeleteVerifyTokenAsync(telegramChatid);
                await DBService.UpdateVerificationStatusAsync(telegramChatid, VerificationStatus.Verified);

                StringBuilder sb = new();

                sb.AppendLine($"{Emoji.Confetti} <b>Dein Account wurde erfolgreich verifiziert.</b> {Emoji.Confetti}\n");
                sb.AppendLine($"{Emoji.Checkmark} Du kannst dich ab jetzt auf der Webseite einloggen und deine Reminder verwalten");

                await telegramBotService.SendMessageAsync(long.Parse(telegramChatid), sb.ToString());

                return Results.Ok("Your Account is now verified.");

            }).WithOpenApi();

            app.Run();
        }
    }
}