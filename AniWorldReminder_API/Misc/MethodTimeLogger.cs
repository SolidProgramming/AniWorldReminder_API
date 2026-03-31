using System.Reflection;

namespace AniWorldReminder_API.Misc
{
    public static class MethodTimeLogger
    {
        private static ILoggerFactory? loggerFactory;

        public static void Configure(ILoggerFactory factory)
        {
            loggerFactory = factory;
        }

        public static void Log(MethodBase methodBase, TimeSpan elapsed, string message)
        {
            ILogger logger = (loggerFactory ?? LoggerFactory.Create(_ => { }))
                .CreateLogger(methodBase.DeclaringType?.FullName ?? "MethodTimer");

            logger.LogInformation(
                "Method {MethodName} took {ElapsedMs} ms. {Message}",
                $"{methodBase.DeclaringType?.Name}.{methodBase.Name}",
                elapsed.TotalMilliseconds,
                message);
        }
    }
}
