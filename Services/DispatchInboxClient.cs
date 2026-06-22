using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OverWatchELD.Models;

namespace OverWatchELD.Services;

public sealed class DispatchInboxClient
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<DispatchMessage>? MessageReceived;
    public event Action<string>? StatusChanged;

    public bool IsRunning => _loopTask is { IsCompleted: false };

    public void Start(string relayBaseWsUrl, string driverId, string? token = null)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(relayBaseWsUrl))
            throw new ArgumentException("relayBaseWsUrl is required.", nameof(relayBaseWsUrl));
        if (string.IsNullOrWhiteSpace(driverId))
            throw new ArgumentException("driverId is required.", nameof(driverId));

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(relayBaseWsUrl, driverId, token, _cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;

        try { _ws?.Abort(); } catch { }
        try { _ws?.Dispose(); } catch { }
        _ws = null;

        _loopTask = null;
    }

    private async Task RunLoopAsync(string relayBaseWsUrl, string driverId, string? token, CancellationToken ct)
    {
        var qs = $"driverId={Uri.EscapeDataString(driverId)}";
        if (!string.IsNullOrWhiteSpace(token))
            qs += $"&token={Uri.EscapeDataString(token)}";

        var wsUri = new Uri($"{relayBaseWsUrl.TrimEnd('/')}/ws?{qs}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                StatusChanged?.Invoke("Connecting…");
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(wsUri, ct);
                StatusChanged?.Invoke("Connected");

                var buffer = new byte[64 * 1024];

                while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    var result = await _ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    // Relay sends envelope: { type: "dispatch_message", data: { ... } }
                    var env = JsonSerializer.Deserialize<RelayEnvelope>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (env?.Type == "dispatch_message" && env.Data != null)
                        MessageReceived?.Invoke(env.Data);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Disconnected: {ex.Message}");
            }

            // basic reconnect delay
            try { await Task.Delay(2000, ct); } catch { }
        }
    }

    private sealed class RelayEnvelope
    {
        public string Type { get; set; } = "";
        public DispatchMessage? Data { get; set; }
    }
}
