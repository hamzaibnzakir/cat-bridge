using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamicIsland.FaceId;

/// <summary>What the camera saw in one frame (yaw &gt; 0 = nose toward image-right in the un-mirrored frame).</summary>
public sealed record FaceObs(DateTime T, int FaceCount, double Similarity, double Yaw, double CenterX, double CenterY, double EyeDist)
{
    public bool HasFace => FaceCount > 0;
}

public enum VerifyStep { FindFace, Identify, Turn, ReturnCenter, Confirm, Passed, AttemptFailed, NeedPassword }

/// <summary>
/// Face check with a liveness challenge. Pure logic (no camera) so every scenario is unit-tested.
///  1. Identify: facing the camera, ≥3 consecutive frames at or above the match threshold.
///  2. Turn: a random direction; a flat photo can't turn its head.
///  3. Back to centre, then confirm identity again, so a different face can't be swapped in after the turn.
///  The same tracked face must stay in view throughout; a jump or a lost face restarts the check.
///  Two failed attempts → NeedPassword.
/// </summary>
public sealed class FaceVerifier
{
    public double Threshold = 0.42;
    public int MaxAttempts = 2;
    public bool FlipTurn;
    public double AttemptSeconds = 12, TurnSeconds = 6;
    public double MinEyeDist = 30, TurnYaw = 0.18;

    public int Attempt { get; private set; } = 1;
    public VerifyStep Step { get; private set; } = VerifyStep.FindFace;
    public string FailReason { get; private set; } = "";
    /// <summary>+1 = ask the person to turn to their left, −1 = their right.</summary>
    public int TurnDir { get; private set; }
    public bool TooFar { get; private set; }
    public bool TooMany { get; private set; }

    readonly Random _rng;
    DateTime _attemptStart = DateTime.MinValue, _stepStart, _lostSince = DateTime.MinValue;
    int _good, _bad;
    (double x, double y)? _last;

    public FaceVerifier(int? seed = null) { _rng = seed is int s ? new Random(s) : new Random(); TurnDir = _rng.Next(2) == 0 ? 1 : -1; }

    public void NextAttempt()
    {
        if (Step != VerifyStep.AttemptFailed) return;
        Attempt++;
        if (Attempt > MaxAttempts) { Step = VerifyStep.NeedPassword; return; }
        TurnDir = _rng.Next(2) == 0 ? 1 : -1;
        Go(VerifyStep.FindFace, DateTime.MinValue);
        _attemptStart = DateTime.MinValue;
    }

    public VerifyStep Feed(FaceObs o)
    {
        if (Step is VerifyStep.Passed or VerifyStep.AttemptFailed or VerifyStep.NeedPassword) return Step;
        if (_attemptStart == DateTime.MinValue) { _attemptStart = o.T; _stepStart = o.T; }
        if ((o.T - _attemptStart).TotalSeconds > AttemptSeconds) return Fail("Ran out of time");

        TooMany = o.FaceCount > 1;
        TooFar = o.HasFace && o.EyeDist < MinEyeDist;

        if (!o.HasFace)
        {
            if (_lostSince == DateTime.MinValue) _lostSince = o.T;
            if (Step != VerifyStep.FindFace && (o.T - _lostSince).TotalSeconds > 1.2) Go(VerifyStep.FindFace, o.T);
            return Step;
        }
        _lostSince = DateTime.MinValue;
        if (TooMany || TooFar) { _good = 0; return Step; }

        // Tracking: a sudden jump means a different face or photo was swapped in. Start over.
        if (_last is (double lx, double ly) && Step != VerifyStep.FindFace)
        {
            double jump = Math.Sqrt(Math.Pow(o.CenterX - lx, 2) + Math.Pow(o.CenterY - ly, 2));
            if (jump > o.EyeDist * 1.6) { _last = (o.CenterX, o.CenterY); Go(VerifyStep.Identify, o.T); return Step; }
        }
        _last = (o.CenterX, o.CenterY);

        int want = FlipTurn ? -TurnDir : TurnDir;
        switch (Step)
        {
            case VerifyStep.FindFace:
                Go(VerifyStep.Identify, o.T);
                break;

            case VerifyStep.Identify:
                if (Math.Abs(o.Yaw) > 0.15) { _good = 0; break; }
                if (o.Similarity >= Threshold) { _bad = 0; if (++_good >= 3) Go(VerifyStep.Turn, o.T); }
                else { _good = 0; if (o.Similarity < Threshold - 0.12 && ++_bad >= 12) return Fail("That doesn't look like you"); }
                break;

            case VerifyStep.Turn:
                if ((o.T - _stepStart).TotalSeconds > TurnSeconds) return Fail("I didn't see you turn");
                if (o.Yaw * want >= TurnYaw && o.Similarity >= Threshold * 0.7) { if (++_good >= 2) Go(VerifyStep.ReturnCenter, o.T); }
                else _good = 0;
                break;

            case VerifyStep.ReturnCenter:
                if ((o.T - _stepStart).TotalSeconds > TurnSeconds) return Fail("I lost you");
                if (Math.Abs(o.Yaw) < 0.1) Go(VerifyStep.Confirm, o.T);
                break;

            case VerifyStep.Confirm:
                if ((o.T - _stepStart).TotalSeconds > 4) return Fail("That doesn't look like you");
                if (o.Similarity >= Threshold && Math.Abs(o.Yaw) < 0.15) { if (++_good >= 2) Go(VerifyStep.Passed, o.T); }
                else _good = 0;
                break;
        }
        return Step;
    }

