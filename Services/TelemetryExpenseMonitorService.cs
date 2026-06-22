using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class TelemetryExpenseMonitorService
    {
        private readonly HttpClient _http = new HttpClient();

        private double? _lastFuelGallons;
        private double? _lastFuelPercent;
        private double? _lastOdometerMiles;
        private decimal? _lastKnownMoney;
        private DateTimeOffset _lastFuelReceiptUtc = DateTimeOffset.MinValue;
        private DateTimeOffset _lastTollReceiptUtc = DateTimeOffset.MinValue;
        private DateTimeOffset _lastTicketReceiptUtc = DateTimeOffset.MinValue;

        private readonly HashSet<string> _seenEventKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public async Task OnTelemetryAsync(TelemetrySnapshot snapshot, string? rawJson)
        {
            if (snapshot == null || !snapshot.Connected)
                return;

            JsonDocument? doc = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(rawJson))
                    doc = JsonDocument.Parse(rawJson);

                await DetectFuelAsync(snapshot, doc?.RootElement);
                await DetectTollOrTicketAsync(snapshot, doc?.RootElement);
            }
            catch
            {
            }
            finally
            {
                doc?.Dispose();

                _lastFuelGallons = snapshot.FuelGallons;
                _lastFuelPercent = snapshot.FuelPct;
                _lastOdometerMiles = snapshot.OdometerMiles;

                var money = TryReadMoney(rawJson);
                if (money.HasValue)
                    _lastKnownMoney = money.Value;
            }
        }

        private async Task DetectFuelAsync(TelemetrySnapshot snapshot, JsonElement? root)
        {
            var currentGallons = snapshot.FuelGallons;
            if (!currentGallons.HasValue)
                return;

            var previousGallons = _lastFuelGallons;
            if (!previousGallons.HasValue)
                return;

            var added = currentGallons.Value - previousGallons.Value;
            var speedMph = Math.Abs(snapshot.SpeedMps * 2.23694);

            // Fuel receipt rule:
            // fuel went up by at least 3 gal, vehicle is stopped/near-stopped, and not spammed.
            if (added < 3.0 || speedMph > 3.0)
                return;

            if ((DateTimeOffset.UtcNow - _lastFuelReceiptUtc).TotalMinutes < 3)
                return;

            var pricePerGallon = 4.25m;
            var amount = Math.Round((decimal)added * pricePerGallon, 2);

            var receipt = BuildBaseReceipt(snapshot, "Fuel");
            receipt.Description = $"Fuel stop detected: {added:0.0} gal added.";
            receipt.Amount = amount;
            receipt.FuelGallonsAdded = added;
            receipt.FuelPercent = snapshot.FuelPct;
            receipt.RawDetails =
                $"Fuel before: {previousGallons.Value:0.0} gal\n" +
                $"Fuel after: {currentGallons.Value:0.0} gal\n" +
                $"Fuel added: {added:0.0} gal\n" +
                $"Estimated cost: {amount:C}\n" +
                $"Odometer: {(snapshot.OdometerMiles ?? 0):N1} mi";

            TelemetryExpenseReceiptStore.Add(receipt);
            _lastFuelReceiptUtc = DateTimeOffset.UtcNow;

            await PostReceiptToDiscordAsync(receipt, "fuel-receipts");
        }

        private async Task DetectTollOrTicketAsync(TelemetrySnapshot snapshot, JsonElement? root)
        {
            if (!root.HasValue)
                return;

            var tollAmount = FirstDecimal(root.Value,
                new[] { "events", "toll", "amount" },
                new[] { "event", "toll", "amount" },
                new[] { "toll", "amount" },
                new[] { "truck", "tollAmount" },
                new[] { "game", "tollAmount" });

            var ticketAmount = FirstDecimal(root.Value,
                new[] { "events", "fine", "amount" },
                new[] { "events", "ticket", "amount" },
                new[] { "event", "fine", "amount" },
                new[] { "event", "ticket", "amount" },
                new[] { "fine", "amount" },
                new[] { "ticket", "amount" },
                new[] { "truck", "fineAmount" },
                new[] { "game", "fineAmount" });

            var tollFlag = FirstBool(root.Value,
                new[] { "events", "toll" },
                new[] { "event", "toll" },
                new[] { "toll", "paid" },
                new[] { "truck", "tollPaid" });

            var ticketFlag = FirstBool(root.Value,
                new[] { "events", "fine" },
                new[] { "events", "ticket" },
                new[] { "event", "fine" },
                new[] { "event", "ticket" },
                new[] { "fine", "active" },
                new[] { "ticket", "active" });

            // Some telemetry readers expose only money. If money drops while stopped and no fuel increase,
            // classify small drops as tolls and larger drops as tickets.
            var money = FirstDecimal(root.Value,
                new[] { "game", "money" },
                new[] { "economy", "money" },
                new[] { "player", "money" },
                new[] { "money" });

            decimal? drop = null;
            if (money.HasValue && _lastKnownMoney.HasValue && money.Value < _lastKnownMoney.Value)
                drop = _lastKnownMoney.Value - money.Value;

            if ((!tollAmount.HasValue || tollAmount.Value <= 0) &&
                tollFlag != true &&
                drop.HasValue &&
                drop.Value > 0 &&
                drop.Value <= 100)
            {
                tollAmount = drop.Value;
                tollFlag = true;
            }

            if ((!ticketAmount.HasValue || ticketAmount.Value <= 0) &&
                ticketFlag != true &&
                drop.HasValue &&
                drop.Value >= 100)
            {
                ticketAmount = drop.Value;
                ticketFlag = true;
            }

            if ((tollFlag == true || (tollAmount.HasValue && tollAmount.Value > 0)) &&
                (DateTimeOffset.UtcNow - _lastTollReceiptUtc).TotalMinutes >= 2)
            {
                var amount = tollAmount ?? 0m;
                var key = $"TOLL|{snapshot.DriverName}|{snapshot.TruckName}|{snapshot.City}|{snapshot.State}|{amount}|{DateTimeOffset.UtcNow:yyyyMMddHHmm}";

                if (_seenEventKeys.Add(key))
                {
                    var receipt = BuildBaseReceipt(snapshot, "Toll");
                    receipt.Description = amount > 0
                        ? $"Toll deduction detected: {amount:C}."
                        : "Toll deduction detected.";
                    receipt.Amount = amount;
                    receipt.RawDetails =
                        $"Toll Amount: {(amount > 0 ? amount.ToString("C") : "--")}\n" +
                        $"Odometer: {(snapshot.OdometerMiles ?? 0):N1} mi\n" +
                        $"Fuel: {(snapshot.FuelPct ?? 0):0}%";

                    TelemetryExpenseReceiptStore.Add(receipt);
                    _lastTollReceiptUtc = DateTimeOffset.UtcNow;
                    await PostReceiptToDiscordAsync(receipt, "toll-receipts");
                }
            }

            if ((ticketFlag == true || (ticketAmount.HasValue && ticketAmount.Value > 0)) &&
                (DateTimeOffset.UtcNow - _lastTicketReceiptUtc).TotalMinutes >= 2)
            {
                var amount = ticketAmount ?? 0m;
                var reason = FirstString(root.Value,
                    new[] { "events", "fine", "reason" },
                    new[] { "events", "ticket", "reason" },
                    new[] { "fine", "reason" },
                    new[] { "ticket", "reason" },
                    new[] { "fine", "offence" },
                    new[] { "ticket", "offence" });

                var key = $"TICKET|{snapshot.DriverName}|{snapshot.TruckName}|{snapshot.City}|{snapshot.State}|{amount}|{reason}|{DateTimeOffset.UtcNow:yyyyMMddHHmm}";

                if (_seenEventKeys.Add(key))
                {
                    var receipt = BuildBaseReceipt(snapshot, "Ticket");
                    receipt.Description = string.IsNullOrWhiteSpace(reason)
                        ? (amount > 0 ? $"Ticket/fine deduction detected: {amount:C}." : "Ticket/fine deduction detected.")
                        : (amount > 0 ? $"Ticket/fine detected: {reason} ({amount:C})." : $"Ticket/fine detected: {reason}.");
                    receipt.Amount = amount;
                    receipt.RawDetails =
                        $"Ticket Reason: {(string.IsNullOrWhiteSpace(reason) ? "--" : reason)}\n" +
                        $"Ticket Amount: {(amount > 0 ? amount.ToString("C") : "--")}\n" +
                        $"Odometer: {(snapshot.OdometerMiles ?? 0):N1} mi\n" +
                        $"Fuel: {(snapshot.FuelPct ?? 0):0}%";

                    TelemetryExpenseReceiptStore.Add(receipt);
                    _lastTicketReceiptUtc = DateTimeOffset.UtcNow;
                    await PostReceiptToDiscordAsync(receipt, "ticket-receipts");
                }
            }
        }

        private static TelemetryExpenseReceipt BuildBaseReceipt(TelemetrySnapshot snapshot, string eventType)
        {
            var city = snapshot.City ?? "";
            var state = snapshot.State ?? "";
            var location = string.Join(", ", new[] { city, state }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (string.IsNullOrWhiteSpace(location))
                location = "Unknown";

            return new TelemetryExpenseReceipt
            {
                EventType = eventType,
                DriverName = string.IsNullOrWhiteSpace(snapshot.DriverName) ? "Driver" : snapshot.DriverName!,
                TruckName = FirstNonBlank(snapshot.TruckName, snapshot.TruckMakeModel, snapshot.TruckId, "Truck"),
                TruckId = snapshot.TruckId ?? "",
                City = city,
                State = state,
                Location = location,
                FuelPercent = snapshot.FuelPct,
                OdometerMiles = snapshot.OdometerMiles,
                CreatedUtc = DateTimeOffset.UtcNow
            };
        }

        private async Task PostReceiptToDiscordAsync(TelemetryExpenseReceipt receipt, string defaultChannelName)
        {
            try
            {
                var botBaseUrl = GetBotApiBaseUrl();
                var guildId = GetGuildId();

                if (string.IsNullOrWhiteSpace(botBaseUrl) || string.IsNullOrWhiteSpace(guildId))
                    return;

                botBaseUrl = botBaseUrl.Trim().TrimEnd('/');

                var details =
                    $"Receipt #: {receipt.ReceiptNumber}\n" +
                    $"Driver: {receipt.DriverName}\n" +
                    $"Truck: {receipt.TruckName}\n" +
                    $"Location: {receipt.Location}\n" +
                    $"Amount: {receipt.AmountDisplay}\n" +
                    $"Odometer: {(receipt.OdometerMiles ?? 0):N1} mi\n" +
                    $"Fuel: {(receipt.FuelPercent ?? 0):0}%\n\n" +
                    receipt.RawDetails;

                var body = new
                {
                    GuildId = guildId,
                    Category = receipt.EventType.Equals("Ticket", StringComparison.OrdinalIgnoreCase) ? "maintenance" : "fleet",
                    Title = $"{receipt.EventType} Receipt",
                    Message = receipt.Description,
                    Details = details,
                    DefaultChannelName = defaultChannelName
                };

                var json = JsonSerializer.Serialize(body);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await _http.PostAsync($"{botBaseUrl}/api/notifications/push", content);
                receipt.DiscordPosted = resp.IsSuccessStatusCode;
            }
            catch
            {
            }
        }

        private static string GetBotApiBaseUrl()
        {
            var fromSession = GetSessionValue("BotApiBaseUrl", "BotBaseUrl", "ApiBaseUrl", "VtcBotApiBaseUrl");
            if (!string.IsNullOrWhiteSpace(fromSession))
                return fromSession.Trim();

            return "https://overwatcheld.up.railway.app";
        }

        private static string GetGuildId()
        {
            var fromSession = GetSessionValue(
                "GuildId",
                "DiscordGuildId",
                "VtcGuildId",
                "LinkedGuildId",
                "SelectedGuildId",
                "CurrentGuildId",
                "ServerId",
                "DiscordServerId");

            if (!string.IsNullOrWhiteSpace(fromSession) && fromSession.Trim() != "0")
                return fromSession.Trim();

            try
            {
                var pairing = VtcPairingStore.Load();
                var pairingGuild = (pairing?.GuildId ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(pairingGuild) && pairingGuild != "0")
                    return pairingGuild;
            }
            catch
            {
            }

            return "";
        }

        private static string? GetSessionValue(params string[] names)
        {
            try
            {
                var app = System.Windows.Application.Current as OverWatchELD.App;
                var session = app?.Session;

                if (session == null)
                    return null;

                var type = session.GetType();

                foreach (var name in names)
                {
                    var prop = type.GetProperty(name);
                    if (prop == null)
                        continue;

                    var value = prop.GetValue(session)?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            catch
            {
            }

            return null;
        }

        private static decimal? TryReadMoney(string? rawJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawJson))
                    return null;

                using var doc = JsonDocument.Parse(rawJson);

                return FirstDecimal(doc.RootElement,
                    new[] { "game", "money" },
                    new[] { "economy", "money" },
                    new[] { "player", "money" },
                    new[] { "money" });
            }
            catch
            {
                return null;
            }
        }

        private static decimal? FirstDecimal(JsonElement root, params string[][] paths)
        {
            foreach (var path in paths)
            {
                if (!TryGetElement(root, out var el, path))
                    continue;

                try
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                        return d;

                    if (el.ValueKind == JsonValueKind.String &&
                        decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                        return d;
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool? FirstBool(JsonElement root, params string[][] paths)
        {
            foreach (var path in paths)
            {
                if (!TryGetElement(root, out var el, path))
                    continue;

                try
                {
                    if (el.ValueKind == JsonValueKind.True) return true;
                    if (el.ValueKind == JsonValueKind.False) return false;

                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                        return n != 0;

                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString();
                        if (bool.TryParse(s, out var b))
                            return b;
                        if (int.TryParse(s, out var i))
                            return i != 0;
                    }

                    if (el.ValueKind == JsonValueKind.Object)
                        return true;
                }
                catch
                {
                }
            }

            return null;
        }

        private static string FirstString(JsonElement root, params string[][] paths)
        {
            foreach (var path in paths)
            {
                if (!TryGetElement(root, out var el, path))
                    continue;

                try
                {
                    if (el.ValueKind == JsonValueKind.String)
                        return el.GetString() ?? "";

                    if (el.ValueKind == JsonValueKind.Number ||
                        el.ValueKind == JsonValueKind.True ||
                        el.ValueKind == JsonValueKind.False)
                        return el.ToString();
                }
                catch
                {
                }
            }

            return "";
        }

        private static bool TryGetElement(JsonElement root, out JsonElement element, params string[] path)
        {
            element = root;

            foreach (var part in path)
            {
                if (element.ValueKind != JsonValueKind.Object)
                    return false;

                if (!element.TryGetProperty(part, out element))
                    return false;
            }

            return true;
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}
