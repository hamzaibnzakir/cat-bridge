using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using DynamicIsland.Shared;

namespace DynamicIsland;

/// <summary>Talks to the Cat Face sign-in tile's pipe (only there while the PC is locked).</summary>
public static class PhoneUnlockRelay
{
    /// Sends one line and returns the tile's one-line answer, or null if the lock screen isn't up.
    public static async Task<string?> Ask(string line, int timeoutMs = 1500)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PhoneTrust.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutMs);
            var w = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var r = new StreamReader(pipe, new UTF8Encoding(false));
            await w.WriteLineAsync(line);
            var read = r.ReadLineAsync();
            return await Task.WhenAny(read, Task.Delay(timeoutMs)) == read ? await read : null;
        }
        catch (TimeoutException) { return null; }
        catch (IOException) { return null; }
        catch (Exception ex) { App.Log(ex); return null; }
    }
}
