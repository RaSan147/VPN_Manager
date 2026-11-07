using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

internal sealed partial class OpenVpnDisconnectCommand : InvokableCommand
{
    private readonly VpnConnectionManager _vpnManager;

    public OpenVpnDisconnectCommand(VpnConnectionManager vpnManager)
    {
        _vpnManager = vpnManager;
        Id = "com.alonedeveloper.vpnmanager.disconnectall";
        Name = "Disconnect all VPNs";
    }

    public override ICommandResult Invoke()
    {
        try
        {
            _vpnManager.DisconnectAll();
            return CommandResult.KeepOpen();
        }
        catch (Exception)
        {
            return CommandResult.GoBack();
        }
    }
}
