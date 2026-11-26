using System.Threading;
using DynamicIsland.Bridge;

namespace DynamicIsland;

/// <summary>The iPhone watching this screen (Remote tab). While anyone watches, the island shows a purple dot.</summary>
public partial class IslandWindow
{
    int _screenViewers;

    bool IBridgeHost.ScreenAllowed => _s.ScreenShare;
    bool IBridgeHost.SessionLocked => _sessionLocked;

    string? IBridgeHost.ScreenStart()
    {
        if (!_s.ScreenShare) return "Screen viewing is off on the laptop. Turn it on in Cat Center → iPhone.";
        if (Interlocked.Increment(ref _screenViewers) == 1)
            Dispatcher.InvokeAsync(() =>
            {
                ShowToast(new ToastSpec("Your iPhone is viewing this screen", "Cat Bridge · turn it off in Cat Center → iPhone", Glyph: "", GlyphBrush: ScreenPurple, Seconds: 4));
                PollPrivacy(); UpdateCompact();
            });
        return null;
    }

    IScreenFeed IBridgeHost.OpenScreen() => new ScreenGrabber();

    void IBridgeHost.ScreenStop()
    {
        if (Interlocked.Decrement(ref _screenViewers) <= 0)
        {
            Interlocked.Exchange(ref _screenViewers, 0);
            Dispatcher.InvokeAsync(() => { PollPrivacy(); UpdateCompact(); });
        }
    }

    static readonly System.Windows.Media.Brush ScreenPurple = Frozen(System.Windows.Media.Color.FromRgb(0xBF, 0x5A, 0xF2));
}
