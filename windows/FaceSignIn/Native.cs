using System;
using System.Runtime.InteropServices;

namespace DynamicIsland.FaceSignIn
{
    static class HR
    {
        public const int S_OK = 0, E_NOTIMPL = unchecked((int)0x80004001), E_INVALIDARG = unchecked((int)0x80070057), E_FAIL = unchecked((int)0x80004005);
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct LSA_STRING { public ushort Length, MaximumLength; public IntPtr Buffer; }

        [DllImport("secur32.dll")] public static extern int LsaConnectUntrusted(out IntPtr handle);
        [DllImport("secur32.dll")] public static extern int LsaLookupAuthenticationPackage(IntPtr handle, ref LSA_STRING name, out uint package);
        [DllImport("secur32.dll")] public static extern int LsaDeregisterLogonProcess(IntPtr handle);

        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredPackAuthenticationBuffer(int flags, string user, string password, IntPtr packed, ref int size);

        public static uint NegotiatePackage()
        {
            LsaConnectUntrusted(out var h);
            var bytes = System.Text.Encoding.ASCII.GetBytes("Negotiate");
            var buf = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, buf, bytes.Length);
                var s = new LSA_STRING { Length = (ushort)bytes.Length, MaximumLength = (ushort)bytes.Length, Buffer = buf };
                LsaLookupAuthenticationPackage(h, ref s, out var pkg);
                return pkg;
            }
            finally { Marshal.FreeHGlobal(buf); LsaDeregisterLogonProcess(h); }
        }

        /// Packs user + password the way Windows' own password tile does (works for sign-in and unlock).
        public static IntPtr Pack(string user, string password, out int size)
        {
            size = 0;
            CredPackAuthenticationBuffer(0, user, password, IntPtr.Zero, ref size);
            var buf = Marshal.AllocCoTaskMem(size); // LogonUI frees this with CoTaskMemFree
            if (!CredPackAuthenticationBuffer(0, user, password, buf, ref size))
            {
                Marshal.FreeCoTaskMem(buf);
                throw new InvalidOperationException("CredPackAuthenticationBuffer failed: " + Marshal.GetLastWin32Error());
            }
            return buf;
        }

        // 32-bit premultiplied-alpha DIB for the tile icon
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    }
}
