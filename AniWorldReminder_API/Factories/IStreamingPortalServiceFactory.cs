namespace AniWorldReminder_API.Factories
{
    public interface IStreamingPortalServiceFactory
    {
        void AddService(StreamingPortal streamingPortal, IServiceProvider sp);
        IStreamingPortalService GetService(StreamingPortal streamingPortal);
    }
}
