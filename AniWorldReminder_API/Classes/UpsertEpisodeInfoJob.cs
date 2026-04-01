using Hangfire;
using MethodTimer;
using Mysqlx.Prepare;
using System.Reflection;
using System.Text;

namespace AniWorldReminder_API.Classes
{
    [DisableConcurrentExecution(timeoutInSeconds: 15)]
    [AutomaticRetry(Attempts = 3)]
    public class UpsertEpisodeInfoJob(ILogger<UpsertEpisodeInfoJob> logger, IStreamingPortalServiceFactory streamingPortalServiceFactory, IDBService dbService, ITelegramBotService telegramBotService, IEpisodeReminderDelayService episodeReminderDelayService)
    {
        private readonly IStreamingPortalService aniWorldService = streamingPortalServiceFactory.GetService(StreamingPortal.AniWorld);
        private readonly IStreamingPortalService stoService = streamingPortalServiceFactory.GetService(StreamingPortal.STO);

        [Time]
        public async Task ExecuteAsync()
        {
            MethodBase? methodBase = typeof(UpsertEpisodeInfoJob).GetMethod(nameof(ExecuteAsync));

            if (methodBase is not null)
            {
                MethodTimeLogger.LogExecution(methodBase);
            }

            TelegramBotSettingsModel? botSettings = SettingsHelper.ReadSettings<TelegramBotSettingsModel>();
            AppSettingsModel? appSettings = SettingsHelper.ReadSettings<AppSettingsModel>();

            try
            {
                await CheckForNewEpisodesAsync(botSettings, appSettings);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Episode reminder job failed.");
                await SendAdminErrorAsync(botSettings, ex);
            }
        }

        private async Task CheckForNewEpisodesAsync(TelegramBotSettingsModel? botSettings, AppSettingsModel? appSettings)
        {
            List<SeriesReminderModel>? userReminderSeries = await dbService.GetUsersReminderSeriesAsync();

            if (!userReminderSeries.HasItems())
                return;

            IEnumerable<IGrouping<int, SeriesReminderModel>> userReminderSeriesGroups = userReminderSeries!
                .Where(_ => _.Series is not null && _.User is not null)
                .GroupBy(_ => _.Series!.Id);

            foreach (IGrouping<int, SeriesReminderModel> group in userReminderSeriesGroups)
            {
                SeriesReminderModel seriesReminder = group.First();

                if (seriesReminder.Series is null)
                    continue;

                try
                {
                    logger.LogInformation("{Timestamp} | Scanning for changes: {SeriesName}", DateTime.Now, seriesReminder.Series.Name);

                    (bool updateAvailable, SeriesInfoModel? seriesInfo, List<EpisodeModel>? languageUpdateEpisodes, List<EpisodeModel>? newEpisodes, List<EpisodeModel>? namesUpdatedEpisodes) =
                        await UpdateNeededAsync(seriesReminder);

                    if (!updateAvailable || seriesInfo is null)
                        continue;

                    List<EpisodeModel> changedEpisodes = [];

                    if (namesUpdatedEpisodes.HasItems())
                    {
                        await dbService.UpdateEpisodesAsync(seriesReminder.Series.Id, namesUpdatedEpisodes!);
                        await SendAdminNotificationAsync(
                            botSettings,
                            namesUpdatedEpisodes!,
                            $"Es wurden folgende Episoden fuer <b>{seriesReminder.Series.Name}</b> mit <b>Namens-Updates</b> gefunden und geupdated!");
                    }

                    if (languageUpdateEpisodes.HasItems())
                    {
                        await dbService.UpdateEpisodesAsync(seriesReminder.Series.Id, languageUpdateEpisodes!);
                        changedEpisodes.AddRange(languageUpdateEpisodes!);

                        await SendAdminNotificationAsync(
                            botSettings,
                            languageUpdateEpisodes!,
                            $"Es wurden folgende Episoden fuer <b>{seriesReminder.Series.Name}</b> mit <b>Sprach-Updates</b> gefunden und geupdated!");
                    }

                    if (newEpisodes.HasItems())
                    {
                        await dbService.InsertEpisodesAsync(seriesReminder.Series.Id, newEpisodes!);
                        await dbService.UpdateSeriesInfoAsync(seriesReminder.Series.Id, seriesInfo);
                        changedEpisodes.AddRange(newEpisodes!);

                        await SendAdminNotificationAsync(
                            botSettings,
                            newEpisodes!,
                            $"Es wurden neue Episoden fuer <b>{seriesReminder.Series.Name}</b> gefunden und hinzugefuegt!");
                    }

                    if (!changedEpisodes.HasItems())
                        continue;

                    logger.LogInformation("{Timestamp} | Changes found for: {SeriesName} | {Count}x", DateTime.Now, seriesReminder.Series.Name, changedEpisodes.Count);
                    await SendNotificationsAsync(botSettings, appSettings, seriesInfo, group, changedEpisodes);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Episode scan for series {SeriesName} failed.", seriesReminder.Series.Name);
                    await SendAdminErrorAsync(botSettings, ex);
                }
                finally
                {
                    await episodeReminderDelayService.DelayAfterSeriesScanAsync(appSettings);
                }
            }
        }

