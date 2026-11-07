using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

internal sealed partial class RefreshOrCopyIpCommand : InvokableCommand
{
    private readonly PublicIpService _ipService;

    public RefreshOrCopyIpCommand(PublicIpService ipService)
    {
        _ipService = ipService;
        Name = "Copy Public IP";
    }

    public override ICommandResult Invoke()
    {
        var ip = _ipService.CurrentIp;
        if (!string.IsNullOrWhiteSpace(ip) && !ip.Contains("Checking", System.StringComparison.OrdinalIgnoreCase))
        {
            Win32Clipboard.SetText(ip);
        }

        _ = _ipService.RefreshIpAsync(force: true);
        return CommandResult.KeepOpen();
    }

    /// <summary>
    /// Sets clipboard text using Win32 APIs directly.
    /// WinRT Clipboard requires an STA thread with a message pump, which is not guaranteed
    /// in an out-of-process Command Palette extension. Win32 OpenClipboard works from any thread.
    /// </summary>
    private static class Win32Clipboard
    {
        private const uint CF_UNICODETEXT = 13;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        private const uint GMEM_MOVEABLE = 0x0002;

        public static void SetText(string text)
        {
            // Allocate global memory for the text (UTF-16, null-terminated)
            var byteCount = (text.Length + 1) * 2;
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var ptr = GlobalLock(hGlobal);
                if (ptr == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                    Marshal.WriteInt16(ptr, text.Length * 2, 0); // null terminator
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                if (!OpenClipboard(IntPtr.Zero))
                {
                    GlobalFree(hGlobal);
                    return;
                }

                try
                {
                    EmptyClipboard();
                    var result = SetClipboardData(CF_UNICODETEXT, hGlobal);
                    // If SetClipboardData succeeds, the system owns hGlobal — do NOT free it.
                    if (result == IntPtr.Zero)
                    {
                        GlobalFree(hGlobal);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch
            {
                try { GlobalFree(hGlobal); } catch { }
            }
        }
    }
}
