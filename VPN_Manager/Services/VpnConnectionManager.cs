using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

internal sealed class VpnConnectionManager
{
    private static readonly object _syncLock = new();

    
    // Delay before re-probing the public IP after a connection state transition.
    // Long enough for routes/DNS to settle, and to let any fetch that was already
    // in flight from NetworkAddressChanged complete (otherwise the _isFetching
    // guard in PublicIpService would silently drop this one).
    private static readonly TimeSpan IpRefreshDelay = TimeSpan.FromSeconds(8);


    private readonly OpenVpnProfileService _openVpnService;
    private readonly CloudflareWarpService _warpService;
    private readonly PublicIpService _ipService;
    

    public VpnConnectionManager(
        OpenVpnProfileService openVpnService, 
        CloudflareWarpService warpService,
        PublicIpService ipService)
    {
        _openVpnService = openVpnService;
        _warpService = warpService;
        _ipService = ipService;

        // Network adapter events fire too early (adapter created, routes not yet
        // installed) and the OpenVPN "CONNECTED,SUCCESS" log marker doesn't raise
        // any network event at all. Hook the state machines directly so we refresh
        // the IP exactly when the tunnel has actually come up or gone down.
        _openVpnService.StateChanged += OnOpenVpnStateChanged;
        _warpService.StateChanged += OnWarpStateChanged;
    }

    
    private void OnOpenVpnStateChanged(object? sender, EventArgs e)
    {
        var profiles = _openVpnService.FindProfiles();
        var state = _openVpnService.GetConnectionState(profiles);

        // Only refresh on fully-connected / fully-disconnected transitions.
        // "Connecting" doesn't change the egress IP.
        if (state.IsConnected || state.Status == OpenVpnConnectionStatus.Disconnected)
        {
            ScheduleIpRefresh();
        }
    }

    private void OnWarpStateChanged(object? sender, EventArgs e)
    {
        var state = _warpService.GetConnectionState();
        if (state.Status == WarpConnectionStatus.Connected ||
            state.Status == WarpConnectionStatus.Disconnected)
        {
            ScheduleIpRefresh();
        }
    }

    private void ScheduleIpRefresh()
    {
        // 1 second is enough now that we cancel in-flight requests and drop pooled sockets
        _ = Task.Delay(TimeSpan.FromSeconds(1))
            .ContinueWith(_ => _ipService.RefreshIpAsync(force: true), TaskScheduler.Default)
            .Unwrap();
    }


    /// <summary>
    /// Disconnects all active VPNs (both OpenVPN and Cloudflare WARP).
    /// </summary>
    public bool DisconnectAll()
    {
        lock (_syncLock)
        {
            // 1. Disconnect WARP
            _warpService.DisconnectSync(TimeSpan.FromSeconds(5));

            // 2. Disconnect OpenVPN
            var openVpnDisconnected = _openVpnService.DisconnectAndWait(TimeSpan.FromSeconds(10));

            _warpService.RefreshState();
            return openVpnDisconnected;
        }
    }

    /// <summary>
    /// Connects to a specific OpenVPN profile, ensuring all other VPNs (OpenVPN + Cloudflare WARP)
    /// are completely disconnected first.
    /// </summary>
    public bool ConnectOpenVpn(OpenVpnProfile profile, TimeSpan timeout)
    {
        lock (_syncLock)
        {
            var executable = _openVpnService.FindOpenVpnGui();
            if (executable is null)
            {
                return false;
            }

            // Ensure WARP is disconnected first to prevent routing and subnet collision
            _warpService.DisconnectSync(TimeSpan.FromSeconds(5));

            // Connect OpenVPN (internally calls DisconnectAndWait)
            var success = _openVpnService.Connect(profile, executable, timeout);

            _warpService.RefreshState();
            return success;
        }
    }

    /// <summary>
    /// Connects Cloudflare WARP, ensuring OpenVPN is completely disconnected first.
    /// </summary>
    public async Task<bool> ConnectWarpAsync()
    {
        // Disconnect OpenVPN first to prevent overlapping tunnel drivers / routing conflicts
        await Task.Run(() =>
        {
            lock (_syncLock)
            {
                _openVpnService.DisconnectAndWait(TimeSpan.FromSeconds(10));
            }
        });

        return await _warpService.ConnectAsync();
    }

    /// <summary>
    /// Disconnects Cloudflare WARP.
    /// </summary>
    public async Task<bool> DisconnectWarpAsync()
    {
        return await _warpService.DisconnectAsync();
    }
}
