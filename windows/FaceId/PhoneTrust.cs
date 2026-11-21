// Shared by the app (net8), FaceHelper (net8) and the sign-in tile (net48): keep it C# 7.3/.NET Framework friendly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DynamicIsland.Shared
{
    /// <summary>
    /// iPhones allowed to unlock this PC. Each one holds a P-256 key inside its Secure Enclave that only signs after Face ID.
    /// The public keys live in ProgramData\DynamicIsland\FaceSignIn\phones.txt, which only Administrators and SYSTEM can change
    /// (written by FaceHelper --trust-phone after you press Allow in Cat Center). The sign-in tile checks signatures itself, so the
    /// app that relays messages between the phone and the tile can't fake an unlock.
    /// </summary>
    public static class PhoneTrust
    {
        public const string PipeName = "CatFace.Unlock";

        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DynamicIsland", "FaceSignIn", "phones.txt"); }
        }

        public sealed class Phone
        {
            public string Name;
            public byte[] PublicKey; // 65 bytes: 04 || X || Y (what iOS's SecKeyCopyExternalRepresentation gives)
            public string Added;
            public string Id { get { return KeyId(PublicKey); } }
        }

        /// Short id for a key: first 8 bytes of SHA-256, hex.
        public static string KeyId(byte[] publicKey)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(publicKey), 0, 8).Replace("-", "").ToLowerInvariant();
        }

        /// The 6-digit code shown on both the iPhone and the laptop when pairing, so you can check they match.
        public static string PairingCode(byte[] publicKey)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(publicKey);
                uint n = (uint)(h[0] << 24 | h[1] << 16 | h[2] << 8 | h[3]) % 1000000u;
                return n.ToString("000000");
            }
        }

        /// For the sign-in tile: only trust the file if Administrators/SYSTEM own it and nobody else can write to it.
        /// (Standard users may create files in that folder, so a planted phones.txt must never be accepted.)
        public static List<Phone> LoadVerified()
        {
            try
            {
                if (!File.Exists(FilePath) || !SafeAcl(FilePath)) return new List<Phone>();
            }
            catch { return new List<Phone>(); }
            return Load();
        }

        static bool SafeAcl(string path)
        {
#if NETFRAMEWORK
            var sec = File.GetAccessControl(path);
#else
            var sec = new FileInfo(path).GetAccessControl();
#endif
            var owner = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !Privileged(owner)) return false;
            const FileSystemRights writeish = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes
                | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (r.AccessControlType != AccessControlType.Allow) continue;
                if ((r.FileSystemRights & writeish) == 0) continue;
                if (!Privileged((SecurityIdentifier)r.IdentityReference)) return false;
            }
            return true;
        }

        static bool Privileged(SecurityIdentifier sid)
        {
            return sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
                || sid.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"; // TrustedInstaller
        }

        public static List<Phone> Load()
        {
            var list = new List<Phone>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var p = line.Split('|');
                    if (p.Length < 4 || p[0] != "v1") continue;
                    var key = Convert.FromBase64String(p[2]);
                    if (key.Length != 65 || key[0] != 4) continue;
                    list.Add(new Phone { Name = Encoding.UTF8.GetString(Convert.FromBase64String(p[1])), PublicKey = key, Added = p[3] });
                }
            }
            catch { }
            return list;
        }

        public static string Line(Phone p)
        {
            return "v1|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(p.Name)) + "|" + Convert.ToBase64String(p.PublicKey) + "|" + p.Added;
        }

        /// What the iPhone signs: ties the signature to this PC and this one-time challenge.
        public static byte[] Message(string machine, string nonceB64)
        {
            return Encoding.UTF8.GetBytes("catbridge-unlock|v1|" + machine.ToUpperInvariant() + "|" + nonceB64);
        }

        /// Checks an ECDSA P-256 / SHA-256 signature in X9.62 DER form (iOS ecdsaSignatureMessageX962SHA256).
        public static bool Verify(byte[] publicKey, byte[] message, byte[] derSignature)
        {
            try
            {
                if (publicKey == null || publicKey.Length != 65 || publicKey[0] != 4) return false;
                var raw = DerToP1363(derSignature);
                if (raw == null) return false;
                var x = new byte[32]; var y = new byte[32];
                Buffer.BlockCopy(publicKey, 1, x, 0, 32);
                Buffer.BlockCopy(publicKey, 33, y, 0, 32);
                using (var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } }))
                    return ec.VerifyData(message, raw, HashAlgorithmName.SHA256);
            }
            catch { return false; }
        }

        /// SEQUENCE { INTEGER r, INTEGER s } → r || s (32 bytes each).
        public static byte[] DerToP1363(byte[] der)
        {
            if (der == null || der.Length < 8 || der[0] != 0x30) return null;
            int i = 1;
            int seqLen = ReadLen(der, ref i);
            if (seqLen < 0 || i + seqLen != der.Length) return null;
            var r = ReadInt(der, ref i); var s = ReadInt(der, ref i);
            if (r == null || s == null || i != der.Length) return null;
            var outp = new byte[64];
            if (!Fit(r, outp, 0) || !Fit(s, outp, 32)) return null;
            return outp;
        }

        static int ReadLen(byte[] b, ref int i)
        {
            if (i >= b.Length) return -1;
            int l = b[i++];
            if (l < 0x80) return l;
            int n = l & 0x7f;
            if (n < 1 || n > 2 || i + n > b.Length) return -1;
            l = 0;
            for (int k = 0; k < n; k++) l = l << 8 | b[i++];
            return l;
        }

        static byte[] ReadInt(byte[] b, ref int i)
        {
            if (i >= b.Length || b[i++] != 0x02) return null;
            int l = ReadLen(b, ref i);
            if (l <= 0 || i + l > b.Length) return null;
            var v = new byte[l];
            Buffer.BlockCopy(b, i, v, 0, l);
            i += l;
            return v;
        }

        static bool Fit(byte[] v, byte[] dst, int off)
        {
            int start = 0;
            while (start < v.Length - 1 && v[start] == 0) start++; // drop sign padding
            int len = v.Length - start;
            if (len > 32) return false;
            Buffer.BlockCopy(v, start, dst, off + 32 - len, len);
            return true;
        }
    }
}
