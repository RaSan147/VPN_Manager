using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace OVPN_Manager;

internal sealed class OpenVpnDisconnectCommand : InvokableCommand
{
    private readonly OpenVpnProfileService _profileService;

    public OpenVpnDisconnectCommand(OpenVpnProfileService profileService)
    {
        _profileService = profileService;
        Id = "disconnect";
        Name = "Disconnect OpenVPN";
    }

    public override ICommandResult Invoke()
    {
        try
        {
            _profileService.DisconnectAndWait(TimeSpan.FromSeconds(10));
            return CommandResult.KeepOpen();
        }
        catch (Exception)
        {
            return CommandResult.GoBack();
        }
    }
}
