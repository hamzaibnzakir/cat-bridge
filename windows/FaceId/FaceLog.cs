using System;

namespace DynamicIsland.FaceId;

/// <summary>Logging hook so the face code runs both inside the app and in FaceHelper.exe.</summary>
public static class FaceLog
{
    public static Action<Exception?>? Sink;
    public static void Write(Exception? ex) { try { Sink?.Invoke(ex); } catch { } }

    /// Must run before the first OpenCV call: Windows' camera layer opens much faster without hardware transforms.
    public static void PrepareCamera() => Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");
}