    VerifyStep Fail(string why) { FailReason = why; Step = VerifyStep.AttemptFailed; return Step; }
    void Go(VerifyStep s, DateTime t) { Step = s; _stepStart = t; _good = 0; _bad = 0; }

    /// <summary>Best similarity against all enrolled embeddings.</summary>
    public static double Best(float[] probe, IReadOnlyList<float[]> enrolled)
    {
        double best = -1;
        foreach (var e in enrolled) best = Math.Max(best, FaceEngine.Cosine(probe, e));
        return best;
    }
}

public enum EnrollStep { Straight, SideA, SideB, Done }

/// <summary>Collects a handful of embeddings: facing the camera, then a little to each side.</summary>
public sealed class FaceEnroller
{
    public const int StraightCount = 5, SideCount = 3;
    public EnrollStep Step { get; private set; } = EnrollStep.Straight;
    public List<float[]> Embeddings { get; } = new();
    public bool TooFar { get; private set; }
    public bool TooMany { get; private set; }
    public bool NotSamePerson { get; private set; }
    public double Progress => Math.Min(1, Embeddings.Count / (double)(StraightCount + 2 * SideCount));
    int _sideASign;
    DateTime _lastTake = DateTime.MinValue;

    public EnrollStep Feed(FaceObs o, float[]? emb)
    {
        if (Step == EnrollStep.Done) return Step;
        TooMany = o.FaceCount > 1;
        TooFar = o.HasFace && o.EyeDist < 34;
        NotSamePerson = false;
        if (!o.HasFace || TooMany || TooFar || emb == null) return Step;
        if ((o.T - _lastTake).TotalMilliseconds < 220) return Step; // spread samples over time

        // everything after the first sample must be the same person
        if (Embeddings.Count > 0)
        {
            double sim = FaceVerifier.Best(emb, Embeddings.Take(StraightCount).ToList());
            if (sim < 0.5) { NotSamePerson = true; return Step; }
        }

        switch (Step)
        {
            case EnrollStep.Straight:
                if (Math.Abs(o.Yaw) < 0.08) Take(o, emb);
                if (Embeddings.Count >= StraightCount) Step = EnrollStep.SideA;
                break;
            case EnrollStep.SideA:
                if (Math.Abs(o.Yaw) >= 0.12 && Math.Abs(o.Yaw) <= 0.45 && (_sideASign == 0 || Math.Sign(o.Yaw) == _sideASign)) { _sideASign = Math.Sign(o.Yaw); Take(o, emb); }
                if (Embeddings.Count >= StraightCount + SideCount) Step = EnrollStep.SideB;
                break;
            case EnrollStep.SideB:
                if (Math.Sign(o.Yaw) == -_sideASign && Math.Abs(o.Yaw) >= 0.12 && Math.Abs(o.Yaw) <= 0.45) Take(o, emb);
                if (Embeddings.Count >= StraightCount + 2 * SideCount) Step = EnrollStep.Done;
                break;
        }
        return Step;
    }

    void Take(FaceObs o, float[] e) { Embeddings.Add(e); _lastTake = o.T; }

    public void Reset() { Embeddings.Clear(); Step = EnrollStep.Straight; _lastTake = DateTime.MinValue; _sideASign = 0; }
}
