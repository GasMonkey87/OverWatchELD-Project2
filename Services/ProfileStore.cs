using OverWatchELD.Models;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public ProfileStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATS_ELD"
        );
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "profiles.json");
    }

    public async Task<List<DriverProfile>> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return new List<DriverProfile>();

        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<DriverProfile>>(json, JsonOpts) ?? new List<DriverProfile>();
    }

    public async Task SaveAsync(List<DriverProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, JsonOpts);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
