using System;
using System.Diagnostics;
using WF = System.Windows.Forms;
using SD = System.Drawing;
using SD2 = System.Drawing.Drawing2D;

namespace DynamicIsland;

public sealed class TrayIcon : IDisposable
{
    readonly WF.NotifyIcon _ni;

    public TrayIcon(IslandWindow w, AppSettings s)
    {
        var menu = new WF.ContextMenuStrip();
        var center = new WF.ToolStripMenuItem("Open Cat Center   (Ctrl+Alt+C)", null, (_, _) => Center.CenterWindow.Open(w)) { Font = new SD.Font(WF.Control.DefaultFont, SD.FontStyle.Bold) };
        menu.Items.Add(center);
        menu.Items.Add("Expand / pin   (Ctrl+Alt+Space)", null, (_, _) => w.TogglePinned());
        menu.Items.Add("Search apps     (Ctrl+Alt+A)", null, (_, _) => w.OpenAppSearch());
        menu.Items.Add("Show / hide    (Ctrl+Alt+H)", null, (_, _) => w.ToggleHidden());
        if (s.HermesEnabled) menu.Items.Add("Ask Hermes     (Ctrl+Alt+Q)", null, (_, _) => w.OpenPrompt());
        menu.Items.Add(new WF.ToolStripSeparator());
        var app = (App)System.Windows.Application.Current;
        var petItem = new WF.ToolStripMenuItem("Desktop cat") { Checked = s.PetEnabled };
        petItem.Click += (_, _) => { app.TogglePet(); petItem.Checked = app.PetVisible; };
        menu.Items.Add(petItem);
        var colors = new WF.ToolStripMenuItem("Cat colour");
        foreach (var (label, key) in new[] { ("Ginger", "ginger"), ("Black", "black"), ("White", "white"), ("Grey", "grey") })
        {
            var item = new WF.ToolStripMenuItem(label) { Checked = string.Equals(s.PetColor, key, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) =>
            {
                app.SetPetColor(key);
                foreach (WF.ToolStripMenuItem other in colors.DropDownItems) other.Checked = other == item;
            };
            colors.DropDownItems.Add(item);
        }
        menu.Items.Add(colors);

        var phone = new WF.ToolStripMenuItem("iPhone");
        phone.DropDownItems.Add("Set up / pair…", null, (_, _) => Center.CenterWindow.Open(w, "iphone"));
        phone.DropDownItems.Add("Send clipboard to iPhone", null, (_, _) => w.SendClipboardToPhone());
        phone.DropDownItems.Add("Ring my iPhone", null, (_, _) => w.RingPhone());
        menu.Items.Add(phone);

        var face = new WF.ToolStripMenuItem("Face unlock");
        var testItem = new WF.ToolStripMenuItem("Test face unlock", null, (_, _) => new FaceId.FaceWindow(s, FaceId.FaceMode.Test).Show());
        var delItem = new WF.ToolStripMenuItem("Delete my face data", null, (_, _) =>
        {
            if (WF.MessageBox.Show("Delete your saved face? You can set it up again any time.", "Face unlock", WF.MessageBoxButtons.OKCancel) == WF.DialogResult.OK)
                FaceId.FaceStore.Delete();
        });
        face.DropDownItems.Add("Set up my face", null, (_, _) => new FaceId.FaceWindow(s, FaceId.FaceMode.Enroll).Show());
        face.DropDownItems.Add(testItem);
        var signInItem = new WF.ToolStripMenuItem("Use on Windows sign-in screen…", null, (_, _) => new FaceId.FaceSignInWindow(s).Show());
        face.DropDownItems.Add(signInItem);
        face.DropDownItems.Add(delItem);
        face.DropDownOpening += (_, _) =>
        {
            testItem.Enabled = delItem.Enabled = FaceId.FaceStore.Exists;
            signInItem.Checked = FaceId.FaceSignInWindow.Installed;
        };
        menu.Items.Add(face);
        var auto = new WF.ToolStripMenuItem("Start with Windows") { Checked = Autostart.IsEnabled, CheckOnClick = true };
        auto.CheckedChanged += (_, _) => Autostart.Set(auto.Checked);
        menu.Items.Add(auto);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Open settings.json", null, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppSettings.FilePath}\"") { UseShellExecute = true }); } catch { }
        });
        menu.Items.Add("Reload settings", null, (_, _) => w.ReloadSettings());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _ni = new WF.NotifyIcon { Icon = MakeIcon(s.Accent), Text = "Dynamic Island", Visible = true, ContextMenuStrip = menu };
        _ni.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) w.TogglePinned(); };
        _ni.MouseDoubleClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) Center.CenterWindow.Open(w); };
    }

    static SD.Icon MakeIcon(string accentHex)
    {
        SD.Color accent;
        try { accent = SD.ColorTranslator.FromHtml(accentHex); } catch { accent = SD.Color.FromArgb(57, 255, 136); }
        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
            g.Clear(SD.Color.Transparent);
            using var path = new SD2.GraphicsPath();
            var r = new SD.RectangleF(1, 9, 30, 14);
            float d = 14;
            path.AddArc(r.X, r.Y, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            path.CloseFigure();
            using var fill = new SD.SolidBrush(SD.Color.FromArgb(255, 20, 20, 20));
            g.FillPath(fill, path);
            using var pen = new SD.Pen(SD.Color.FromArgb(120, 255, 255, 255), 1);
            g.DrawPath(pen, path);
            using var dot = new SD.SolidBrush(accent);
            g.FillEllipse(dot, 20, 13, 6, 6);
        }
        return SD.Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose() { _ni.Visible = false; _ni.Dispose(); }
}
