using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace DynamicIsland.FaceId;

/// <summary>Turns Cat Face on/off for the Windows sign-in and lock screens.</summary>
public sealed class FaceSignInWindow : Window
{
    readonly AppSettings _s;
    readonly Brush _accent;
    static Brush Fz(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static readonly Brush Dim = Fz(Color.FromRgb(0x8E, 0x8E, 0x93)), Red = Fz(Color.FromRgb(0xFF, 0x6B, 0x60)), Field = Fz(Color.FromRgb(0x1C, 0x1C, 0x20));

    readonly StackPanel _body = new();
    readonly CatFace _cat = new() { Width = 70, Height = 58, Mood = FaceMood.Awake, HorizontalAlignment = HorizontalAlignment.Center };

    public static bool Installed
    {
        get { try { using var k = Registry.LocalMachine.OpenSubKey(SignInSecret.ProviderKey); return k != null; } catch { return false; } }
    }

    public FaceSignInWindow(AppSettings s)
    {
        _s = s;
        try { _accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Accent)); } catch { _accent = Brushes.Orange; }
        Title = "Cat Face sign-in";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); Foreground = Brushes.White;

        var root = new StackPanel();
        root.Children.Add(_cat);
        root.Children.Add(_body);
        Content = new Border
        {
            Background = Fz(Color.FromRgb(0x0B, 0x0B, 0x0D)), CornerRadius = new CornerRadius(32),
            BorderBrush = Fz(Color.FromRgb(0x24, 0x24, 0x28)), BorderThickness = new Thickness(1),
            Padding = new Thickness(28, 18, 28, 24), Child = root,
        };
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not TextBox and not PasswordBox) try { DragMove(); } catch { } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Render();
    }

    void Render(string? error = null)
    {
        _body.Children.Clear();
        if (!FaceStore.Exists)
        {
            _cat.Mood = FaceMood.Surprised;
            H("Set up your face first");
            P("Tray → Face unlock → Set up my face, then come back here.");
            Buttons(("Close", false, Close));
            return;
        }

        bool on = Installed;
        _cat.Mood = on ? FaceMood.Happy : FaceMood.Awake;
        H(on ? "Cat Face is on for sign-in" : "Use Cat Face to sign in to Windows");
        if (on)
        {
            P("Lock your PC (Win + L) or restart, then choose the cat under Sign-in options. After the first time, Windows remembers it and starts the camera on its own.");
            P("Changed your Windows password? Type the new one below so the cat can keep signing you in.");
        }
        else
        {
            Bullet("Works on the lock screen and after a restart.");
            Bullet("Same check as the test: your face, a head turn, then a final look.");
            Bullet("Two failed tries → a password box appears right in the tile.");
            Bullet("Your normal password and PIN options always stay available.");
            P("To sign you in, the cat needs your Windows password once. It's checked, encrypted, and stored where only Windows itself can read it.");
        }

        var id = WindowsIdentity.GetCurrent();
        var msa = MicrosoftAccountOf(id.Name);
        var acct = new TextBlock
        {
            Text = msa != null ? $"Account: {id.Name} · Microsoft account {msa}\nType your Microsoft account password (not your PIN)." : $"Account: {id.Name} · local account\nType your Windows account password (not your PIN).",
            FontSize = 12, Foreground = Dim, Margin = new Thickness(0, 14, 0, 6), TextWrapping = TextWrapping.Wrap, LineHeight = 17,
        };
        _body.Children.Add(acct);

        // password box with a show/hide eye, so you can see what you typed
        var pwd = new PasswordBox
        {
            FontSize = 14, Padding = new Thickness(12, 9, 44, 9), Background = Field, Foreground = Brushes.White,
            BorderThickness = new Thickness(1), BorderBrush = Fz(Color.FromRgb(0x2C, 0x2C, 0x32)), CaretBrush = _accent,
        };
        var plain = new TextBox
        {
            FontSize = 14, Padding = new Thickness(12, 9, 44, 9), Background = Field, Foreground = Brushes.White, Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(1), BorderBrush = Fz(Color.FromRgb(0x2C, 0x2C, 0x32)), CaretBrush = _accent,
        };
        var eye = new Button
        {
            Content = "\uE7B3", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Foreground = Dim,
            HorizontalAlignment = HorizontalAlignment.Right, Width = 40, Cursor = Cursors.Hand, ToolTip = "Show password",
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
        };
        eye.Click += (_, _) =>
        {
            bool show = plain.Visibility != Visibility.Visible;
            if (show) { plain.Text = pwd.Password; plain.Visibility = Visibility.Visible; pwd.Visibility = Visibility.Collapsed; }
            else { pwd.Password = plain.Text; pwd.Visibility = Visibility.Visible; plain.Visibility = Visibility.Collapsed; }
        };
        string Typed() => plain.Visibility == Visibility.Visible ? plain.Text : pwd.Password;
        var box = new Grid();
        box.Children.Add(pwd); box.Children.Add(plain); box.Children.Add(eye);
        _body.Children.Add(box);
        var err = new TextBlock { FontSize = 12, Foreground = Red, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Text = error ?? "" };
        _body.Children.Add(err);

        async void Enable()
        {
            err.Text = "";
            var typed = Typed();
            if (typed.Length == 0) { err.Text = "Type your Windows password first."; return; }
            var (check, signInName) = CheckPassword(id.Name, msa, typed);
            if (check != null) { err.Text = check; _cat.Mood = FaceMood.Surprised; return; }
            _cat.Mood = FaceMood.Content;
            err.Foreground = Dim; err.Text = "Windows will ask for permission to install…";
            string handoff = SignInSecret.WriteHandoff(id.User?.Value ?? "", signInName, typed);
            pwd.Clear(); plain.Clear();
            var cfg = $"camera={_s.CameraIndex} threshold={_s.FaceMatchThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture)} flip={(_s.FaceTurnFlip ? 1 : 0)}";
            var (ok, msg) = await RunElevated($"--install \"{AppContext.BaseDirectory.TrimEnd('\\')}\" \"{handoff}\" {cfg}");
            try { File.Delete(handoff); } catch { }
            err.Foreground = Red;
            if (ok) { Render(); _cat.Mood = FaceMood.Happy; }
            else Render(msg);
        }

        async void Disable()
        {
            var (ok, msg) = await RunElevated("--uninstall");
            Render(ok ? null : msg);
        }

        if (on) Buttons(("Turn off", false, Disable), ("Update password", true, Enable));
        else Buttons(("Not now", false, Close), ("Turn on", true, Enable));
        pwd.KeyDown += (_, e) => { if (e.Key == Key.Enter) Enable(); };
        plain.KeyDown += (_, e) => { if (e.Key == Key.Enter) Enable(); };
    }

    void H(string text) => _body.Children.Add(new TextBlock
    {
        Text = text, FontSize = 19, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), Margin = new Thickness(0, 10, 0, 10),
    });

    void P(string text) => _body.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), LineHeight = 19 });

    void Bullet(string text)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = _accent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 7, 0, 0) });
        var t = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1); g.Children.Add(t);
        _body.Children.Add(g);
    }

    void Buttons(params (string label, bool primary, Action act)[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var (label, primary, act) in items)
        {
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, primary ? _accent : Fz(Color.FromRgb(0x1F, 0x1F, 0x23)));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            bd.SetValue(Border.PaddingProperty, new Thickness(18, 8, 18, 8));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(TextElement.FontSizeProperty, 13.0);
            cp.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
            cp.SetValue(TextElement.ForegroundProperty, primary ? Fz(Color.FromRgb(0x1A, 0x12, 0x0C)) : Brushes.White);
            bd.AppendChild(cp);
            var b = new Button { Content = label, Margin = new Thickness(5, 0, 5, 0), Cursor = Cursors.Hand, Template = new ControlTemplate(typeof(Button)) { VisualTree = bd } };
            b.Click += (_, _) => act();
            row.Children.Add(b);
        }
        _body.Children.Add(row);
    }

    // ───────── checks + elevation ─────────

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool LogonUser(string user, string domain, string password, int type, int provider, out IntPtr token);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct USER_INFO_24 { public int internet_identity; public int flags; public string provider_name; public string principal_name; public IntPtr user_sid; }
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetUserGetInfo(string? server, string user, int level, out IntPtr buf);
    [DllImport("netapi32.dll")] static extern int NetApiBufferFree(IntPtr buf);

    /// The Microsoft account email linked to this Windows account, or null for a local account.
    static string? MicrosoftAccountOf(string fullName)
    {
        try
        {
            int slash = fullName.IndexOf('\\');
            var user = slash >= 0 ? fullName[(slash + 1)..] : fullName;
            if (NetUserGetInfo(null, user, 24, out var buf) != 0 || buf == IntPtr.Zero) return null;
            try
            {
                var info = Marshal.PtrToStructure<USER_INFO_24>(buf);
                return info.internet_identity != 0 && !string.IsNullOrWhiteSpace(info.principal_name) ? info.principal_name : null;
            }
            finally { NetApiBufferFree(buf); }
        }
        catch { return null; }
    }

    /// Confirms the password with Windows before saving it, and returns the exact account name that worked,
    /// so the sign-in tile uses the same one. Microsoft accounts are tried as MicrosoftAccount\email first.
    static (string? error, string name) CheckPassword(string fullName, string? msa, string password)
    {
        var tries = new System.Collections.Generic.List<(string domain, string user)>();
        if (msa != null) tries.Add(("MicrosoftAccount", msa));
        int slash = fullName.IndexOf('\\');
        tries.Add((slash > 0 ? fullName[..slash] : ".", slash > 0 ? fullName[(slash + 1)..] : fullName));

        int code = 0;
        foreach (var (domain, user) in tries)
        {
            if (LogonUser(user, domain, password, 2 /*interactive*/, 0, out var token)) { CloseHandle(token); return (null, domain + "\\" + user); }
            code = Marshal.GetLastWin32Error();
            if (code != 1326) break;
        }
        bool looksLikePin = password.Length >= 4 && password.Length <= 8 && password.All(char.IsDigit);
        string msg = code switch
        {
            1326 when looksLikePin => "That looks like your PIN. Windows needs your account password here. Forgot it? " +
                (msa != null ? "Reset it at account.live.com/password/reset." : "Set a new one in Settings → Accounts → Sign-in options → Password."),
            1326 => msa != null ? $"That isn't the password for {msa}. Use your Microsoft account password (the one you'd use on outlook.com), not your PIN."
                                : "That password didn't work. Use your Windows account password, not your PIN. Tap the eye to check what you typed.",
            1327 => "Your account has no password. Add one in Settings → Accounts → Sign-in options → Password, then try again.",
            1909 => "Your account is locked. Try again later.",
            1330 => "Your Windows password has expired. Change it first.",
            _ => $"Windows couldn't check that password (error {code}).",
        };
        return (msg, fullName);
    }

    static async Task<(bool ok, string msg)> RunElevated(string args)
    {
        var helper = Path.Combine(AppContext.BaseDirectory, "FaceHelper.exe");
        if (!File.Exists(helper)) return (false, "FaceHelper.exe is missing from the app folder.");
        try
        {
            var p = Process.Start(new ProcessStartInfo(helper, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p == null) return (false, "Couldn't start the installer.");
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? (true, "") : (false, $"The installer stopped (code {p.ExitCode}). Details: C:\\ProgramData\\DynamicIsland\\FaceSignIn\\helper.log");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return (false, "Windows permission was declined, so nothing changed."); }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
