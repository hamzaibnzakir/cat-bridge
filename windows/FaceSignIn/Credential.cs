using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using CredProvider.NET.Interop2;

namespace DynamicIsland.FaceSignIn
{
    /// <summary>
    /// The Cat Face tile. When selected it starts FaceHelper.exe (a separate process, so a camera or model crash
    /// can't take the sign-in screen down), shows its prompts, and signs you in when your face passes.
    /// Two failed tries, a missing camera or a timeout → the password box appears right in the tile.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class CatFaceCredential : ICredentialProviderCredential2, ICredentialProviderCredential
    {
        public static string HelperPath => Path.Combine(Path.GetDirectoryName(typeof(CatFaceCredential).Assembly.Location) ?? "", "FaceHelper.exe");

        readonly CatFaceProvider _provider;
        readonly string _sid;
        readonly CredStore _secret;
        ICredentialProviderCredentialEvents _ev;
        Process _helper;
        int _generation;

        string _status = "Look at the camera to sign in";
        string _typed = "";
        bool _showPassword, _faceOk, _usedTyped;

        internal bool ReadyToSubmit => _faceOk;

        internal CatFaceCredential(CatFaceProvider provider, string sid, CredStore secret)
        {
            _provider = provider; _sid = sid; _secret = secret;
        }

        // ───────── lifecycle ─────────

        public int Advise(ICredentialProviderCredentialEvents pcpce)
        {
            _ev = pcpce;
            _feed = LockFeed.Read();
            _feedTimer?.Dispose();
            _feedTimer = new Timer(_ => { var f = LockFeed.Read(); _provider.Ui.Post(() => UpdateFeed(f)); }, null, 5000, 5000);
            return HR.S_OK;
        }
        public int UnAdvise() { _feedTimer?.Dispose(); _feedTimer = null; _ev = null; StopHelper(); return HR.S_OK; }

        // ───────── "while you were away" line (written by the Dynamic Island app) ─────────

        string _feed = "";
        Timer _feedTimer;

        void UpdateFeed(string f)
        {
            if (f == _feed || _ev == null) return;
            bool wasEmpty = _feed.Length == 0;
            _feed = f;
            try
            {
                _ev.SetFieldString(this, Fields.Feed, f);
                if (wasEmpty != (f.Length == 0))
                    _ev.SetFieldState(this, Fields.Feed, f.Length == 0 ? _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN : _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_BOTH);
            }
            catch (Exception ex) { Log.Write("feed: " + ex); }
        }

        public int SetSelected(out int pbAutoLogon)
        {
            pbAutoLogon = _faceOk ? 1 : 0;
            if (!_faceOk && !_showPassword) StartHelper();
            return HR.S_OK;
        }

        public int SetDeselected()
        {
            StopHelper();
            _typed = "";
            _ev?.SetFieldString(this, Fields.Password, "");
            return HR.S_OK;
        }

        // ───────── fields ─────────

        public int GetFieldState(uint id, out _CREDENTIAL_PROVIDER_FIELD_STATE s, out _CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE i)
        {
            i = _CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE.CPFIS_NONE;
            switch (id)
            {
                case Fields.Tile: s = _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_BOTH; break;
                case Fields.Label: s = _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN; break;
                case Fields.Title: s = _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN; break; // your name already shows on the user tile
                case Fields.Feed: s = _feed.Length == 0 ? _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN : _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_BOTH; break;
                case Fields.Status: s = _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_SELECTED_TILE; break;
                case Fields.UsePassword: s = _showPassword ? _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN : _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_SELECTED_TILE; break;
                case Fields.Password:
                    s = _showPassword ? _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_SELECTED_TILE : _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN;
                    if (_showPassword) i = _CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE.CPFIS_FOCUSED;
                    break;
                case Fields.UsePhone: s = _provider.Phone != null ? _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_SELECTED_TILE : _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN; break;
                case Fields.Submit: s = _showPassword ? _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_SELECTED_TILE : _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN; break;
                default: s = _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN; return HR.E_INVALIDARG;
            }
            return HR.S_OK;
        }

        public int GetStringValue(uint id, out string ppsz)
        {
            switch (id)
            {
                case Fields.Label: case Fields.Title: ppsz = "Cat Face"; return HR.S_OK;
                case Fields.Status: ppsz = _status; return HR.S_OK;
                case Fields.Feed: ppsz = _feed; return HR.S_OK;
                case Fields.UsePassword: ppsz = "Use my password instead"; return HR.S_OK;
                case Fields.UsePhone: ppsz = "Unlock with my iPhone"; return HR.S_OK;
                case Fields.Password: ppsz = _typed; return HR.S_OK;
                default: ppsz = ""; return HR.E_INVALIDARG;
            }
        }

        public int GetBitmapValue(uint id, out IntPtr phbmp)
        {
            phbmp = IntPtr.Zero;
            if (id != Fields.Tile) return HR.E_INVALIDARG;
            try { phbmp = TileBitmap(); return phbmp == IntPtr.Zero ? HR.E_FAIL : HR.S_OK; }
            catch (Exception ex) { Log.Write("bitmap: " + ex); return HR.E_FAIL; }
        }

        public int GetSubmitButtonValue(uint id, out uint pdwAdjacentTo)
        {
            pdwAdjacentTo = Fields.Password;
            return id == Fields.Submit ? HR.S_OK : HR.E_INVALIDARG;
        }

        public int SetStringValue(uint id, string psz)
        {
            if (id == Fields.Password) { _typed = psz ?? ""; return HR.S_OK; }
            return HR.E_INVALIDARG;
        }

        public int CommandLinkClicked(uint id)
        {
            if (id == Fields.UsePhone)
            {
                if (_provider.Phone == null) { SetStatus("No iPhone is set up to unlock this PC yet."); return HR.S_OK; }
                _provider.Phone.Wanted = true;
                SetStatus("Check your iPhone and look at it to unlock…");
                return HR.S_OK;
            }
            if (id != Fields.UsePassword) return HR.E_INVALIDARG;
            StopHelper();
            ShowPassword("Type your password");
            return HR.S_OK;
        }

        public int GetCheckboxValue(uint id, out int pbChecked, out string ppszLabel) { pbChecked = 0; ppszLabel = ""; return HR.E_NOTIMPL; }
        public int GetComboBoxValueCount(uint id, out uint pcItems, out uint pdwSelectedItem) { pcItems = 0; pdwSelectedItem = 0; return HR.E_NOTIMPL; }
        public int GetComboBoxValueAt(uint id, uint dwItem, out string ppszItem) { ppszItem = ""; return HR.E_NOTIMPL; }
        public int SetCheckboxValue(uint id, int bChecked) => HR.E_NOTIMPL;
        public int SetComboBoxSelectedValue(uint id, uint dwSelectedItem) => HR.E_NOTIMPL;
        public int GetUserSid(out string sid) { sid = _sid; return HR.S_OK; }

        // ───────── signing in ─────────

        public int GetSerialization(out _CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE pcpgsr, out _CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION pcpcs,
            out string ppszOptionalStatusText, out _CREDENTIAL_PROVIDER_STATUS_ICON pcpsiOptionalStatusIcon)
        {
            pcpgsr = _CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE.CPGSR_NO_CREDENTIAL_NOT_FINISHED;
            pcpcs = new _CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION();
            ppszOptionalStatusText = null;
            pcpsiOptionalStatusIcon = _CREDENTIAL_PROVIDER_STATUS_ICON.CPSI_NONE;
            try
            {
                string password = _faceOk ? _secret.Password : (_showPassword && _typed.Length > 0 ? _typed : null);
                if (password == null) return HR.S_OK; // nothing to submit yet
                _usedTyped = !_faceOk;
                _faceOk = false; // one shot

                var buf = Native.Pack(_secret.User, password, out int size);
                pcpcs.clsidCredentialProvider = new Guid(CatFaceProvider.Clsid);
                pcpcs.rgbSerialization = buf;
                pcpcs.cbSerialization = (uint)size;
                pcpcs.ulAuthenticationPackage = Native.NegotiatePackage();
                pcpgsr = _CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE.CPGSR_RETURN_CREDENTIAL_FINISHED;
                return HR.S_OK;
            }
            catch (Exception ex)
            {
                Log.Write("GetSerialization: " + ex);
                ppszOptionalStatusText = "Couldn't sign in with Cat Face. Use your password.";
                pcpsiOptionalStatusIcon = _CREDENTIAL_PROVIDER_STATUS_ICON.CPSI_ERROR;
                return HR.S_OK;
            }
        }

        public int ReportResult(int ntsStatus, int ntsSubstatus, out string ppszOptionalStatusText, out _CREDENTIAL_PROVIDER_STATUS_ICON pcpsiOptionalStatusIcon)
        {
            ppszOptionalStatusText = null;
            pcpsiOptionalStatusIcon = _CREDENTIAL_PROVIDER_STATUS_ICON.CPSI_NONE;
            try
            {
                if (ntsStatus == 0)
                {
                    if (_usedTyped && _typed.Length > 0 && _typed != _secret.Password) _secret.SavePassword(_typed); // password changed: remember the new one
                }
                else if (!_usedTyped)
                {
                    Log.Write("saved password rejected: 0x" + ntsStatus.ToString("X8"));
                    ShowPassword("Your saved password didn't work. Type your current one and I'll remember it.");
                }
                _typed = "";
            }
            catch (Exception ex) { Log.Write("ReportResult: " + ex); }
            return HR.S_OK;
        }

        // ───────── face check (separate process) ─────────

        void StartHelper()
        {
            StopHelper();
            int gen = Interlocked.Increment(ref _generation);
            SetStatus("Waking up the camera…");
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo(HelperPath, "--verify")
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                        WorkingDirectory = Path.GetDirectoryName(HelperPath),
                    },
                };
                p.Start();
                _helper = p;
                var t = new Thread(() => ReadHelper(p, gen)) { IsBackground = true, Name = "CatFaceHelper" };
                t.Start();
            }
            catch (Exception ex)
            {
                Log.Write("start helper: " + ex);
                ShowPassword("Cat Face isn't available right now. Type your password.");
            }
        }

        void ReadHelper(Process p, int gen)
        {
            bool gotResult = false;
            try
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    var parts = line.Split(new[] { '|' }, 3);
                    if (parts[0] == "STATUS" && parts.Length > 1) { var s = parts[1]; _provider.Ui.Post(() => { if (gen == _generation) SetStatus(s); }); }
                    else if (parts[0] == "RESULT")
                    {
                        gotResult = true;
                        var what = parts.Length > 1 ? parts[1] : "";
                        var why = parts.Length > 2 ? parts[2] : "Type your password instead.";
                        _provider.Ui.Post(() =>
                        {
                            if (gen != _generation) return;
                            if (what == "passed")
                            {
                                _faceOk = true;
                                SetStatus("Welcome back!");
                                _provider.RaiseCredentialsChanged(); // LogonUI re-asks, sees auto-logon, calls GetSerialization
                            }
                            else ShowPassword(why);
                        });
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Write("read helper: " + ex); }
            if (!gotResult) _provider.Ui.Post(() => { if (gen == _generation && !_faceOk) ShowPassword("Cat Face stopped unexpectedly. Type your password."); });
        }

        internal void StopHelper()
        {
            Interlocked.Increment(ref _generation);
            var p = _helper; _helper = null;
            try { if (p != null && !p.HasExited) p.Kill(); } catch { }
            try { p?.Dispose(); } catch { }
        }

        /// A trusted iPhone signed this PC's challenge after Face ID: sign in with the saved credential.
        internal void PhoneApproved(string phoneName)
        {
            StopHelper();
            _faceOk = true;
            SetStatus("Unlocked with " + phoneName);
            _provider.RaiseCredentialsChanged(); // LogonUI re-asks, sees auto-logon, calls GetSerialization
        }

        void SetStatus(string s)
        {
            _status = s;
            try { _ev?.SetFieldString(this, Fields.Status, s); } catch (Exception ex) { Log.Write("SetFieldString: " + ex); }
        }

        void ShowPassword(string message)
        {
            _showPassword = true;
            SetStatus(message);
            try
            {
                _ev?.SetFieldState(this, Fields.UsePassword, _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_HIDDEN);
                _ev?.SetFieldState(this, Fields.Password, _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_SELECTED_TILE);
                _ev?.SetFieldState(this, Fields.Submit, _CREDENTIAL_PROVIDER_FIELD_STATE.CPFS_DISPLAY_IN_SELECTED_TILE);
                _ev?.SetFieldInteractiveState(this, Fields.Password, _CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE.CPFIS_FOCUSED);
            }
            catch (Exception ex) { Log.Write("ShowPassword: " + ex); }
        }

        // ───────── tile icon (the cat, with real transparency) ─────────

        static IntPtr TileBitmap()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("cat-tile.png"))
            using (var src = new System.Drawing.Bitmap(s))
            {
                int w = src.Width, h = src.Height;
                var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                var hbmp = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (hbmp == IntPtr.Zero) return IntPtr.Zero;
                var data = src.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    var row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        Marshal.Copy(row, 0, bits + y * w * 4, row.Length);
                    }
                }
                finally { src.UnlockBits(data); }
                return hbmp;
            }
        }
    }
}
