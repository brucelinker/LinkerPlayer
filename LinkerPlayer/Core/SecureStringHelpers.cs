using System.Security.Cryptography;
using System.Text;

namespace LinkerPlayer.Core;

public static class SecureStringHelpers
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LinkerPlayer-Secure-MusicBrainz-2026-v1");

    public static string Protect(this string? clearText)
    {
        if (string.IsNullOrEmpty(clearText))
            return string.Empty;

        try
        {
            byte[] clearBytes = Encoding.UTF8.GetBytes(clearText);
            byte[] encryptedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string Unprotect(this string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        try
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
            byte[] clearBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}
