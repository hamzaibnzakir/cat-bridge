using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace DynamicIsland.FaceId;

/// <summary>
/// Shared constants and the saved Windows credential for the sign-in tile.
/// The password is DPAPI-encrypted (machine scope) in a file only SYSTEM and Administrators can open.
/// The format is mirrored in FaceSignIn/CredStore.cs (.NET Framework), so keep both in sync.
/// </summary>
public static class SignInSecret
{
    public const string ProviderClsid = "7a4a5c8b-1a84-498f-9bf5-d0fcffc94a1e";
    public const string ProviderName = "Dynamic Island Cat Face";
    public const string ProviderKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{" + ProviderClsid + "}";

    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DynamicIsland", "FaceSignIn");
    public static string SecretPath => Path.Combine(DataDir, "cred.bin");
    public static string ConfigPath => Path.Combine(DataDir, "config.txt");
    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DynamicIsland", "FaceSignIn");

    static readonly byte[] MachineEntropy = Encoding.UTF8.GetBytes("DynamicIsland.FaceSignIn.v1");
    static readonly byte[] HandoffEntropy = Encoding.UTF8.GetBytes("DynamicIsland.FaceSignIn.handoff");

    public static string Encode(string sid, string user, string password) =>
        "v1\n" + sid + "\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(user)) + "\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(password));

    /// The app (not elevated) hands the secret to the elevated installer via a user-only, user-encrypted temp file.
    public static string WriteHandoff(string sid, string user, string password)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "signin-handoff.bin");
        File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(Encode(sid, user, password)), HandoffEntropy, DataProtectionScope.CurrentUser));
        return path;
    }

    public static string ReadHandoff(string path)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), HandoffEntropy, DataProtectionScope.CurrentUser)); }
        finally { try { File.Delete(path); } catch { } }
    }

    /// Elevated: store for the sign-in screen (SYSTEM + Administrators only).
    public static void WriteMachineSecret(string encoded)
    {
        Directory.CreateDirectory(DataDir);
        var tmp = SecretPath + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(Encoding.UTF8.GetBytes(encoded), MachineEntropy, DataProtectionScope.LocalMachine));
        var sec = new FileSecurity();
        sec.SetAccessRuleProtection(true, false);
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(tmp).SetAccessControl(sec);
        File.Move(tmp, SecretPath, overwrite: true);
    }
}
