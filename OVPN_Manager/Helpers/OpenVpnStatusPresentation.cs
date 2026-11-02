namespace OVPN_Manager;

internal static class OpenVpnStatusPresentation
{
    public static string Icon(OpenVpnConnectionStatus status) => status switch
    {
        OpenVpnConnectionStatus.Connecting => "\uE895",
        OpenVpnConnectionStatus.Connected => "\uE73E",
        _ => "\uE711",
    };

    public static string PageTitle(OpenVpnConnectionState state) => state.Status switch
    {
        OpenVpnConnectionStatus.Connecting => $"Connecting: {state.ProfileDisplayName ?? "profile"}",
        OpenVpnConnectionStatus.Connected => $"Connected: {state.ProfileDisplayName ?? "profile"}",
        _ => "OVPN disconnected",
    };

    public static string PageSubtitle(OpenVpnConnectionStatus status) => status switch
    {
        OpenVpnConnectionStatus.Connecting => "Connection in progress",
        OpenVpnConnectionStatus.Connected => "Active connection",
        _ => "Select a profile to connect",
    };

    public static string DockTitle(OpenVpnConnectionState state) => state.Status switch
    {
        OpenVpnConnectionStatus.Connecting => $"OVPN: Connecting {state.ProfileDisplayName ?? "profile"}",
        OpenVpnConnectionStatus.Connected => $"{state.ProfileDisplayName ?? "Connected"} • OVPN",
        _ => "OVPN: Off",
    };
}
