using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

internal sealed partial class VPN_ManagerPage : ListPage
{
    private readonly OpenVpnProfileService _profileService;
    private readonly CloudflareWarpService _warpService;
    private readonly PublicIpService _ipService;
    private readonly VpnConnectionManager _vpnManager;

    public VPN_ManagerPage(
        OpenVpnProfileService profileService,
        CloudflareWarpService warpService,
        PublicIpService ipService,
        VpnConnectionManager vpnManager)
    {
        _profileService = profileService;
        _warpService = warpService;
        _ipService = ipService;
        _vpnManager = vpnManager;

        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "VPN Manager";
        Name = "Profiles";
        PlaceholderText = "Search VPN profiles and actions";

        _profileService.StateChanged += OnStateChanged;
        _warpService.StateChanged += OnStateChanged;
        _ipService.IpChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e) => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();

        // 1. Current Public IP item (click to copy / refresh)
        var ip = _ipService.CurrentIp;
        items.Add(new ListItem(new RefreshOrCopyIpCommand(_ipService))
        {
            Title = $"Public IP: {ip}",
            Subtitle = "Click to copy IP to clipboard and refresh",
            Icon = new IconInfo("\uE774"), // Globe icon
        });

        // 2. Disconnect All item (Disconnects both OpenVPN & WARP)
        items.Add(new ListItem(new OpenVpnDisconnectCommand(_vpnManager))
        {
            Title = "Disconnect all",
            Subtitle = "Disconnect any active VPN connection (OpenVPN or Cloudflare WARP)",
            Icon = new IconInfo("\uE711"),
        });

        // 3. Cloudflare WARP Section
        var warpState = _warpService.GetConnectionState();
        var warpCliInstalled = CloudflareWarpService.FindWarpCli() is not null;
        if (warpCliInstalled)
        {
            var isWarpConnected = warpState.Status == WarpConnectionStatus.Connected;
            var isWarpConnecting = warpState.Status == WarpConnectionStatus.Connecting;

            items.Add(new ListItem(new WarpToggleCommand(_vpnManager, connect: !isWarpConnected))
            {
                Title = $"Cloudflare WARP: {(isWarpConnected ? "Connected" : isWarpConnecting ? "Connecting..." : "Disconnected")}",
                Subtitle = isWarpConnected ? "Click to disconnect WARP" : "Click to connect WARP (disconnects others)",
                Icon = new IconInfo(isWarpConnected ? "\uE73E" : isWarpConnecting ? "\uE895" : "\uE711"),
            });
        }

        // 4. OpenVPN Profiles Section
        var profiles = _profileService.FindProfiles();
        var state = _profileService.GetConnectionState(profiles);

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = OpenVpnStatusPresentation.PageTitle(state),
            Subtitle = OpenVpnStatusPresentation.PageSubtitle(state.Status),
            Icon = new IconInfo(OpenVpnStatusPresentation.Icon(state.Status)),
        });

        if (_profileService.FindOpenVpnGui() is null)
        {
            items.Add(new ListItem(new OpenVpnInstallCommand())
            {
                Title = "Install OpenVPN Community",
                Subtitle = "Open the official OpenVPN download page",
                Icon = new IconInfo("\uE71D"),
            });
        }

        items.AddRange(profiles.Select(profile => new ListItem(new OpenVpnConnectCommand(_vpnManager, profile))
        {
            Title = profile.DisplayName,
            Subtitle = state.ProfileDisplayName == profile.DisplayName && state.Status == OpenVpnConnectionStatus.Connecting
                ? "Connecting"
                : state.ProfileDisplayName == profile.DisplayName && state.IsConnected
                    ? "Connected"
                    : "Connect (disconnects any active VPN)",
            Icon = new IconInfo(state.ProfileDisplayName == profile.DisplayName
                ? OpenVpnStatusPresentation.Icon(state.Status)
                : "\uE774"),
        }));

        return items.ToArray();
    }
}
