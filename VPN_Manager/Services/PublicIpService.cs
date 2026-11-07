using System;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace VPN_Manager;

internal sealed class PublicIpService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly object _lock = new();
    private readonly Timer _fallbackTimer;
    private string _publicIp = "Checking IP...";
    private DateTimeOffset _lastFetched = DateTimeOffset.MinValue;
    private bool _disposed;
    
    // Used to cancel in-flight requests when a forced refresh occurs
    private CancellationTokenSource? _fetchCts;

    public string CurrentIp
    {
        get
        {
            lock (_lock)
            {
                return _publicIp;
            }
        }
    }

    public event EventHandler? IpChanged;

    public PublicIpService()
    {
        var handler = new SocketsHttpHandler
        {
            // CRITICAL: Prevents reusing old TCP sockets that bypass the VPN tunnel
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectTimeout = TimeSpan.FromSeconds(3),
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        _fallbackTimer = new Timer(
            _ => _ = RefreshIpAsync(force: false),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    public void StartAsync() => _ = RefreshIpAsync(force: true);

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        _ = Task.Delay(1500).ContinueWith(_ => RefreshIpAsync(force: true), TaskScheduler.Default).Unwrap();
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            _ = Task.Delay(1500).ContinueWith(_ => RefreshIpAsync(force: true), TaskScheduler.Default).Unwrap();
        }
    }

    public async Task RefreshIpAsync(bool force = false)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (!force && DateTimeOffset.UtcNow - _lastFetched < TimeSpan.FromSeconds(30))
            {
                return;
            }

            // Cancel any currently running fetch so it doesn't block this new one
            _fetchCts?.Cancel();
            _fetchCts?.Dispose();
            _fetchCts = new CancellationTokenSource();
            cts = _fetchCts;
        }

        var token = cts.Token;

        try
        {
            string? ip = await FetchPublicIpAsync(token);
            
            if (token.IsCancellationRequested)
            {
                return; // A newer request took over
            }

            if (!string.IsNullOrWhiteSpace(ip))
            {
                ip = ip.Trim();
                bool changed = false;
                lock (_lock)
                {
                    if (_publicIp != ip)
                    {
                        _publicIp = ip;
                        changed = true;
                    }
                    _lastFetched = DateTimeOffset.UtcNow;
                }

                if (changed)
                {
                    IpChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch
        {
        }
    }

    private async Task<string?> FetchPublicIpAsync(CancellationToken token)
    {
        try
        {
            using var response = await _httpClient.GetAsync("https://1.1.1.1/cdn-cgi/trace", token);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(token);
                using var reader = new StringReader(content);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.StartsWith("ip=", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring(3).Trim();
                    }
                }
            }
        }
        catch (OperationCanceledException) { return null; }
        catch { }

        try
        {
            using var response = await _httpClient.GetAsync("https://api.ipify.org", token);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(token);
            }
        }
        catch (OperationCanceledException) { return null; }
        catch { }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _fallbackTimer.Dispose();
        
        lock (_lock)
        {
            _fetchCts?.Cancel();
            _fetchCts?.Dispose();
            _fetchCts = null;
        }
        
        _httpClient.Dispose();
    }
}