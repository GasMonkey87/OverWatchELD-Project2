using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class BolAutoCreateService
    {
        public static BolAutoCreateService Shared { get; } = new();

        private string BaseDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD",
                "BOL");

        private string RecordsDir => Path.Combine(BaseDir, "records");
        private string HtmlDir => Path.Combine(BaseDir, "html");

        private BolAutoCreateService()
        {
        }

        public async Task CreateOrUpdateAsync(BolRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.LoadNumber))
                return;

            Directory.CreateDirectory(RecordsDir);
            Directory.CreateDirectory(HtmlDir);

            var jsonPath = Path.Combine(RecordsDir, $"{SafeFile(record.LoadNumber)}.json");
            var htmlPath = Path.Combine(HtmlDir, $"{SafeFile(record.LoadNumber)}.html");

            var opts = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(record, opts));
            await File.WriteAllTextAsync(htmlPath, BuildHtml(record), Encoding.UTF8);
        }

        private static string SafeFile(string input)
        {
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();

            foreach (var ch in input)
            {
                var invalid = false;
                for (var i = 0; i < bad.Length; i++)
                {
                    if (bad[i] == ch)
                    {
                        invalid = true;
                        break;
                    }
                }

                sb.Append(invalid ? '_' : ch);
            }

            var safe = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(safe) ? "bol" : safe;
        }

        private static string E(string? value)
        {
            return System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        }

        private static string BuildHtml(BolRecord r)
        {
            var weight = r.WeightLbs > 0
                ? r.WeightLbs.ToString("N0", CultureInfo.InvariantCulture) + " lbs"
                : "";

            var created = r.CreatedUtc == default
                ? ""
                : r.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture);

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"" />
<title>BOL {E(r.LoadNumber)}</title>
<style>
body {{
    font-family: Segoe UI, Arial, sans-serif;
    margin: 32px;
    color: #111;
}}
.header {{
    display: flex;
    justify-content: space-between;
    align-items: center;
    border-bottom: 2px solid #222;
    padding-bottom: 12px;
    margin-bottom: 20px;
}}
h1 {{
    margin: 0;
    font-size: 28px;
}}
.sub {{
    color: #555;
    font-size: 13px;
}}
.grid {{
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 14px;
    margin-bottom: 22px;
}}
.card {{
    border: 1px solid #ccc;
    border-radius: 10px;
    padding: 14px;
}}
.label {{
    color: #666;
    font-size: 12px;
    text-transform: uppercase;
    margin-bottom: 4px;
}}
.value {{
    font-size: 16px;
    font-weight: 600;
}}
.notes {{
    white-space: pre-wrap;
    min-height: 80px;
}}
.footer {{
    margin-top: 28px;
    font-size: 12px;
    color: #666;
}}
</style>
</head>
<body>
<div class=""header"">
    <div>
        <h1>Bill of Lading</h1>
        <div class=""sub"">OverWatch ELD</div>
    </div>
    <div>
        <div class=""label"">Load Number</div>
        <div class=""value"">{E(r.LoadNumber)}</div>
    </div>
</div>

<div class=""grid"">
    <div class=""card"">
        <div class=""label"">Driver</div>
        <div class=""value"">{E(r.Driver)}</div>
    </div>
    <div class=""card"">
        <div class=""label"">Truck</div>
        <div class=""value"">{E(r.Truck)}</div>
    </div>
    <div class=""card"">
        <div class=""label"">Cargo</div>
        <div class=""value"">{E(r.Cargo)}</div>
    </div>
    <div class=""card"">
        <div class=""label"">Weight</div>
        <div class=""value"">{E(weight)}</div>
    </div>
    <div class=""card"">
        <div class=""label"">Origin</div>
        <div class=""value"">{E(r.StartLocation)}</div>
    </div>
    <div class=""card"">
        <div class=""label"">Destination</div>
        <div class=""value"">{E(r.EndLocation)}</div>
    </div>
    <div class=""card"">
        <div class=""label"">Pickup Time</div>
        <div class=""value"">{E(created)}</div>
    </div>
    <div class=""card"">
        <div class=""label"">Status</div>
        <div class=""value"">{E(r.Status)}</div>
    </div>
</div>

<div class=""card"">
    <div class=""label"">Notes</div>
    <div class=""notes"">{E(r.Notes)}</div>
</div>

<div class=""footer"">
Generated by OverWatch ELD at {E(DateTime.Now.ToString("yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture))}
</div>
</body>
</html>";
        }

        public sealed class BolRecord
        {
            public string LoadNumber { get; set; } = "";
            public string Driver { get; set; } = "";
            public string Truck { get; set; } = "";
            public string Cargo { get; set; } = "";
            public double WeightLbs { get; set; }
            public string StartLocation { get; set; } = "";
            public string EndLocation { get; set; } = "";
            public string Status { get; set; } = "Picked Up";
            public string Notes { get; set; } = "";
            public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        }
    }
}