global using AniWorldReminder_API.Models;
global using AniWorldReminder_API.Classes;
global using AniWorldReminder_API.Interfaces;
global using AniWorldReminder_API.Enums;
global using AniWorldReminder_API.Misc;
global using AniWorldReminder_API.Factories;
using Newtonsoft.Json;
using System.Net;

namespace AniWorldReminder_API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddAuthorization();

            AppSettingsModel? appSettings = SettingsHelper.ReadSettings<AppSettingsModel>();

            if (appSettings is not null && appSettings.AddSwagger)
            {
                // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();
            }

            builder.Services.AddSingleton<Interfaces.IHttpClientFactory, HttpClientFactory>();

            builder.Services.AddSingleton<IStreamingPortalServiceFactory>(_ =>
            {
                StreamingPortalServiceFactory streamingPortalServiceFactory = new();
                streamingPortalServiceFactory.AddService(StreamingPortal.AniWorld, _);
                streamingPortalServiceFactory.AddService(StreamingPortal.STO, _);

                return streamingPortalServiceFactory;
            });

            var app = builder.Build();

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

            if (!await aniWordService.Init(proxy))
                return;

            if (!await sTOService.Init(proxy))
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

            app.Run();
        }
    }
}