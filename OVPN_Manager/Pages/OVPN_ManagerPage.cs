using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace OVPN_Manager;

internal sealed partial class OVPN_ManagerPage : ListPage
{
    private readonly OpenVpnProfileService _profileService;

    public OVPN_ManagerPage(OpenVpnProfileService profileService)
    {
        _profileService = profileService;
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "OVPN Manager";
        Name = "Profiles";
        PlaceholderText = "Search OpenVPN profiles";
        _profileService.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e) => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var profiles = _profileService.FindProfiles();
        var state = _profileService.GetConnectionState(profiles);
        var items = new List<IListItem>
        {
            new ListItem(new NoOpCommand())
            {
                Title = OpenVpnStatusPresentation.PageTitle(state),
                Subtitle = OpenVpnStatusPresentation.PageSubtitle(state.Status),
                Icon = new IconInfo(OpenVpnStatusPresentation.Icon(state.Status)),
            },
        };

        if (_profileService.FindOpenVpnGui() is null)
        {
            items.Add(new ListItem(new OpenVpnInstallCommand())
            {
                Title = "Install OpenVPN Community",
                Subtitle = "Open the official OpenVPN download page",
                Icon = new IconInfo("\uE71D"),
            });
        }
        else
        {
            items.Add(new ListItem(new OpenVpnDisconnectCommand(_profileService))
            {
                Title = "Disconnect all",
                Subtitle = "Stop active OpenVPN connections",
                Icon = new IconInfo("\uE711"),
            });
        }

        items.AddRange(profiles.Select(profile => new ListItem(new OpenVpnConnectCommand(_profileService, profile))
        {
            Title = profile.DisplayName,
            Subtitle = state.ProfileDisplayName == profile.DisplayName && state.Status == OpenVpnConnectionStatus.Connecting
                ? "Connecting"
                : state.ProfileDisplayName == profile.DisplayName && state.IsConnected
                    ? "Connected"
                    : "Connect",
            Icon = new IconInfo(state.ProfileDisplayName == profile.DisplayName
                ? OpenVpnStatusPresentation.Icon(state.Status)
                : "\uE774"),
        }));

        return items.ToArray();
    }
}