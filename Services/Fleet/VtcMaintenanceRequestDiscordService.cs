using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services.Fleet
{
    public static class VtcMaintenanceRequestDiscordService
    {
        private static readonly HttpClient Http = new();

        public static async Task SendRequestAsync(
            string unit,
            string truck,
            string driver,
            string issue,
            string notes)
        {
            var baseUrl = "https://overwatcheld.up.railway.app";

            var payload = new
            {
                type = "maintenance_request",
                unit,
                truck,
                driver,
                issue,
                notes
            };

            var json = JsonSerializer.Serialize(payload);

            using var body =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

            await Http.PostAsync(
                $"{baseUrl}/api/maintenance/request",
                body);
        }
    }
}