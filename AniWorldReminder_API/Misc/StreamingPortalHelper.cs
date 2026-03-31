namespace AniWorldReminder_API.Misc
{
    public static class StreamingPortalHelper
    {
        private static readonly Dictionary<string, StreamingPortal> StreamingPortals = new()
        {
            { "AniWorld", StreamingPortal.AniWorld },
            { "STO", StreamingPortal.STO },
        };
        public static StreamingPortal GetStreamingPortalByName(string streamingPortalName)
        {
            if (StreamingPortals.Any(_ => _.Key == streamingPortalName))
            {
                return StreamingPortals[streamingPortalName];
            }

            return StreamingPortal.Undefined;
        }
        public static string? GetStreamingPortalName(StreamingPortal streamingPortal)
        {
            if (StreamingPortals.Any(_ => _.Value == streamingPortal))
            {
                return StreamingPortals.First(_ => _.Value == streamingPortal).Key;
            }

            return null;
        }
    }
}
