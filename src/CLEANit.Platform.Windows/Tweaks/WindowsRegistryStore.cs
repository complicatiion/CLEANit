using CLEANit.Core.Tweaks;
using Microsoft.Win32;

namespace CLEANit.Platform.Windows;

/// <summary>Registry access for the documented Windows settings (64-bit view, DWORD values only).</summary>
public sealed class WindowsRegistryStore : IRegistryStore
{
    public int? Read(RegHive hive, string key, string name)
    {
        try
        {
            using var k = Root(hive).OpenSubKey(key);
            return k?.GetValueKind(name) == RegistryValueKind.DWord && k.GetValue(name) is int value ? value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool Exists(RegHive hive, string key, string name)
    {
        try
        {
            using var k = Root(hive).OpenSubKey(key);
            return k?.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase) == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Write(RegHive hive, string key, string name, int value)
    {
        using var k = Root(hive).CreateSubKey(key, writable: true);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void Delete(RegHive hive, string key, string name)
    {
        using var k = Root(hive).OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static RegistryKey Root(RegHive hive) =>
        RegistryKey.OpenBaseKey(hive == RegHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
}