        private async Task SendNotificationsAsync(TelegramBotSettingsModel? botSettings, AppSettingsModel? appSettings, SeriesInfoModel seriesInfo, IGrouping<int, SeriesReminderModel> seriesGroup, List<EpisodeModel> changedEpisodes)
        {
            const int maxCount = 5;
            string? seriesName = seriesGroup.First().Series?.Name;

            if (string.IsNullOrEmpty(seriesName) || seriesInfo.Seasons.Count == 0)
                return;

            bool downloadInserted = false;

            foreach (SeriesReminderModel reminder in seriesGroup)
            {
                if (reminder.User is null || reminder.Series is null || string.IsNullOrEmpty(reminder.User.TelegramChatId))
                    continue;

                List<EpisodeModel> matchingEpisodes = FilterEpisodesForReminder(changedEpisodes, reminder.Language);

                if (!matchingEpisodes.HasItems())
                    continue;

                StringBuilder sb = new();
                sb.AppendLine($"{Emoji.Confetti} Neue Folge(n) fuer <b>{seriesName}</b> sind erschienen! {Emoji.Confetti}\n");
                sb.AppendLine($"{Emoji.Wave} Staffel: <b>{seriesInfo.SeasonCount}</b> Episode: <b>{seriesInfo.Seasons.Last().EpisodeCount}</b> {Emoji.Wave}\n");

                foreach (EpisodeModel episode in matchingEpisodes.Take(maxCount))
                {
                    sb.AppendLine($"{Emoji.SmallBlackSquare} S<b>{episode.Season:D2}</b> E<b>{episode.Episode:D2}</b> {Emoji.HeavyMinus} {episode.Name} [{episode.Languages.ToLanguageText()}]");
                }

                if (matchingEpisodes.Count > maxCount)
                {
                    sb.AppendLine($"{Emoji.SmallBlackSquare} <b>...</b>");
                    sb.AppendLine($"+{matchingEpisodes.Count - maxCount}");
                }

                sb.AppendLine($"\nFuer Benachrichtigung eingestellte Sprache(n): {reminder.Language.ToLanguageText()}");

                string messageText = string.IsNullOrEmpty(reminder.User.Username)
                    ? sb.ToString()
                    : $"Hallo {reminder.User.Username}!\n\n{sb}";

                UserWebsiteSettings? userWebsiteSettings = await dbService.GetUserWebsiteSettings(reminder.User.Id.ToString());
                bool silentMessage = userWebsiteSettings?.TelegramDisableNotifications == 1;
                bool noCoverArt = userWebsiteSettings?.TelegramNoCoverArtNotifications == 1;
                long chatId = long.Parse(reminder.User.TelegramChatId);

                if (string.IsNullOrEmpty(seriesInfo.CoverArtUrl) || noCoverArt)
                {
                    await telegramBotService.SendMessageAsync(chatId, messageText, silentMessage: silentMessage, showLinkPreview: !noCoverArt);
                }
                else
                {
                    await telegramBotService.SendPhotoAsync(chatId, seriesInfo.CoverArtUrl, messageText, silentMessage: silentMessage);
                }

                await dbService.InsertDownloadAsync(reminder.User.Id.ToString(), reminder.Series.Id.ToString(), matchingEpisodes);
                downloadInserted = true;

                string usernameText = string.IsNullOrEmpty(reminder.User.Username) ? "N/A" : reminder.User.Username;
                logger.LogInformation("{Timestamp} | Sent 'New Episodes' notification to chat: {Username}|{ChatId}", DateTime.Now, usernameText, reminder.User.TelegramChatId);
                await episodeReminderDelayService.DelayAfterNotificationAsync(appSettings);
            }

            if (downloadInserted && TryGetAdminChatId(botSettings, out long adminChatId))
            {
                await telegramBotService.SendMessageAsync(adminChatId, "Die Folgen wurden in die Download-Datenbank eingetragen.", silentMessage: true);
            }
        }

