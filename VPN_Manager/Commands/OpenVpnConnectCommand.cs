using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

internal sealed partial class OpenVpnConnectCommand : InvokableCommand
{
    private readonly VpnConnectionManager _vpnManager;
    private readonly OpenVpnProfile _profile;

    public OpenVpnConnectCommand(VpnConnectionManager vpnManager, OpenVpnProfile profile)
    {
        _vpnManager = vpnManager;
        _profile = profile;
        Id = $"connect-{profile.Uid}";
        Name = $"Connect to {profile.DisplayName}";
    }

    public override ICommandResult Invoke()
    {
        try
        {
            return _vpnManager.ConnectOpenVpn(_profile, TimeSpan.FromSeconds(10))
                ? CommandResult.KeepOpen()
                : CommandResult.GoBack();
        }
        catch (Exception)
        {
            return CommandResult.GoBack();
        }
    }
}
