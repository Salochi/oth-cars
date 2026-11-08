using System;
using System.Security.Cryptography;

namespace OverTheHillPlus.Core.Install;

/// <summary>
/// The release signing key (ECDSA P-256, SHA-256) and the check of a mods.json against its mods.json.sig. Only the
/// mod author holds the private key, so nobody else can publish a mod list this accepts. Pure logic so it can be unit tested.
/// </summary>
internal static class ReleaseKey
{
    /// <summary>Public key, big-endian coordinates (hex). Salochi fork: the Salochi release key (the launcher repo's
    /// tools/sign.cs makes and uses it).</summary>
    public const string X = "dbe096f23d5a5bd0fd325e0db6dc4e4a6e25b0d8b66b906a00084d90c4e6cb04";
    public const string Y = "22100439c1b7da663b93ea0e5c29ef25a4d5d1d5fb2feabce2fcae93a5fda109";

    /// <summary>What the player sees when the list fails the check.</summary>
    public const string NotSignedMessage = "The mod list from GitHub isn't signed by the mod author, so nothing was installed.";

    /// <summary>True when <paramref name="sigBase64"/> (base64 of the 64-byte r||s) is a valid signature of <paramref name="data"/> by the release key.</summary>
    public static bool Verify(byte[] data, string sigBase64) => Verify(data, sigBase64, X, Y);

    public static bool Verify(byte[] data, string sigBase64, string xHex, string yHex)
    {
        if (data == null || string.IsNullOrWhiteSpace(sigBase64)) return false;
        try
        {
            var sig = Convert.FromBase64String(sigBase64.Trim());
            if (sig.Length != 64) return false;
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = { X = Hex(xHex), Y = Hex(yHex) },
            });
            return ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256);
        }
        catch (Exception) { return false; } // bad base64, bad key, platform error: not verified
    }

    private static byte[] Hex(string s)
    {
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }
}