        private List<EpisodeModel> FilterEpisodesForReminder(IEnumerable<EpisodeModel> episodes, Language reminderLanguages)
        {
            List<EpisodeModel> matchingEpisodes = [];
            List<Language> reminderFlags = reminderLanguages.GetFlags(Language.None).ToList();

            foreach (EpisodeModel episode in episodes)
            {
                IEnumerable<Language> episodeFlags = episode.UpdatedLanguageFlags.HasItems()
                    ? episode.UpdatedLanguageFlags!
                    : episode.Languages.GetFlags(Language.None);

                List<Language> wantedLanguages = episodeFlags
                    .Where(_ => reminderFlags.Contains(_))
                    .Distinct()
                    .ToList();

                if (!wantedLanguages.HasItems())
                    continue;

                matchingEpisodes.Add(CloneEpisode(episode, AggregateLanguages(wantedLanguages)));
            }

            return matchingEpisodes;
        }

        private async Task SendAdminNotificationAsync(TelegramBotSettingsModel? botSettings, List<EpisodeModel> episodes, string messageText)
        {
            if (!TryGetAdminChatId(botSettings, out long adminChatId))
                return;

            StringBuilder sb = new();
            sb.AppendLine("Neue Admin Meldung:\n");
            sb.AppendLine($"{messageText}\n");

            foreach (EpisodeModel episode in episodes)
            {
                sb.AppendLine($"{Emoji.SmallBlackSquare} S<b>{episode.Season:D2}</b> E<b>{episode.Episode:D2}</b> {Emoji.HeavyMinus} {episode.Name} [{episode.Languages.ToLanguageText()}]");
            }

            await telegramBotService.SendMessageAsync(adminChatId, sb.ToString(), silentMessage: true);
            logger.LogInformation("{Timestamp} | Sent admin notification to chat: {AdminChatId}", DateTime.Now, adminChatId);
        }

        private async Task SendAdminErrorAsync(TelegramBotSettingsModel? botSettings, Exception exception)
        {
            if (!TryGetAdminChatId(botSettings, out long adminChatId))
                return;

            await telegramBotService.SendMessageAsync(adminChatId, $"Error: {exception}", silentMessage: true);
        }

        private async Task<(bool updateAvailable, SeriesInfoModel? seriesInfo, List<EpisodeModel>? updateEpisodes, List<EpisodeModel>? newEpisodes, List<EpisodeModel>? namesUpdatedEpisodes)> UpdateNeededAsync(SeriesReminderModel seriesReminder)
        {
            if (seriesReminder.Series is null || string.IsNullOrEmpty(seriesReminder.Series.Path))
                return (false, null, null, null, null);

            IStreamingPortalService? streamingPortalService = seriesReminder.Series.StreamingPortal switch
            {
                StreamingPortal.AniWorld => aniWorldService,
                StreamingPortal.STO => stoService,
                _ => null
            };

            if (streamingPortalService is null)
                return (false, null, null, null, null);

            SeriesInfoModel? seriesInfo = await streamingPortalService.GetMediaInfoAsync(seriesReminder.Series.Path.TrimStart('/'));

            if (seriesInfo?.Seasons is not { Count: > 0 })
                return (false, null, null, null, null);

            List<EpisodeModel>? languageUpdateEpisodes = await GetLanguageUpdateEpisodesAsync(seriesReminder.Series.Id, seriesInfo);
            List<EpisodeModel>? newEpisodes = await GetNewEpisodesAsync(seriesReminder.Series.Id, seriesInfo);
            List<EpisodeModel>? namesUpdatedEpisodes = await GetEpisodeNamesUpdatesAsync(seriesReminder.Series.Id, seriesInfo);

            if (languageUpdateEpisodes.HasItems() || newEpisodes.HasItems() || namesUpdatedEpisodes.HasItems())
                return (true, seriesInfo, languageUpdateEpisodes, newEpisodes, namesUpdatedEpisodes);

            return (false, null, null, null, null);
        }

        private async Task<List<EpisodeModel>?> GetNewEpisodesAsync(int seriesId, SeriesInfoModel seriesInfo)
        {
            List<EpisodeModel>? dbEpisodes = await dbService.GetSeriesEpisodesAsync(seriesId);
            List<EpisodeModel> newEpisodes = [];

            if (!dbEpisodes.HasItems())
                return seriesInfo.Seasons.SelectMany(_ => _.Episodes).Select(CloneEpisode).ToList();

            foreach (SeasonModel season in seriesInfo.Seasons)
            {
                List<EpisodeModel> tempNewEpisodes = season.Episodes
                    .Where(seasonEpisode => !dbEpisodes!.Any(dbEpisode => dbEpisode.Episode == seasonEpisode.Episode && dbEpisode.Season == seasonEpisode.Season))
                    .Select(episode => CloneEpisode(episode, episode.Languages, seriesId))
                    .ToList();

                if (tempNewEpisodes.HasItems())
                    newEpisodes.AddRange(tempNewEpisodes);
            }

            return newEpisodes;
        }

