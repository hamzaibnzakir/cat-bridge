using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace DynamicIsland;

public partial class App : Application
{
    Mutex? _mutex;
    TrayIcon? _tray;
    IslandWindow? _win;
    PetWindow? _pet;
    AppSettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "DynamicIsland.SingleInstance", out bool first);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToArray();
        if (!first)
        {
            // Already running: pass the request on (the installer uses --quit; the Start menu's Cat Center uses --center)
            Signal(args.Contains("--quit") ? "Quit" : args.Contains("--center") ? "Center" : "Show");
            Shutdown();
            return;
        }
        if (args.Contains("--quit")) { Shutdown(); return; }

        DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { Taskbar.TaskbarHider.Restore(); Log(ex.ExceptionObject as Exception); };

        FaceId.FaceLog.Sink = Log;
        FaceId.FaceLog.PrepareCamera(); // before any OpenCV call
        SmoothScroll.Install();
        base.OnStartup(e);
        var settings = _settings = AppSettings.Load();
        var win = _win = new IslandWindow(settings);
        win.Show();

        // the desktop cat reacts to what the island sees
        win.TimerFinished += () => { _pet?.React("happy"); _pet?.Say("Time's up! Nice focus session.", 6); };
        win.NotificationShown += _ => _pet?.React("alert");
        win.Sale += (amount, cur) => { _pet?.React("happy"); _pet?.Say(amount is decimal a ? $"Cha-ching! {Notify.Shopify.Format(a, cur)}" : "Cha-ching! New order!", 5); };
        win.PowerEvent += (ev, pct) =>
        {
            if (ev is "low20" or "low10") { _pet?.React("tired"); _pet?.Say($"Battery's at {pct}%. Plug me in?", 7); }
            else if (ev == "plug") _pet?.React("happy");
        };
        win.BridgeEvent += (kind, path) =>
        {
            if (kind == "file") { _pet?.React("happy"); _pet?.Say("Caught it! " + System.IO.Path.GetFileName(path), 4); }
            else if (kind == "ping") { _pet?.React("alert"); _pet?.Say("Meow! I'm right here!", 6); }
        };
        if (settings.PetEnabled) CreatePet();

        // warm up face recognition in the background so the camera check starts fast
        if (FaceId.FaceStore.Exists)
        {
            var warm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            warm.Tick += (_, _) => { warm.Stop(); FaceId.FaceCamera.Warm(System.IO.Path.Combine(AppContext.BaseDirectory, "models")); };
            warm.Start();
        }

        _tray = new TrayIcon(win, settings);

        ListenForSignals(win);
        Autostart.Repair();
        Updater.Start(win, settings);
        if (args.Contains("--center") || args.Contains("--updated")) Center.CenterWindow.Open(win, args.Contains("--updated") ? "general" : "home");
        if (args.Contains("--updated")) win.ShowNotice($"Updated to Cat Bridge {Updater.CurrentVersion.ToString(3)}", "Have a look at what's new in Cat Center");
    }

    // ───────── talking to the running copy ─────────

    static void Signal(string what)
    {
        try { using var ev = EventWaitHandle.OpenExisting("DynamicIsland." + what); ev.Set(); } catch { }
    }

    void ListenForSignals(IslandWindow win)
    {
        foreach (var what in new[] { "Quit", "Center", "Show" })
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "DynamicIsland." + what);
            var name = what;
            var t = new Thread(() =>
            {
                while (ev.WaitOne())
                    Dispatcher.InvokeAsync(() =>
                    {
                        switch (name)
                        {
                            case "Quit": Shutdown(); break;
                            case "Center": Center.CenterWindow.Open(win); break;
                            default: win.ShowIslandPage("home"); break;
                        }
                    });
            }) { IsBackground = true, Name = "Signal " + what };
            t.Start();
        }
    }

    public bool PetVisible => _pet != null;

    void CreatePet()
    {
        if (_win == null || _settings == null) return;
        var pet = _pet = new PetWindow(_settings);
        pet.Command += cmd =>
        {
            switch (cmd)
            {
                case "focus25": _win.StartFocus(25); break;
                case "playpause": _win.TogglePlayback(); break;
                case "island": _win.OpenIsland(); break;
                case "ringphone": _win.RingPhone(); _pet?.Say("Meowing at your iPhone…"); break;
            }
        };
        pet.FilesDropped += files => _win.AddToShelf(files);
        pet.Show();
    }

    public void TogglePet()
    {
        if (_settings == null) return;
        if (_pet == null) { _settings.PetEnabled = true; CreatePet(); }
        else { _pet.Close(); _pet = null; _settings.PetEnabled = false; }
        _settings.Save();
    }

    public void SetPetColor(string color)
    {
        if (_settings == null) return;
        _settings.PetColor = color;
        _settings.Save();
        _pet?.ApplyPalette(color);
        CatFace.CurrentPalette = CatArt.Palette(color);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Taskbar.TaskbarHider.Restore();
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }

    public static void Log(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.AppendAllText(Path.Combine(AppSettings.Dir, "log.txt"), $"[{DateTime.Now:u}] {ex}\n");
        }
        catch { }
    }
}
