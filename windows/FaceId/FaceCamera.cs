using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace DynamicIsland.FaceId;

/// <param name="Ready">False while the face models are still loading (preview only).</param>
public sealed record CameraFrame(byte[]? Pixels, int Width, int Height, int Stride, FaceObs Obs, float[]? Embedding, bool Ready);

/// <summary>
/// Webcam + face engine on a background thread (~15 fps). The camera opens while the models load in
/// parallel, so the preview appears right away. The preview is mirrored; the analysis isn't.
/// </summary>
public sealed class FaceCamera : IDisposable
{
    readonly int _index;
    readonly string _modelDir;
    readonly IReadOnlyList<float[]>? _enrolled;
    readonly bool _wantPreview;
    readonly Thread _thread;
    volatile bool _stop;

    public event Action<CameraFrame>? Frame; // background thread
    public event Action<string>? Error;      // background thread

    public FaceCamera(int index, string modelDir, IReadOnlyList<float[]>? enrolled, bool preview = true)
    {
        _index = index; _modelDir = modelDir; _enrolled = enrolled; _wantPreview = preview;
        _thread = new Thread(Run) { IsBackground = true, Name = "FaceCamera" };
    }

    public void Start() => _thread.Start();

    /// <summary>Loads OpenCV and the models once in the background so the first real use starts fast.</summary>
    public static void Warm(string modelDir) => Task.Run(() =>
    {
        try { FaceLog.PrepareCamera(); using var e = new FaceEngine(modelDir); using var m = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(0)); e.Detect(m); }
        catch (Exception ex) { FaceLog.Write(ex); }
    });

    static VideoCapture? Open(int index)
    {
        FaceLog.PrepareCamera();
        foreach (var api in new[] { VideoCaptureAPIs.MSMF, VideoCaptureAPIs.DSHOW })
        {
            var cap = new VideoCapture(index, api);
            if (cap.IsOpened())
            {
                cap.Set(VideoCaptureProperties.FrameWidth, 640);
                cap.Set(VideoCaptureProperties.FrameHeight, 480);
                return cap;
            }
            cap.Dispose();
        }
        return null;
    }

    void Run()
    {
        var engineTask = Task.Run(() => new FaceEngine(_modelDir)); // loads while the camera opens
        FaceEngine? eng = null;
        VideoCapture? cap = null;
        try
        {
            cap = Open(_index);
            if (cap == null) { Error?.Invoke("I can't open your camera. Is another app using it, or is camera access turned off in Windows privacy settings?"); return; }

            using var frame = new Mat();
            using var mirrored = new Mat();
            var sw = Stopwatch.StartNew();
            int misses = 0;
            while (!_stop)
            {
                long start = sw.ElapsedMilliseconds;
                if (!cap.Read(frame) || frame.Empty())
                {
                    if (++misses > 90) { Error?.Invoke("The camera stopped sending pictures."); return; }
                    Thread.Sleep(20); continue;
                }
                misses = 0;

                if (eng == null && engineTask.IsCompleted)
                {
                    if (engineTask.IsFaulted) { Error?.Invoke("Couldn't load the face models: " + engineTask.Exception?.GetBaseException().Message); return; }
                    eng = engineTask.Result;
                }

                float[]? emb = null;
                var obs = new FaceObs(DateTime.Now, 0, 0, 0, 0, 0, 0);
                if (eng != null)
                {
                    var faces = eng.Detect(frame, 0.8f);
                    if (faces.Count > 0)
                    {
                        var f = faces[0];
                        emb = eng.Embed(frame, f);
                        double sim = _enrolled is { Count: > 0 } ? FaceVerifier.Best(emb, _enrolled) : 0;
                        obs = new FaceObs(DateTime.Now, faces.Count, sim, FaceEngine.Yaw(f), f.Box.X + f.Box.Width / 2, f.Box.Y + f.Box.Height / 2, f.EyeDistance);
                    }
                }

                byte[]? px = null; int stride = 0, w = frame.Cols, h = frame.Rows;
                if (_wantPreview)
                {
                    Cv2.Flip(frame, mirrored, FlipMode.Y);
                    stride = (int)mirrored.Step();
                    px = new byte[stride * mirrored.Rows];
                    Marshal.Copy(mirrored.Data, px, 0, px.Length);
                }
                Frame?.Invoke(new CameraFrame(px, w, h, stride, obs, emb, eng != null));

                int wait = 66 - (int)(sw.ElapsedMilliseconds - start);
                if (wait > 0) Thread.Sleep(wait);
            }
        }
        catch (Exception ex) { FaceLog.Write(ex); Error?.Invoke("Face unlock hit a problem: " + ex.Message); }
        finally
        {
            cap?.Release(); cap?.Dispose();
            if (eng != null) eng.Dispose();
            else engineTask.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose(); });
        }
    }

    public void Dispose() { _stop = true; }
}
