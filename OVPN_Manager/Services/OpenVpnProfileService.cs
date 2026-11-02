using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace OVPN_Manager;

internal sealed record OpenVpnProfile(string Uid, string DisplayName, string ProfilePath);

internal enum OpenVpnConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
}

internal sealed record OpenVpnConnectionState(OpenVpnConnectionStatus Status, string? ProfileDisplayName)
{
    public bool IsConnected => Status == OpenVpnConnectionStatus.Connected;
}

internal sealed class OpenVpnProfileService
{
    private static readonly string ConnectConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenVPN Connect",
        "config.json");
    private static readonly string ConnectProfilesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenVPN Connect",
        "profiles");

    private readonly object _cacheLock = new();
    private readonly object _connectionOperationLock = new();
    private OpenVpnConnectionState _cachedState = new(OpenVpnConnectionStatus.Disconnected, null);
    private DateTimeOffset _lastStatePoll;
    private readonly Timer _stateTimer;

    private readonly object _tempProfileLock = new();
    private string? _currentTempProfilePath;
    private OpenVpnProfile? _currentProfile;

    public event EventHandler? StateChanged;

    public OpenVpnProfileService()
    {
        try
        {
            DisconnectAndWait(TimeSpan.FromSeconds(10));
        }
        catch (Exception)
        {
        }

        _stateTimer = new Timer(
            _ => RefreshState(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));
    }

    public string? FindOpenVpnGui()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenVPN", "bin", "openvpn-gui.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenVPN", "bin", "openvpn-gui.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public IReadOnlyList<OpenVpnProfile> FindProfiles()
    {
        return ReadProfilesFromConnectConfig()
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Uid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public OpenVpnConnectionState GetConnectionState(IReadOnlyList<OpenVpnProfile> profiles)
    {
        lock (_cacheLock)
        {
            if (DateTimeOffset.UtcNow - _lastStatePoll < TimeSpan.FromMilliseconds(500))
            {
                return _cachedState;
            }
        }

        var state = ComputeConnectionState(profiles);
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

    private void RefreshState()
    {
        try
        {
            GetConnectionState(FindProfiles());
        }
        catch (Exception)
        {
        }
    }

    private OpenVpnConnectionState ComputeConnectionState(IReadOnlyList<OpenVpnProfile> profiles)
    {
        try
        {
            if (Process.GetProcessesByName("openvpn").Length == 0)
            {
                return new(OpenVpnConnectionStatus.Disconnected, null);
            }

            string? profileDisplayName = null;
            OpenVpnProfile? trackedProfile = null;

            lock (_tempProfileLock)
            {
                trackedProfile = _currentProfile;
            }

            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "OpenVPN",
                "log");
            FileInfo? latestLog;
            if (trackedProfile is not null)
            {
                var trackedTempPath = GetCurrentTempProfilePath();
                var trackedLog = trackedTempPath is null
                    ? null
                    : new FileInfo(Path.Combine(
                        logDirectory,
                        Path.GetFileNameWithoutExtension(trackedTempPath) + ".log"));

                latestLog = trackedLog is not null && trackedLog.Exists
                    ? trackedLog
                    : Directory.Exists(logDirectory)
                        ? Directory.EnumerateFiles(logDirectory, "*.log")
                            .Select(path => new FileInfo(path))
                            .OrderByDescending(file => file.LastWriteTimeUtc)
                            .FirstOrDefault()
                        : null;
                if (latestLog is not null && !latestLog.Exists)
                {
                    latestLog = null;
                }
            }
            else
            {
                latestLog = Directory.Exists(logDirectory)
                    ? Directory.EnumerateFiles(logDirectory, "*.log")
                        .Select(path => new FileInfo(path))
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .FirstOrDefault()
                    : null;
            }

            if (latestLog is null)
            {
                profileDisplayName = trackedProfile?.DisplayName;
                return trackedProfile is null
                    ? new(OpenVpnConnectionStatus.Connected, profileDisplayName)
                    : new(OpenVpnConnectionStatus.Connecting, profileDisplayName);
            }

            if (ReadLogConnectionState(latestLog) == OpenVpnConnectionStatus.Connected)
            {
                profileDisplayName = trackedProfile?.DisplayName ??
                    FindActiveConnectProfile(profiles)?.DisplayName;

                if (profileDisplayName is null)
                {
                    var configName = latestLog.Name[..^latestLog.Extension.Length];
                    var matches = profiles
                        .Where(profile => string.Equals(
                            Path.GetFileNameWithoutExtension(profile.ProfilePath),
                            configName,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    profileDisplayName = matches.Length == 1 ? matches[0].DisplayName : null;
                }
            }
            else
            {
                if (trackedProfile is not null)
                {
                    profileDisplayName = trackedProfile.DisplayName;
                }
                else
                {
                    var activeProfile = FindActiveConnectProfile(profiles);
                    if (activeProfile is not null)
                    {
                        profileDisplayName = activeProfile.DisplayName;
                        return new(OpenVpnConnectionStatus.Connecting, profileDisplayName);
                    }

                    // Only use the log filename when it identifies exactly one profile.
                    var configName = latestLog.Name[..^latestLog.Extension.Length];
                    var matches = profiles
                        .Where(profile => string.Equals(
                            Path.GetFileNameWithoutExtension(profile.ProfilePath),
                            configName,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    profileDisplayName = matches.Length == 1 ? matches[0].DisplayName : null;
                }
            }

            if (ReadLogConnectionState(latestLog) == OpenVpnConnectionStatus.Connected)
            {
                return new(OpenVpnConnectionStatus.Connected, profileDisplayName);
            }

            return new(OpenVpnConnectionStatus.Connecting, profileDisplayName);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return new(OpenVpnConnectionStatus.Disconnected, null);
    }

    private OpenVpnConnectionStatus ReadLogConnectionState(FileInfo logFile)
    {
        try
        {
            var log = ReadLogText(logFile.FullName);
            if (log is null)
            {
                return OpenVpnConnectionStatus.Connecting;
            }

            var connectedIdx = log.LastIndexOf("CONNECTED,SUCCESS", StringComparison.OrdinalIgnoreCase);
            var initIdx = log.LastIndexOf("Initialization Sequence Completed", StringComparison.OrdinalIgnoreCase);
            var exitingIdx = log.LastIndexOf("EXITING", StringComparison.OrdinalIgnoreCase);
            if ((connectedIdx >= 0 && connectedIdx > exitingIdx) ||
                (initIdx >= 0 && initIdx > exitingIdx))
            {
                return OpenVpnConnectionStatus.Connected;
            }

            if (log.Contains("AUTH_FAILED", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("Fatal error", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                return OpenVpnConnectionStatus.Disconnected;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return OpenVpnConnectionStatus.Connecting;
    }

    private static string? ReadLogText(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
        }

        return null;
    }

    private OpenVpnConnectionStatus? ReadManagedLogState(OpenVpnProfile profile)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OVPN_Manager",
            "Logs",
            $"{profile.Uid}.log");
        if (!File.Exists(logPath))
        {
            return null;
        }

        try
        {
            var log = ReadLogText(logPath);
            if (log is null)
            {
                return OpenVpnConnectionStatus.Connecting;
            }

            var connectedIdx = log.LastIndexOf("CONNECTED,SUCCESS", StringComparison.OrdinalIgnoreCase);
            var initIdx = log.LastIndexOf("Initialization Sequence Completed", StringComparison.OrdinalIgnoreCase);
            var exitingIdx = log.LastIndexOf("EXITING", StringComparison.OrdinalIgnoreCase);
            if ((connectedIdx >= 0 && connectedIdx > exitingIdx) ||
                (initIdx >= 0 && initIdx > exitingIdx))
            {
                return OpenVpnConnectionStatus.Connected;
            }

            if (log.Contains("AUTH_FAILED", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("Fatal error", StringComparison.OrdinalIgnoreCase) ||
                log.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                return OpenVpnConnectionStatus.Disconnected;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return OpenVpnConnectionStatus.Connecting;
    }

    public void BeginConnection(OpenVpnProfile profile)
    {
        // For GUI-based connections, state is managed by ComputeConnectionState watching the GUI/logs
        // This method is kept for compatibility but does nothing.
    }

    public void EndConnectionTransition()
    {
        // For GUI-based connections, state is managed by ComputeConnectionState watching the GUI/logs
        // This method is kept for compatibility but does nothing.
    }

    private void SetCachedState(OpenVpnConnectionState state)
    {
        var changed = false;
        lock (_cacheLock)
        {
            if (_cachedState != state)
            {
                _cachedState = state;
                _lastStatePoll = DateTimeOffset.UtcNow;
                changed = true;
            }
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool Connect(OpenVpnProfile profile, string executable, TimeSpan timeout)
    {
        lock (_connectionOperationLock)
        {
            return ConnectCore(profile, executable, timeout);
        }
    }

    private bool ConnectCore(OpenVpnProfile profile, string executable, TimeSpan timeout)
    {
        BeginConnection(profile);

        try
        {
            if (!DisconnectAndWait(timeout))
            {
                return false;
            }

            CleanupTempProfile();

            if (Process.GetProcessesByName("openvpn").Length != 0 ||
                Process.GetProcessesByName("openvpn-gui").Length != 0)
            {
                return false;
            }

            lock (_tempProfileLock)
            {
                _currentTempProfilePath = profile.ProfilePath;
                _currentProfile = profile;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = ConnectProfilesDirectory,
                UseShellExecute = true,
                Arguments = $"--config_dir \"{EscapeArgument(ConnectProfilesDirectory)}\" --connect \"{EscapeArgument(Path.GetFileName(profile.ProfilePath))}\"",
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            EndConnectionTransition();
        }
    }

    private static string EscapeArgument(string value) => value.Replace("\"", "\\\"");

    private static void StopOpenVpnGui(TimeSpan timeout)
    {
        foreach (var gui in Process.GetProcessesByName("openvpn-gui"))
        {
            try
            {
                if (!gui.HasExited)
                {
                    gui.CloseMainWindow();
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                gui.Dispose();
            }
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Min(timeout.TotalMilliseconds, 3000));
        while (DateTime.UtcNow < deadline && Process.GetProcessesByName("openvpn-gui").Length > 0)
        {
            Thread.Sleep(100);
        }

        foreach (var gui in Process.GetProcessesByName("openvpn-gui"))
        {
            try
            {
                if (!gui.HasExited)
                {
                    gui.Kill(entireProcessTree: true);
                    gui.WaitForExit(2000);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                gui.Dispose();
            }
        }
    }

    private string? GetCurrentTempProfilePath()
    {
        lock (_tempProfileLock)
        {
            return _currentTempProfilePath;
        }
    }

    private void CleanupTempProfile()
    {
        lock (_tempProfileLock)
        {
            var tempDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVPN_Manager",
                "Temp");

            if (Directory.Exists(tempDirectory))
            {
                try
                {
                    foreach (var directory in Directory.EnumerateDirectories(tempDirectory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }

                    foreach (var path in Directory.EnumerateFiles(tempDirectory))
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            _currentTempProfilePath = null;
            _currentProfile = null;
        }
    }

    public bool DisconnectAndWait(TimeSpan timeout)
    {
        lock (_connectionOperationLock)
        {
            return DisconnectAndWaitCore(timeout);
        }
    }

    private bool DisconnectAndWaitCore(TimeSpan timeout)
    {
        try
        {
            var gui = FindOpenVpnGui();
            if (gui is not null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = gui,
                        UseShellExecute = true,
                        Arguments = "--command disconnect_all",
                    });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }

            var disconnectDeadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < disconnectDeadline && Process.GetProcessesByName("openvpn").Length > 0)
            {
                Thread.Sleep(150);
            }

            StopOpenVpnGui(timeout);

            Process[] processes = Process.GetProcessesByName("openvpn");
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    {
                        process.CloseMainWindow();
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Min(timeout.TotalMilliseconds, 5000));
            while (DateTime.UtcNow < deadline && Process.GetProcessesByName("openvpn").Length > 0)
            {
                Thread.Sleep(150);
            }

            foreach (var process in Process.GetProcessesByName("openvpn"))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2000);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            StopOpenVpnGui(timeout);
            return Process.GetProcessesByName("openvpn").Length == 0 &&
                   Process.GetProcessesByName("openvpn-gui").Length == 0;
        }
        finally
        {
            CleanupTempProfile();
        }
    }

    private static List<OpenVpnProfile> ReadProfilesFromConnectConfig()
    {
        var result = new List<OpenVpnProfile>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ConnectConfigPath));
            if (!TryGetJsonObject(document.RootElement, "persist:root", out var persistedRoot) ||
                !TryGetJsonObject(persistedRoot, "status", out var status) ||
                !status.TryGetProperty("profiles", out var profiles) ||
                profiles.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var path = Path.Combine(ConnectProfilesDirectory, $"{profile.Name}.ovpn");
                if (!File.Exists(path))
                {
                    continue;
                }

                var displayName = GetString(profile.Value, "profileDisplayName") ??
                                  GetString(profile.Value, "profileName") ??
                                  Path.GetFileNameWithoutExtension(path);
                result.Add(new(profile.Name, displayName, path));
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return result;
    }

    private static OpenVpnProfile? FindActiveConnectProfile(IReadOnlyList<OpenVpnProfile> profiles)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ConnectConfigPath));
            if (!TryGetJsonObject(document.RootElement, "persist:root", out var persistedRoot) ||
                !TryGetJsonObject(persistedRoot, "status", out var status))
            {
                return null;
            }

            var reference = FindProfileReference(status);
            if (reference is null)
            {
                return null;
            }

            return profiles.FirstOrDefault(profile =>
                string.Equals(profile.Uid, reference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.DisplayName, reference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.ProfilePath, reference, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindProfileReference(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            var propertyName = property.Name.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace(":", string.Empty, StringComparison.Ordinal);
            if (propertyName.Contains("activeprofile", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Contains("connectedprofile", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Contains("currentprofile", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    return GetString(property.Value, "uid") ??
                           GetString(property.Value, "id") ??
                           GetString(property.Value, "profileId") ??
                           GetString(property.Value, "filePath") ??
                           GetString(property.Value, "profileDisplayName");
                }
            }

            var nested = FindProfileReference(property.Value);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool TryGetJsonObject(JsonElement parent, string propertyName, out JsonElement value)
    {
        value = default;
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(property.GetString() ?? string.Empty);
            value = document.RootElement.Clone();
            return value.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}