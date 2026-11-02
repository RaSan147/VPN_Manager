using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace OVPN_Manager;

internal sealed class OpenVpnConnectCommand : InvokableCommand
{
    private readonly OpenVpnProfileService _profileService;
    private readonly OpenVpnProfile _profile;

    public OpenVpnConnectCommand(OpenVpnProfileService profileService, OpenVpnProfile profile)
    {
        _profileService = profileService;
        _profile = profile;
        Id = $"connect-{profile.Uid}";
        Name = $"Connect to {profile.DisplayName}";
    }

    public override ICommandResult Invoke()
    {
        try
        {
            var executable = _profileService.FindOpenVpnGui();
            if (executable is null)
            {
                return CommandResult.GoBack();
            }

            return _profileService.Connect(_profile, executable, TimeSpan.FromSeconds(10))
                ? CommandResult.KeepOpen()
                : CommandResult.GoBack();
        }
        catch (Exception)
        {
            return CommandResult.GoBack();
        }
    }
}
