using Microsoft.Extensions.DependencyInjection;

namespace OverWatchELD.Services.ATS
{
    public static class ServiceCollectionAtsLoadCreationExtensions
    {
        public static IServiceCollection AddOverWatchAtsLoadCreation(this IServiceCollection services)
        {
            services.AddSingleton<AtsUserFolderLocatorService>();
            services.AddSingleton<AtsUserModScannerService>();
            services.AddSingleton<AtsLoadCreationService>();
            services.AddSingleton<AtsSaveLoadExportService>();
            return services;
        }
    }
}
