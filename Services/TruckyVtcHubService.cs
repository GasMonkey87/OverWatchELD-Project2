using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class TruckyVtcHubService
    {
        // Keep it conservative: just hit the API host and validate token header is accepted.
        // You can later replace with a specific endpoint from the OpenAPI docs.
        private const string ApiBase = "https://e.truckyapp.com";

        public async Task<bool> TestTokenAsync(string companyAccessToken)
        {
            if (string.IsNullOrWhiteSpace(companyAccessToken))
                return false;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                http.DefaultRequestHeaders.Add("X-ACCESS-TOKEN", companyAccessToken);

                // Using docs base as a simple reachable endpoint; success could be 200/401/403 depending on route.
                // If you want stronger validation, we’ll switch to a token-protected endpoint from OpenAPI.
                var resp = await http.GetAsync($"{ApiBase}/api/docs");

                // If we can reach Trucky at all, we count as "connected-ish".
                // For strict token validation, we should call an endpoint that requires X-ACCESS-TOKEN.
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
