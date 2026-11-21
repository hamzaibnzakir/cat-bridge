using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using DynamicIsland.Shared;

namespace DynamicIsland.FaceSignIn
{
    /// <summary>
    /// Lets a trusted iPhone unlock this PC. While the lock screen is up, the tile listens on a named pipe that only the
    /// signed-in user (and SYSTEM) can open. The Dynamic Island app relays:
    ///   POLL                → WANT | IDLE           (did you press "Unlock with my iPhone"?)
    ///   CHALLENGE           → NONCE b64 MACHINE     (one-time, valid 60 s)
    ///   APPROVE nonce sig   → OK name | NO reason   (the tile verifies the Face ID signature itself)
    /// </summary>
    sealed class PhoneUnlock : IDisposable
    {
        readonly string _userSid;
        readonly Action<string> _approved;   // phone name; runs on a background thread
        volatile bool _stop;
        Thread _thread;
        string _nonce;
        DateTime _nonceUntil;
        public volatile bool Wanted;
        int _failures;

        public PhoneUnlock(string userSid, Action<string> approved)
        {
            _userSid = userSid; _approved = approved;
        }

        public static bool AnyTrusted { get { return PhoneTrust.LoadVerified().Count > 0; } }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop) { IsBackground = true, Name = "CatFacePhoneUnlock" };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
            // poke the pipe so the waiting server wakes up and exits
            try { using (var c = new NamedPipeClientStream(".", PhoneTrust.PipeName, PipeDirection.InOut)) c.Connect(200); } catch { }
        }

        PipeSecurity Security()
        {
            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(_userSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            return ps;
        }

        void Loop()
        {
            while (!_stop)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(PhoneTrust.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, Security()))
                    {
                        pipe.WaitForConnection();
                        if (_stop) break;
                        Serve(pipe);
                    }
                }
                catch (Exception ex) { Log.Write("phone pipe: " + ex.Message); Thread.Sleep(1000); }
            }
        }

        void Serve(NamedPipeServerStream pipe)
        {
            var reader = new StreamReader(pipe, new UTF8Encoding(false));
            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            string line = reader.ReadLine();
            if (line == null || line.Length > 2048) return;
            var parts = line.Split(' ');
            switch (parts[0])
            {
                case "POLL":
                    writer.WriteLine(Wanted ? "WANT" : "IDLE");
                    break;
                case "CHALLENGE":
                    var bytes = new byte[32];
                    using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                    _nonce = Convert.ToBase64String(bytes);
                    _nonceUntil = DateTime.UtcNow.AddSeconds(60);
                    writer.WriteLine("NONCE " + _nonce + " " + Environment.MachineName.ToUpperInvariant());
                    break;
                case "APPROVE":
                    writer.WriteLine(Approve(parts));
                    break;
                default:
                    writer.WriteLine("NO unknown");
                    break;
            }
        }

        string Approve(string[] parts)
        {
            if (parts.Length < 3) return "NO bad-request";
            if (_failures >= 5) return "NO too-many-tries";
            var nonce = _nonce;
            if (nonce == null || DateTime.UtcNow > _nonceUntil || parts[1] != nonce) return "NO expired";
            _nonce = null; // one use only, pass or fail
            byte[] sig;
            try { sig = Convert.FromBase64String(parts[2]); } catch { return "NO bad-signature"; }
            var msg = PhoneTrust.Message(Environment.MachineName, nonce);
            foreach (var phone in PhoneTrust.LoadVerified())
            {
                if (PhoneTrust.Verify(phone.PublicKey, msg, sig))
                {
                    Log.Write("unlocked by phone " + phone.Id);
                    Wanted = false;
                    _failures = 0;
                    _approved(phone.Name);
                    return "OK " + phone.Name;
                }
            }
            _failures++;
            Log.Write("phone signature rejected");
            return "NO not-trusted";
        }
    }
}
