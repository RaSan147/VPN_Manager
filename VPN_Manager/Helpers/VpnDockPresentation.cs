namespace VPN_Manager;

/// <summary>
/// Single source of truth for the VPN status shown on the dock band.
/// Takes the same state objects the full VPN Manager page uses, so the dock and page can't disagree.
/// </summary>
internal static class VpnDockPresentation
{
    public static (string Title, string Icon) Status(OpenVpnConnectionState ovpn, WarpConnectionState warp)
    {
        if (ovpn.IsConnected)
        {
            return ($"{ovpn.ProfileDisplayName ?? "VPN"} \u2022 On", "\uE73E"); // checkmark
        }

        if (ovpn.Status == OpenVpnConnectionStatus.Connecting)
        {
            return ("OpenVPN: Connecting\u2026", "\uE895"); // sync
        }

        if (warp.Status == WarpConnectionStatus.Connected)
        {
            return ("WARP \u2022 On", "\uE73E");
        }

        if (warp.Status == WarpConnectionStatus.Connecting)
        {
            return ("WARP: Connecting\u2026", "\uE895");
        }

        return ("VPN: Off", "\uE711"); // cross
    }
}
