using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace DynamicIsland.FaceId;

/// <summary>
/// Your face template, never a photo: 11 numeric fingerprints (128 numbers each).
/// Encrypted with Windows DPAPI (machine scope, so the sign-in screen can read it in step 2), stored in
/// ProgramData with access limited to you, Administrators and SYSTEM. It never leaves this PC.
/// </summary>
public static class FaceStore
{
    public sealed class Data
    {
        public int Version { get; set; } = 1;
        public string User { get; set; } = "";
        public string Sid { get; set; } = "";
        public DateTime Created { get; set; }
        public List<float[]> Embeddings { get; set; } = new();
    }

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DynamicIsland", "FaceId");
    static string FilePath => Path.Combine(Dir, "face.bin");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DynamicIsland.FaceId.v1");

    public static bool Exists => File.Exists(FilePath);

    public static void Save(List<float[]> embeddings)
    {
        var id = WindowsIdentity.GetCurrent();
        var d = new Data { User = id.Name, Sid = id.User?.Value ?? "", Created = DateTime.Now, Embeddings = embeddings };
        Directory.CreateDirectory(Dir);
        var enc = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(d), Entropy, DataProtectionScope.LocalMachine);
        var tmp = FilePath + ".tmp";
        File.WriteAllBytes(tmp, enc);
        Restrict(tmp, id);
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static Data? Load()
    {
        try
        {
            if (!Exists) return null;
            var json = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.LocalMachine);
            return JsonSerializer.Deserialize<Data>(json);
        }
        catch (Exception ex) { FaceLog.Write(ex); return null; }
    }

    public static void Delete()
    {
        try { if (Exists) File.Delete(FilePath); } catch (Exception ex) { FaceLog.Write(ex); }
    }

    static void Restrict(string path, WindowsIdentity id)
    {
        try
        {
            var sec = new FileSecurity();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            if (id.User != null) sec.AddAccessRule(new FileSystemAccessRule(id.User, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(sec);
        }
        catch (Exception ex) { FaceLog.Write(ex); }
    }
}
