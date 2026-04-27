using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AniWorldReminder_API.Classes
{
    public class SwaggerAuthOperationFilter : IOperationFilter
    {
        private static readonly string[] ApiKeyPaths =
        [
            "getDownloads",
            "removeFinishedDownload",
            "getDownloadsCount",
            "captchaNotify",
            "setDownloaderPreferences",
            "getDownloaderPreferences",
            "api/v3/"
        ];

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            bool requiresBearer = context.ApiDescription.ActionDescriptor.EndpointMetadata?
                .OfType<AuthorizeAttribute>()
                .Any() == true;

            string relativePath = context.ApiDescription.RelativePath ?? string.Empty;
            bool requiresApiKey = ApiKeyPaths.Any(path =>
                relativePath.StartsWith(path, StringComparison.OrdinalIgnoreCase));

            if (!requiresBearer && !requiresApiKey)
                return;

            operation.Security ??= [];

            if (requiresBearer)
            {
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [
                        new OpenApiSecuritySchemeReference("Bearer")
                    ] = []
                });
            }

            if (requiresApiKey)
            {
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [
                        new OpenApiSecuritySchemeReference("ApiKey")
                    ] = []
                });
            }
        }
    
    }
}
