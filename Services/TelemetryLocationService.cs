using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services;

public sealed class TelemetryLocationService
{
    private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };

    public string City { get; private set; } = "";
    public string State { get; private set; } = "";

    public string CityState
    {
        get
        {
            var cs = $"{City}, {State}".Trim().Trim(',');
            return cs == "," ? "" : cs;
        }
    }

    // Default Funbit endpoints
    public string TelemetryUrl { get; set; } = "http://localhost:25555/api/ats/telemetry";

    public async Task PollAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(TelemetryUrl).ConfigureAwait(false);
            Parse(json);
        }
        catch
        {
            // swallow telemetry errors (no crash / no log spam)
        }
    }

    public void Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            City =
                ReadString(root, "navigation.city") ??
                ReadString(root, "nav.city") ??
                ReadString(root, "gps.city") ??
                ReadString(root, "truck.navigation.city") ??
                "";

            State =
                ReadString(root, "navigation.state") ??
                ReadString(root, "nav.state") ??
                ReadString(root, "gps.state") ??
                ReadString(root, "truck.navigation.state") ??
                "";
        }
        catch
        {
            // ignore parse issues
        }
    }

    private static string? ReadString(JsonElement root, string dottedPath)
    {
        var parts = dottedPath.Split('.');
        var cur = root;
        foreach (var p in parts)
        {
            if (cur.ValueKind != JsonValueKind.Object) return null;
            if (!cur.TryGetProperty(p, out var next)) return null;
            cur = next;
        }
        return cur.ValueKind == JsonValueKind.String ? cur.GetString() : null;
    }
}
