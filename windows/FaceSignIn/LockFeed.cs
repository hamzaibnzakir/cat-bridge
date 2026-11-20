using System;
using System.IO;
using System.Text;

namespace DynamicIsland.FaceSignIn
{
    /// <summary>
    /// The short summary the Dynamic Island app leaves for the sign-in screen, e.g. "🛍 4 orders · $312 today · 2 iPhone alerts".
    /// The app decides how much detail to write (Cat Center → Notifications → Sign-in screen). Stale summaries (older than 12 h) are ignored.
    /// </summary>
    static class LockFeed
    {
        public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DynamicIsland", "LockScreen", "feed.txt");

        public static string Read()
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (!fi.Exists || fi.Length > 4096 || DateTime.UtcNow - fi.LastWriteTimeUtc > TimeSpan.FromHours(12)) return "";
                var text = File.ReadAllText(fi.FullName, Encoding.UTF8).Replace("\r", "").Trim();
                return text.Length > 300 ? text.Substring(0, 300) : text;
            }
            catch { return ""; }
        }
    }
}