        private async Task<List<EpisodeModel>?> GetLanguageUpdateEpisodesAsync(int seriesId, SeriesInfoModel seriesInfo)
        {
            List<EpisodeModel>? dbEpisodes = await dbService.GetSeriesEpisodesAsync(seriesId);

            if (!dbEpisodes.HasItems())
                return null;

            List<EpisodeModel> updateEpisodes = [];

            foreach (EpisodeModel episode in seriesInfo.Seasons.SelectMany(_ => _.Episodes))
            {
                EpisodeModel? episodeNeedingUpdate = dbEpisodes!.SingleOrDefault(_ => _.Season == episode.Season && _.Episode == episode.Episode && _.Languages != episode.Languages);

                if (episodeNeedingUpdate is null)
                    continue;

                List<Language> newLanguageFlags = episode.Languages
                    .GetFlags(Language.None)
                    .Except(episodeNeedingUpdate.Languages.GetFlags(Language.None))
                    .Distinct()
                    .ToList();

                if (!newLanguageFlags.HasItems())
                    continue;

                EpisodeModel updatedEpisode = CloneEpisode(episodeNeedingUpdate, episode.Languages);
                updatedEpisode.Name = episode.Name ?? updatedEpisode.Name;
                updatedEpisode.UpdatedLanguageFlags = newLanguageFlags;
                updateEpisodes.Add(updatedEpisode);
            }

            return updateEpisodes;
        }

        private async Task<List<EpisodeModel>?> GetEpisodeNamesUpdatesAsync(int seriesId, SeriesInfoModel seriesInfo)
        {
            List<EpisodeModel>? dbEpisodes = await dbService.GetSeriesEpisodesAsync(seriesId);

            if (!dbEpisodes.HasItems())
                return null;

            List<EpisodeModel> updatedEpisodes = [];

            foreach (SeasonModel season in seriesInfo.Seasons)
            {
                List<EpisodeModel> tempUpdatedEpisodes = season.Episodes
                    .Where(seasonEpisode => dbEpisodes!.Any(dbEpisode =>
                        dbEpisode.Episode == seasonEpisode.Episode &&
                        dbEpisode.Season == seasonEpisode.Season &&
                        dbEpisode.Name != seasonEpisode.Name))
                    .ToList();

                if (!tempUpdatedEpisodes.HasItems())
                    continue;

                foreach (EpisodeModel episode in tempUpdatedEpisodes)
                {
                    EpisodeModel dbEpisode = dbEpisodes!.First(dbEpisode => dbEpisode.Episode == episode.Episode && dbEpisode.Season == episode.Season);
                    EpisodeModel updatedEpisode = CloneEpisode(episode, episode.Languages, dbEpisode.SeriesId);
                    updatedEpisode.Id = dbEpisode.Id;
                    updatedEpisode.SeriesId = dbEpisode.SeriesId;
                    updatedEpisodes.Add(updatedEpisode);
                }
            }

            return updatedEpisodes;
        }

        private static EpisodeModel CloneEpisode(EpisodeModel episode)
        {
            return CloneEpisode(episode, episode.Languages, episode.SeriesId);
        }

        private static EpisodeModel CloneEpisode(EpisodeModel episode, Language languages, int? seriesId = null)
        {
            return new EpisodeModel
            {
                Id = episode.Id,
                SeriesId = seriesId ?? episode.SeriesId,
                Season = episode.Season,
                Episode = episode.Episode,
                Name = episode.Name,
                Languages = languages,
                M3U8DirectLink = episode.M3U8DirectLink,
                DirectViewLinks = episode.DirectViewLinks,
                UpdatedLanguageFlags = episode.UpdatedLanguageFlags is null ? null : [.. episode.UpdatedLanguageFlags]
            };
        }

        private static Language AggregateLanguages(IEnumerable<Language> languages)
        {
            Language result = Language.None;

            foreach (Language language in languages)
            {
                result |= language;
            }

            return result;
        }

        private static bool TryGetAdminChatId(TelegramBotSettingsModel? botSettings, out long adminChatId)
        {
            adminChatId = default;

            return botSettings is not null &&
                   !string.IsNullOrWhiteSpace(botSettings.AdminChat) &&
                   long.TryParse(botSettings.AdminChat, out adminChatId);
        }
    }
}
