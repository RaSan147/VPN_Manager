using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

/// <summary>
/// ListPage used exclusively as the dock band.
/// Each GetItems() entry renders as one dock button in the band:
///   Button 1 → opens the full VPN Manager page (profiles, WARP, etc.)
///   Button 2 → VPN status / quick disconnect, with WARP toggle in MoreCommands
///   Button 3 → shows public IP / click to copy
///
/// Calls RaiseItemsChanged() on itself when VPN state changes —
/// never triggers a full dock teardown/rebuild, so no flash.
/// </summary>
internal sealed partial class VPN_ManagerDockPage : ListPage
{
    private readonly OpenVpnProfileService _profileService;
    private readonly CloudflareWarpService _warpService;
    private readonly PublicIpService _ipService;
    private readonly VpnConnectionManager _vpnManager;

    // Stable references — created once, reused every GetItems() call.
    private readonly VPN_ManagerPage _mainPage;
    private readonly OpenVpnDisconnectCommand _disconnectAllCmd;
    private readonly RefreshOrCopyIpCommand _refreshIpCmd;

    public VPN_ManagerDockPage(
        OpenVpnProfileService profileService,
        CloudflareWarpService warpService,
        PublicIpService ipService,
        VpnConnectionManager vpnManager,
        VPN_ManagerPage mainPage)
    {
        _profileService = profileService;
        _warpService = warpService;
        _ipService = ipService;
        _vpnManager = vpnManager;
        _mainPage = mainPage;

        Id = "com.alonedeveloper.vpnmanager.dockband";
        Name = "VPN Status";
        Title = "VPN Status";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        // Pre-build stable command instances once.
        _disconnectAllCmd = new OpenVpnDisconnectCommand(_vpnManager);
        _refreshIpCmd = new RefreshOrCopyIpCommand(_ipService);

        // Only this page listens to state changes.
        // RaiseItemsChanged() on a ListPage only refreshes its items list —
        // it does NOT tear down the dock band structure, so there's no flash.
        _profileService.StateChanged += OnStateChanged;
        _warpService.StateChanged += OnStateChanged;
        _ipService.IpChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e) => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var profiles = _profileService.FindProfiles();
        var ovpnState = _profileService.GetConnectionState(profiles);
        var warpState = _warpService.GetConnectionState();

        // --- Button 1: open the full VPN Manager (profiles list, WARP toggle, etc.) ---
        // Wrapping a ListPage in a ListItem makes the dock button open it as a nested page.
        var (vpnTitle, vpnIcon) = VpnDockPresentation.Status(ovpnState, warpState);

        // Opens the full VPN Manager page (with all profiles) on click.
        var openManagerItem = new ListItem(_mainPage)
        {
            Title = vpnTitle,
            Subtitle = "Open VPN Manager",
            Icon = new IconInfo(vpnIcon),
        };

        // MoreCommands: quick actions without opening the full page.
        var isWarpConnected = warpState.Status == WarpConnectionStatus.Connected;
        openManagerItem.MoreCommands =
        [
            new CommandContextItem(_disconnectAllCmd)
            {
                Title = "Disconnect All",
                Icon = new IconInfo("\uE711"),
            },
            new CommandContextItem(new WarpToggleCommand(_vpnManager, connect: !isWarpConnected))
            {
                Title = isWarpConnected ? "Disconnect WARP" : "Connect WARP",
                Icon = new IconInfo(isWarpConnected ? "\uE711" : "\uE73E"),
            },
        ];

        // --- Button 2: public IP / click to copy ---
        var ipItem = new ListItem(_refreshIpCmd)
        {
            Title = $"IP: {_ipService.CurrentIp}",
            Subtitle = "Click to copy IP",
            Icon = new IconInfo("\uE774"),
        };

        return [openManagerItem, ipItem];
    }
}
