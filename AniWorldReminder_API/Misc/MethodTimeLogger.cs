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

            string additionalInfo = $" Additional Info: {message}";
            string duration = $" Runtime: {elapsed:mm}m {elapsed:ss}s.";

            Type? type = methodBase.DeclaringType;

            if (type == null)
                return;

            Type? @interface = type.GetInterfaces()
                .FirstOrDefault(i => type.GetInterfaceMap(i).TargetMethods.Any(m => m.DeclaringType == type));

            string info = "Executed ";

            if (@interface is not null && @interface.FullName == "Quartz.IJob" && methodBase.Name == "Execute")
            {
                info += $"CronJob: ";
            }

            logger.LogInformation($"{DateTime.Now} | " + info + "{Class}.{Method}.{Duration}{Message}",
                methodBase.DeclaringType!.Name,
                methodBase.Name,
                (elapsed.Seconds > 0 ? duration : ""),
                (string.IsNullOrEmpty(message) ? "" : additionalInfo));
        }

        public static void LogExecution(MethodBase methodBase)
        {
            Type? type = methodBase.DeclaringType;

            if (type == null)
                return;

            Type? @interface = type.GetInterfaces()
                .FirstOrDefault(i => type.GetInterfaceMap(i).TargetMethods.Any(m => m.DeclaringType == type));

            string info = "Started ";

            if (@interface is not null && @interface.FullName == "Quartz.IJob" && methodBase.Name == "Execute")
            {
                info += $"CronJob: ";
            }

            ILogger logger = (loggerFactory ?? LoggerFactory.Create(_ => { }))
                .CreateLogger(methodBase.DeclaringType?.FullName ?? "MethodTimer");

            logger.LogInformation($"{DateTime.Now} | " + info + "{Class}.{Method}.",
                methodBase.DeclaringType!.Name,
                methodBase.Name);
        }
    }
}
