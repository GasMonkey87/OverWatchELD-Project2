using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class VtcConnectService
    {
        public async Task<bool> TestConnectionAsync(string apiUrl, string token)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
                return false;

            // Very safe "ping" style check: GET the base URL.
            // Many VTC APIs will require a specific endpoint later.
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(4);

                // Optional auth header if token provided
                if (!string.IsNullOrWhiteSpace(token))
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var resp = await http.GetAsync(apiUrl);
                return resp.IsSuccessStatusCode || (int)resp.StatusCode == 401 || (int)resp.StatusCode == 403;
                // 401/403 still proves the server exists, token might be wrong.
            }
            catch
            {
                return false;
            }
        }
    }
}
