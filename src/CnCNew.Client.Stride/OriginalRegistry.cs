using Microsoft.Win32;

namespace CnCNew.Client.Stride;

/// <summary>
/// The values the original reads from its own registry key (<c>registry.cpp</c>: <c>SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour</c>, first under
/// the local machine, then under the current user). The original is a 32-bit program, so its machine key is the 32-bit view. A missing key or value is the original's default.
/// </summary>
internal static class OriginalRegistry
{
    private const string KeyPath = @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour";

    /// <summary><c>GetUnsignedIntFromRegistry("", "Preorder")</c> is not zero: the skirmish marks the local player as one who pre-ordered the game.</summary>
    public static bool Preordered()
    {
        try
        {
            return (UnsignedInt(RegistryHive.LocalMachine) ?? UnsignedInt(RegistryHive.CurrentUser) ?? 0) != 0;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static long? UnsignedInt(RegistryHive hive)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32);
        using var key = root.OpenSubKey(KeyPath);
        return key?.GetValue("Preorder") is int value ? (uint)value : null;
    }
}
