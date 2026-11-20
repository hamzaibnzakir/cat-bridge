using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace DynamicIsland.FaceSignIn
{
    /// <summary>Reads (and, after a typed password works, refreshes) the saved credential. Mirrors FaceId/SignInSecret.cs.</summary>
    sealed class CredStore
    {
        public string Sid, User, Password;

        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DynamicIsland", "FaceSignIn");
        static string FilePath => Path.Combine(Dir, "cred.bin");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DynamicIsland.FaceSignIn.v1");

        public static CredStore Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var text = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.LocalMachine));
                var p = text.Split('\n');
                if (p.Length < 4 || p[0] != "v1") return null;
                return new CredStore { Sid = p[1], User = Encoding.UTF8.GetString(Convert.FromBase64String(p[2])), Password = Encoding.UTF8.GetString(Convert.FromBase64String(p[3])) };
            }
            catch (Exception ex) { Log.Write("CredStore.Load: " + ex); return null; }
        }

        public void SavePassword(string password)
        {
            try
            {
                Password = password;
                var text = "v1\n" + Sid + "\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(User)) + "\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
                var tmp = FilePath + ".tmp";
                File.WriteAllBytes(tmp, ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy, DataProtectionScope.LocalMachine));
                var sec = new FileSecurity();
                sec.SetAccessRuleProtection(true, false);
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                File.SetAccessControl(tmp, sec);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("CredStore.Save: " + ex); }
        }
    }

    static class Log
    {
        public static void Write(string msg)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DynamicIsland", "FaceSignIn");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "provider.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, "[" + DateTime.Now.ToString("u") + "] " + msg + "\n");
            }
            catch { }
        }
    }
}
