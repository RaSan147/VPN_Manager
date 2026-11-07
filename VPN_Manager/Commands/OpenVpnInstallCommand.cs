using System.Diagnostics;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

internal sealed class OpenVpnInstallCommand : InvokableCommand
{
    public OpenVpnInstallCommand()
    {
        Id = "install-community-gui";
        Name = "Install OpenVPN Community GUI";
    }

    public override ICommandResult Invoke()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://openvpn.net/community-downloads/",
            UseShellExecute = true,
        });

        return CommandResult.KeepOpen();
    }
}