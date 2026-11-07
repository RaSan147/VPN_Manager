// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

public partial class VPN_ManagerCommandsProvider : CommandProvider, IDisposable
{
    private readonly SettingsManager _settingsManager = new();
    private readonly OpenVpnProfileService _profileService = new();
    private readonly CloudflareWarpService _warpService = new();
    private readonly PublicIpService _ipService = new();
    private readonly VpnConnectionManager _vpnManager;

    // All stable objects — built once in the constructor, returned by reference forever.
    private readonly VPN_ManagerPage _mainPage;
    private readonly VPN_ManagerDockPage _dockPage;
    private readonly CommandItem _topLevelCommand;
    private readonly CommandItem _dockBand;

    private bool _disposed;

    public VPN_ManagerCommandsProvider()
    {
        DisplayName = "VPN Manager";
        Id = "com.alonedeveloper.vpnmanager";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _vpnManager = new VpnConnectionManager(_profileService, _warpService, _ipService);
        _mainPage = new VPN_ManagerPage(_profileService, _warpService, _ipService, _vpnManager);

        // The dock page is a ListPage — each GetItems() entry becomes a dock button.
        // It subscribes to state changes itself and calls RaiseItemsChanged() on itself,
        // so only the dock items are re-fetched. No full dock teardown/rebuild = no flash.
        _dockPage = new VPN_ManagerDockPage(_profileService, _warpService, _ipService, _vpnManager, _mainPage);

        _topLevelCommand = new CommandItem(_mainPage)
        {
            Title = DisplayName,
            MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
        };

        // Wrap the dock ListPage in a stable CommandItem — same object returned every call.
        _dockBand = new CommandItem(_dockPage)
        {
            Title = DisplayName,
            Icon = Icon,
        };

        Settings = _settingsManager.Settings;

        // Kick off the initial IP fetch now that both pages have subscribed to IpChanged,
        // so neither misses the event when the IP resolves for the first time.
        _ipService.StartAsync();
    }

    public override ICommandItem[] TopLevelCommands() => [_topLevelCommand];

    // Returns the same stable object every time — no object churn, no flash.
    public override ICommandItem[]? GetDockBands() => [_dockBand];

    public override void Dispose()
    {
        base.Dispose();

        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _warpService.Dispose();
        _ipService.Dispose();
    }
}
