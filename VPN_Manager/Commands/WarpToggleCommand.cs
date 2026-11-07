using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

internal sealed partial class WarpToggleCommand : InvokableCommand
{
    private readonly VpnConnectionManager _vpnManager;
    private readonly bool _connect;

    public WarpToggleCommand(VpnConnectionManager vpnManager, bool connect)
    {
        _vpnManager = vpnManager;
        _connect = connect;
        Id = connect ? "com.alonedeveloper.vpnmanager.warp.connect" : "com.alonedeveloper.vpnmanager.warp.disconnect";
        Name = connect ? "Connect Cloudflare WARP" : "Disconnect Cloudflare WARP";
    }

    public override ICommandResult Invoke()
    {
        try
        {
            if (_connect)
            {
                _ = _vpnManager.ConnectWarpAsync();
            }
            else
            {
                _ = _vpnManager.DisconnectWarpAsync();
            }

            return CommandResult.KeepOpen();
        }
        catch (Exception)
        {
            return CommandResult.GoBack();
        }
    }
}
