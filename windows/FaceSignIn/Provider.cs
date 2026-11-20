using System;
using System.Runtime.InteropServices;
using CredProvider.NET.Interop2;

namespace DynamicIsland.FaceSignIn
{
    /// <summary>
    /// "Cat Face" sign-in option. Shows up under your own user tile on the Windows sign-in and lock screens.
    /// It never filters or hides Windows' own options, so your password and PIN always stay available.
    /// </summary>
    [ComVisible(true)]
    [Guid(Clsid)]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("DynamicIsland.CatFace")]
    public sealed class CatFaceProvider : ICredentialProvider, ICredentialProviderSetUserArray
    {
        public const string Clsid = "7a4a5c8b-1a84-498f-9bf5-d0fcffc94a1e"; // keep in sync with FaceId/SignInSecret.cs

        internal _CREDENTIAL_PROVIDER_USAGE_SCENARIO Usage;
        internal UiThread Ui;
        CredStore _secret;
        CatFaceCredential _cred;
        ICredentialProviderEvents _events;
        ulong _context;

        public int SetUsageScenario(_CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, uint dwFlags)
        {
            try
            {
                if (cpus != _CREDENTIAL_PROVIDER_USAGE_SCENARIO.CPUS_LOGON && cpus != _CREDENTIAL_PROVIDER_USAGE_SCENARIO.CPUS_UNLOCK_WORKSTATION)
                    return HR.E_NOTIMPL;
                if (!System.IO.File.Exists(CatFaceCredential.HelperPath)) { Log.Write("helper missing"); return HR.E_NOTIMPL; }
                _secret = CredStore.Load();
                if (_secret == null) return HR.E_NOTIMPL; // not set up: stay invisible
                Usage = cpus;
                Ui = Ui ?? new UiThread(); // created on LogonUI's UI thread
                return HR.S_OK;
            }
            catch (Exception ex) { Log.Write("SetUsageScenario: " + ex); return HR.E_NOTIMPL; }
        }

        public int SetSerialization(ref _CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION pcpcs) => HR.E_NOTIMPL;

        public int Advise(ICredentialProviderEvents pcpe, ulong upAdviseContext)
        {
            _events = pcpe; _context = upAdviseContext;
            return HR.S_OK;
        }

        public int UnAdvise()
        {
            _events = null;
            _cred?.StopHelper();
            return HR.S_OK;
        }

        public int SetUserArray(ICredentialProviderUserArray users)
        {
            try
            {
                _cred = null;
                if (_secret == null) return HR.S_OK;
                users.GetCount(out uint n);
                for (uint i = 0; i < n; i++)
                {
                    users.GetAt(i, out ICredentialProviderUser u);
                    u.GetSid(out string sid);
                    if (string.Equals(sid, _secret.Sid, StringComparison.OrdinalIgnoreCase))
                    {
                        _cred = new CatFaceCredential(this, sid, _secret);
                        break;
                    }
                }
                return HR.S_OK;
            }
            catch (Exception ex) { Log.Write("SetUserArray: " + ex); return HR.S_OK; }
        }

        public int GetFieldDescriptorCount(out uint pdwCount)
        {
            pdwCount = (uint)Fields.All.Length;
            return HR.S_OK;
        }

        // The interop declares this as a raw CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** we write into.
        public int GetFieldDescriptorAt(uint dwIndex, [Out] IntPtr ppcpfd)
        {
            try
            {
                if (ppcpfd == IntPtr.Zero) return HR.E_INVALIDARG;
                Marshal.WriteIntPtr(ppcpfd, IntPtr.Zero);
                if (dwIndex >= Fields.All.Length) return HR.E_INVALIDARG;
                var f = Fields.All[dwIndex];
                var d = new _CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR { dwFieldID = dwIndex, cpft = f.Type, pszLabel = f.Label, guidFieldType = f.Guid };
                var p = Marshal.AllocCoTaskMem(Marshal.SizeOf(d)); // LogonUI frees with CoTaskMemFree
                Marshal.StructureToPtr(d, p, false);
                Marshal.WriteIntPtr(ppcpfd, p);
                return HR.S_OK;
            }
            catch (Exception ex) { Log.Write("GetFieldDescriptorAt: " + ex); return HR.E_FAIL; }
        }

        public int GetCredentialCount(out uint pdwCount, out uint pdwDefault, out int pbAutoLogonWithDefault)
        {
            pdwCount = _cred == null ? 0u : 1u;
            pdwDefault = _cred == null ? 0xFFFFFFFF : 0u;
            // After a successful face check we ask LogonUI to sign in straight away.
            pbAutoLogonWithDefault = _cred != null && _cred.ReadyToSubmit ? 1 : 0;
            return HR.S_OK;
        }

        public int GetCredentialAt(uint dwIndex, out ICredentialProviderCredential ppcpc)
        {
            ppcpc = dwIndex == 0 ? _cred : null;
            return ppcpc == null ? HR.E_INVALIDARG : HR.S_OK;
        }

        internal void RaiseCredentialsChanged()
        {
            try { _events?.CredentialsChanged(_context); } catch (Exception ex) { Log.Write("CredentialsChanged: " + ex); }
        }
    }

    /// <summary>Marshals work from background threads onto LogonUI's UI thread (where the COM callbacks must run).</summary>
    sealed class UiThread
    {
        readonly System.Windows.Forms.Control _c = new System.Windows.Forms.Control();
        public UiThread() { _c.CreateControl(); var _ = _c.Handle; }
        public void Post(Action a)
        {
            try { if (!_c.IsDisposed) _c.BeginInvoke((Action)(() => { try { a(); } catch (Exception ex) { Log.Write("ui: " + ex); } })); }
            catch (Exception ex) { Log.Write("post: " + ex); }
        }
    }

    sealed class FieldDef
    {
        public _CREDENTIAL_PROVIDER_FIELD_TYPE Type; public string Label; public Guid Guid;
        public FieldDef(_CREDENTIAL_PROVIDER_FIELD_TYPE t, string label, string guid = null) { Type = t; Label = label; Guid = guid == null ? Guid.Empty : new Guid(guid); }
    }

    static class Fields
    {
        public const uint Tile = 0, Label = 1, Title = 2, Feed = 3, Status = 4, UsePassword = 5, Password = 6, Submit = 7;
        const string CPFG_CREDENTIAL_PROVIDER_LOGO = "2d837775-f6cd-464e-a745-482fd0b47493";
        const string CPFG_CREDENTIAL_PROVIDER_LABEL = "286bbff3-bad4-438f-b007-79b7267c3d48";
        const string CPFG_LOGON_PASSWORD_GUID = "60624cfa-a477-47b1-8a8e-3a4a19981827";

        public static readonly FieldDef[] All =
        {
            new FieldDef(_CREDENTIAL_PROVIDER_FIELD_TYPE.CPFT_TILE_IMAGE, "Cat Face", CPFG_CREDENTIAL_PROVIDER_LOGO),
            new FieldDef(_CREDENTIAL_PROVIDER_FIELD_TYPE.CPFT_SMALL_TEXT, "Cat Face", CPFG_CREDENTIAL_PROVIDER_LABEL),
            new FieldDef(_CREDENTIAL_PROVIDER_FIELD_TYPE.CPFT_LARGE_TEXT, "Cat Face"),
            new FieldDef(_CREDENTIAL_PROVIDER_FIELD_TYPE.CPFT_SMALL_TEXT, "While you were away"),
            new FieldDef(_CREDENTIAL_PROVIDER_FIELD_TYPE.CPFT_SMALL_TEXT, "Status"),
            new FieldDef(_CREDENTIAL_PROVIDER_FIELD_TYPE.CPFT_COMMAND_LINK, "Use my password instead"),
            new FieldDef(_CREDENTIAL_PROVIDER_FIELD_TYPE.CPFT_PASSWORD_TEXT, "Password", CPFG_LOGON_PASSWORD_GUID),
            new FieldDef(_CREDENTIAL_PROVIDER_FIELD_TYPE.CPFT_SUBMIT_BUTTON, "Submit"),
        };
    }
}
