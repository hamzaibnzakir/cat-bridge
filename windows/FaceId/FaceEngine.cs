using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace DynamicIsland.FaceId;

public sealed record DetectedFace(Rect2f Box, Point2f[] Landmarks, float Score)
{
    public Point2f RightEye => Landmarks[0]; // the person's right eye (image left)
    public Point2f LeftEye => Landmarks[1];
    public Point2f Nose => Landmarks[2];
    public float EyeDistance => MathF.Max(1, MathF.Abs(LeftEye.X - RightEye.X));
}

/// <summary>
/// Face detection (YuNet) + face embeddings (SFace), both from the OpenCV model zoo.
/// Alignment reproduces OpenCV's FaceRecognizerSF.alignCrop (least-squares similarity onto the 112×112 template).
/// Not thread-safe: use one engine per thread.
/// </summary>
public sealed class FaceEngine : IDisposable
{
    public const string DetectorFile = "face_detection_yunet_2023mar.onnx";
    public const string RecognizerFile = "face_recognition_sface_2021dec.onnx";

    static readonly Point2f[] Template =
    {
        new(38.2946f, 51.6963f), new(73.5318f, 51.5014f), new(56.0252f, 71.7366f),
        new(41.5493f, 92.3655f), new(70.7299f, 92.2041f),
    };

    readonly string _detectorPath;
    readonly Net _recognizer;
    FaceDetectorYN? _detector;
    Size _detectorSize;

    public FaceEngine(string modelDir)
    {
        _detectorPath = System.IO.Path.Combine(modelDir, DetectorFile);
        _recognizer = CvDnn.ReadNetFromOnnx(System.IO.Path.Combine(modelDir, RecognizerFile))
            ?? throw new InvalidOperationException("Couldn't load the face recognition model");
    }

    /// <summary>Faces in a BGR frame, largest first. Runs on a ≤640 px copy for speed.</summary>
    public List<DetectedFace> Detect(Mat bgr, float minScore = 0.9f)
    {
        var list = new List<DetectedFace>();
        double scale = bgr.Width > 640 ? 640.0 / bgr.Width : 1.0;
        using var small = scale < 1 ? bgr.Resize(new Size(Math.Round(bgr.Width * scale), Math.Round(bgr.Height * scale))) : bgr.Clone();
        if (_detector == null || _detectorSize != small.Size())
        {
            _detector?.Dispose();
            _detectorSize = small.Size();
            _detector = FaceDetectorYN.Create(_detectorPath, "", _detectorSize, minScore, 0.3f, 20, 0, 0);
        }
        using var faces = new Mat();
        _detector.Detect(small, faces);
        float inv = (float)(1 / scale);
        for (int i = 0; i < faces.Rows; i++)
        {
            float G(int c) => faces.At<float>(i, c);
            if (G(14) < minScore) continue;
            var lm = new Point2f[5];
            for (int k = 0; k < 5; k++) lm[k] = new Point2f(G(4 + 2 * k) * inv, G(5 + 2 * k) * inv);
            list.Add(new DetectedFace(new Rect2f(G(0) * inv, G(1) * inv, G(2) * inv, G(3) * inv), lm, G(14)));
        }
        list.Sort((a, b) => (b.Box.Width * b.Box.Height).CompareTo(a.Box.Width * a.Box.Height));
        return list;
    }

    /// <summary>L2-normalised 128-d identity embedding.</summary>
    public float[] Embed(Mat bgr, DetectedFace f)
    {
        using var m = Similarity(f.Landmarks, Template);
        using var aligned = new Mat();
        Cv2.WarpAffine(bgr, aligned, m, new Size(112, 112));
        using var blob = CvDnn.BlobFromImage(aligned, 1.0, new Size(112, 112), new Scalar(0, 0, 0), swapRB: true, crop: false);
        _recognizer.SetInput(blob);
        using var output = _recognizer.Forward();
        var v = new float[output.Total()];
        Marshal.Copy(output.Data, v, 0, v.Length);
        double n = 0; foreach (var x in v) n += x * x;
        n = Math.Sqrt(n);
        for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / n);
        return v;
    }

    public static double Cosine(float[] a, float[] b)
    {
        double d = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) d += a[i] * b[i];
        return d;
    }

    /// <summary>Head turn: + when the nose moves toward image-right, − toward image-left. ~0 facing the camera.</summary>
    public static double Yaw(DetectedFace f) => (f.Nose.X - (f.LeftEye.X + f.RightEye.X) / 2) / f.EyeDistance;

    /// Least-squares similarity transform (rotation + uniform scale + translation) mapping src onto dst.
    static Mat Similarity(Point2f[] src, Point2f[] dst)
    {
        int n = src.Length;
        double sx = 0, sy = 0, dx = 0, dy = 0;
        for (int i = 0; i < n; i++) { sx += src[i].X; sy += src[i].Y; dx += dst[i].X; dy += dst[i].Y; }
        sx /= n; sy /= n; dx /= n; dy /= n;
        double a = 0, b = 0, norm = 0;
        for (int i = 0; i < n; i++)
        {
            double px = src[i].X - sx, py = src[i].Y - sy, qx = dst[i].X - dx, qy = dst[i].Y - dy;
            a += px * qx + py * qy;
            b += px * qy - py * qx;
            norm += px * px + py * py;
        }
        double c = a / norm, s = b / norm;
        double tx = dx - (c * sx - s * sy), ty = dy - (s * sx + c * sy);
        var m = new Mat(2, 3, MatType.CV_64FC1);
        m.Set(0, 0, c); m.Set(0, 1, -s); m.Set(0, 2, tx);
        m.Set(1, 0, s); m.Set(1, 1, c); m.Set(1, 2, ty);
        return m;
    }

    public void Dispose() { _detector?.Dispose(); _recognizer.Dispose(); }
}
