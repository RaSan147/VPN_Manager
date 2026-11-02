// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;

namespace OVPN_Manager;

public partial class OVPN_ManagerCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly SettingsManager _settingsManager = new();
    private readonly OpenVpnProfileService _profileService = new();
    private readonly OVPN_ManagerPage _mainPage;

    public OVPN_ManagerCommandsProvider()
    {
        DisplayName = "OVPN Manager";
        Id = "com.alonedeveloper.ovpnmanager";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _mainPage = new OVPN_ManagerPage(_profileService);
        _profileService.StateChanged += OnStateChanged;
        _commands = [
            new CommandItem(_mainPage)
            {
                Title = DisplayName,
                MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
            },
        ];

        Settings = _settingsManager.Settings;
    }

    private void OnStateChanged(object? sender, EventArgs e) => RaiseItemsChanged();

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    public override ICommandItem[]? GetDockBands()
    {
        var profiles = _profileService.FindProfiles();
        var state = _profileService.GetConnectionState(profiles);
        var dockItem = new ListItem(_mainPage)
        {
            Title = OpenVpnStatusPresentation.DockTitle(state),
            Icon = new IconInfo(OpenVpnStatusPresentation.Icon(state.Status)),
        };

        return [
            new WrappedDockItem([dockItem], "com.alonedeveloper.ovpnmanager.status", "OVPN"),
        ];
    }
}
