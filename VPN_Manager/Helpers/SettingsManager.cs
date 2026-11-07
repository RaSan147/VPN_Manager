// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VPN_Manager;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    private static readonly string _namespace = "VPN_Manager";

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    internal static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("VPN_Manager");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        // TODO: Register your settings here. For example:
        // Settings.Add(_myToggle);

        // Load settings from file upon initialization
        LoadSettings();

        Settings.SettingsChanged += (_, _) => SaveSettings();
    }
}
