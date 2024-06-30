namespace AniWorldReminder_API.Factories
{
    public class StreamingPortalServiceFactory : IStreamingPortalServiceFactory
    {
        private readonly Dictionary<StreamingPortal, IStreamingPortalService> StreamingPortalServices = [];

        public void AddService(StreamingPortal streamingPortal, IServiceProvider sp)
        {
            if (!StreamingPortalServices.ContainsKey(streamingPortal))
            {
                StreamingPortalServices.Add(streamingPortal, CreateService(streamingPortal, sp));
            }
        }

        public IStreamingPortalService GetService(StreamingPortal streamingPortal)
        {
            return StreamingPortalServices[streamingPortal];
        }

        private static IStreamingPortalService CreateService(StreamingPortal streamingPortal, IServiceProvider sp)
        {
            Interfaces.IHttpClientFactory httpClientFactory = sp.GetRequiredService<Interfaces.IHttpClientFactory>();
            ITMDBService tmdbService = sp.GetRequiredService<ITMDBService>();

            switch (streamingPortal)
            {
                case StreamingPortal.STO:
                    ILogger<AniWorldSTOService> loggerSTO = sp.GetRequiredService<ILogger<AniWorldSTOService>>();
                    return new AniWorldSTOService(loggerSTO, httpClientFactory, "https://s.to", "S.TO", streamingPortal, tmdbService);
                case StreamingPortal.AniWorld:
                    ILogger<AniWorldSTOService> loggerAniWorld = sp.GetRequiredService<ILogger<AniWorldSTOService>>();
                    return new AniWorldSTOService(loggerAniWorld, httpClientFactory, "https://aniworld.to", "AniWorld", streamingPortal, tmdbService);
                case StreamingPortal.MegaKino:
                    ILogger<MegaKinoService> loggerMegaKino = sp.GetRequiredService<ILogger<MegaKinoService>>();
                    return new MegaKinoService(loggerMegaKino, httpClientFactory, "https://megakino.ws", "MegaKino", streamingPortal);
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
