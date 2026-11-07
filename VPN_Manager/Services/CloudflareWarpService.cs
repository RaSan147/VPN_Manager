using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPN_Manager;

internal enum WarpConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
}

internal sealed record WarpConnectionState(WarpConnectionStatus Status, string? Message);

internal sealed class CloudflareWarpService : IDisposable
{
    private static readonly string[] WarpCliCandidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cloudflare", "Cloudflare WARP", "warp-cli.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Cloudflare", "Cloudflare WARP", "warp-cli.exe"),
    ];

    private readonly object _cacheLock = new();
    private WarpConnectionState _cachedState = new(WarpConnectionStatus.Disconnected, null);
    private DateTimeOffset _lastStatePoll = DateTimeOffset.MinValue;
    private readonly Timer _stateTimer;
    private bool _disposed;

    public event EventHandler? StateChanged;

    public CloudflareWarpService()
    {
        _stateTimer = new Timer(
            _ => RefreshState(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2));
    }

    public static string? FindWarpCli()
    {
        foreach (var candidate in WarpCliCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public WarpConnectionState GetConnectionState()
    {
        lock (_cacheLock)
        {
            if (DateTimeOffset.UtcNow - _lastStatePoll < TimeSpan.FromMilliseconds(800))
            {
                return _cachedState;
            }
        }

        var state = ComputeConnectionState();
        var changed = false;
        lock (_cacheLock)
        {
            if (_cachedState != state)
            {
                _cachedState = state;
                changed = true;
            }

            _lastStatePoll = DateTimeOffset.UtcNow;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return state;
    }

    public void RefreshState()
    {
        try
        {
            if (!_disposed)
            {
                GetConnectionState();
            }
        }
        catch (Exception)
        {
        }
    }

    private static WarpConnectionState ComputeConnectionState()
    {
        var cli = FindWarpCli();
        if (cli is null)
        {
            return new WarpConnectionState(WarpConnectionStatus.Disconnected, "WARP not installed");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cli,
                Arguments = "--json status",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new WarpConnectionState(WarpConnectionStatus.Disconnected, "Failed to start warp-cli");
            }

            if (!process.WaitForExit(3000))
            {
                try { process.Kill(); } catch { }
                return new WarpConnectionState(WarpConnectionStatus.Disconnected, "Timeout querying WARP");
            }

            var output = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output))
            {
                return new WarpConnectionState(WarpConnectionStatus.Disconnected, null);
            }

            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.TryGetProperty("status", out var statusProp))
            {
                var statusStr = statusProp.GetString() ?? string.Empty;
                if (statusStr.Equals("Connected", StringComparison.OrdinalIgnoreCase))
                {
                    return new WarpConnectionState(WarpConnectionStatus.Connected, "Connected");
                }
                else if (statusStr.Contains("Connecting", StringComparison.OrdinalIgnoreCase))
                {
                    return new WarpConnectionState(WarpConnectionStatus.Connecting, "Connecting");
                }
                else
                {
                    return new WarpConnectionState(WarpConnectionStatus.Disconnected, statusStr);
                }
            }
        }
        catch (Exception ex)
        {
            return new WarpConnectionState(WarpConnectionStatus.Disconnected, ex.Message);
        }

        return new WarpConnectionState(WarpConnectionStatus.Disconnected, null);
    }

    public async Task<bool> ConnectAsync()
    {
        return await RunWarpCliCommandAsync("connect");
    }

    public async Task<bool> DisconnectAsync()
    {
        return await RunWarpCliCommandAsync("disconnect");
    }

    public bool DisconnectSync(TimeSpan timeout)
    {
        var cli = FindWarpCli();
        if (cli is null)
        {
            return true;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cli,
                Arguments = "disconnect",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch { }
                return false;
            }

            RefreshState();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> RunWarpCliCommandAsync(string command)
    {
        var cli = FindWarpCli();
        if (cli is null)
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cli,
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await process.WaitForExitAsync(cts.Token);

            RefreshState();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stateTimer.Dispose();
    }
}
