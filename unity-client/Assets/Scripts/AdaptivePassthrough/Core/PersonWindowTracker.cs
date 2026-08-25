using System;
using System.Collections.Generic;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public interface IPersonWindowSnapshotProvider
    {
        bool TryGetPersonWindow(
            int trackId,
            out Rect rect,
            out float opacity);
    }

    public interface IPassthroughPresentationSnapshotProvider
    {
        PassthroughPresentationSnapshot GetPresentationSnapshot();
    }

    public interface IPassthroughVisibilityEventSource
    {
        event Action<bool, string, double> VisibilityChanged;
    }

    public readonly struct PassthroughPresentationSnapshot
    {
        public readonly bool AnyVisible;
        public readonly bool StaticVisible;
        public readonly bool DynamicVisible;
        public readonly int PersonWindowCount;
        public readonly string VisibilitySource;
        public readonly float HoldRemainingSeconds;
        public readonly double TimestampSeconds;

        public PassthroughPresentationSnapshot(
            bool anyVisible,
            bool staticVisible,
            bool dynamicVisible,
            int personWindowCount,
            string visibilitySource,
            float holdRemainingSeconds,
            double timestampSeconds)
        {
            AnyVisible = anyVisible;
            StaticVisible = staticVisible;
            DynamicVisible = dynamicVisible;
            PersonWindowCount = Mathf.Max(0, personWindowCount);
            VisibilitySource = visibilitySource ?? "none";
            HoldRemainingSeconds = Mathf.Max(0f, holdRemainingSeconds);
            TimestampSeconds = Math.Max(0.0, timestampSeconds);
        }
    }

    public sealed class PersonWindowSnapshot
    {
        public readonly int TrackId;
        public readonly Rect Rect;
        public readonly float Opacity;
        public readonly float Risk;
        public readonly bool ObservedThisFrame;
        public readonly float HoldRemainingSeconds;
        public readonly float PredictionAgeSeconds;

        public PersonWindowSnapshot(
            int trackId,
            Rect rect,
            float opacity,
            float risk,
            bool observedThisFrame,
            float holdRemainingSeconds = 0f,
            float predictionAgeSeconds = 0f)
        {
            TrackId = trackId;
            Rect = rect;
            Opacity = Mathf.Clamp01(opacity);
            Risk = Mathf.Clamp01(risk);
            ObservedThisFrame = observedThisFrame;
            HoldRemainingSeconds = Mathf.Max(0f, holdRemainingSeconds);
            PredictionAgeSeconds = Mathf.Max(0f, predictionAgeSeconds);
        }
    }

    public sealed class PersonWindowTracker
    {
        private sealed class State
        {
            public int TrackId;
            public Rect CurrentRect;
            public Rect TargetRect;
            public float Opacity;
            public float Risk;
            public double LastObservedSeconds;
            public bool ObservedThisFrame;
            public Vector2 CenterVelocity;
            public Vector2 SizeVelocity;
            public float PredictionAgeSeconds;
        }

        private readonly Dictionary<int, State> states =
            new Dictionary<int, State>();
        private readonly float positionSmoothingSeconds;
        private readonly float sizeSmoothingSeconds;
        private readonly float fadeInSeconds;
        private readonly float lostHoldSeconds;
        private readonly float fadeOutSeconds;
        private readonly float maximumPredictionSeconds;
        private readonly float maximumViewportSpeed;
        private readonly List<int> removalBuffer = new List<int>();
        private readonly List<PersonWindowSnapshot> snapshotBuffer =
            new List<PersonWindowSnapshot>();
        private double lastUpdateSeconds;

        public PersonWindowTracker(
            float positionSmoothingSeconds = 0.15f,
            float sizeSmoothingSeconds = 0.25f,
            float fadeInSeconds = 0.20f,
            float lostHoldSeconds = 0.60f,
            float fadeOutSeconds = 0.30f,
            float maximumPredictionSeconds = 0.50f,
            float maximumViewportSpeed = 1.50f)
        {
            this.positionSmoothingSeconds = Mathf.Max(
                0.001f,
                positionSmoothingSeconds);
            this.sizeSmoothingSeconds = Mathf.Max(
                0.001f,
                sizeSmoothingSeconds);
            this.fadeInSeconds = Mathf.Max(0.001f, fadeInSeconds);
            this.lostHoldSeconds = Mathf.Max(0f, lostHoldSeconds);
            this.fadeOutSeconds = Mathf.Max(0.001f, fadeOutSeconds);
            this.maximumPredictionSeconds = Mathf.Max(
                0f,
                maximumPredictionSeconds);
            this.maximumViewportSpeed = Mathf.Max(
                0.01f,
                maximumViewportSpeed);
        }

        public void BeginFrame()
        {
            foreach (State state in states.Values)
            {
                state.ObservedThisFrame = false;
            }
        }

        public void Observe(
            int trackId,
            Rect targetRect,
            float risk,
            double timestampSeconds)
        {
            State state;
            if (!states.TryGetValue(trackId, out state))
            {
                state = new State
                {
                    TrackId = trackId,
                    CurrentRect = targetRect,
                    TargetRect = targetRect,
                    Opacity = 0f
                };
                states.Add(trackId, state);
            }
            else
            {
                float elapsed = (float)Math.Max(
                    0.0,
                    timestampSeconds - state.LastObservedSeconds);
                if (elapsed > 0.001f && elapsed <= 1.2f)
                {
                    Vector2 predictedCenter = state.TargetRect.center
                        + state.CenterVelocity * elapsed;
                    Vector2 predictedSize = state.TargetRect.size
                        + state.SizeVelocity * elapsed;
                    Vector2 centerResidual = targetRect.center
                        - predictedCenter;
                    Vector2 sizeResidual = targetRect.size
                        - predictedSize;
                    Vector2 measuredCenterVelocity =
                        (targetRect.center - state.TargetRect.center)
                        / elapsed;
                    measuredCenterVelocity = Vector2.ClampMagnitude(
                        measuredCenterVelocity,
                        maximumViewportSpeed);
                    state.CenterVelocity = Vector2.ClampMagnitude(
                        state.CenterVelocity * 0.55f
                        + measuredCenterVelocity * 0.45f
                        + centerResidual * (0.15f / elapsed),
                        maximumViewportSpeed);
                    state.SizeVelocity = Vector2.ClampMagnitude(
                        state.SizeVelocity * 0.60f
                        + (targetRect.size - state.TargetRect.size)
                            / elapsed * 0.40f
                        + sizeResidual * (0.10f / elapsed),
                        maximumViewportSpeed);
                }
            }

            state.TargetRect = targetRect;
            state.Risk = Mathf.Clamp01(risk);
            state.LastObservedSeconds = timestampSeconds;
            state.ObservedThisFrame = true;
        }

        public void Update(double timestampSeconds, float deltaTime)
        {
            lastUpdateSeconds = Math.Max(0.0, timestampSeconds);
            float safeDelta = Mathf.Max(0f, deltaTime);
            float positionAlpha =
                1f - Mathf.Exp(-safeDelta / positionSmoothingSeconds);
            float sizeAlpha =
                1f - Mathf.Exp(-safeDelta / sizeSmoothingSeconds);
            removalBuffer.Clear();

            foreach (State state in states.Values)
            {
                double age = Math.Max(
                    0.0,
                    timestampSeconds - state.LastObservedSeconds);
                float predictionAge = Mathf.Min(
                    maximumPredictionSeconds,
                    (float)age);
                state.PredictionAgeSeconds = predictionAge;
                Rect predictedRect = state.TargetRect;
                if (!state.ObservedThisFrame && predictionAge > 0f)
                {
                    Vector2 size = state.TargetRect.size
                        + state.SizeVelocity * predictionAge;
                    size = new Vector2(
                        Mathf.Clamp(size.x, 0.01f, 1f),
                        Mathf.Clamp(size.y, 0.01f, 1f));
                    Vector2 center = state.TargetRect.center
                        + state.CenterVelocity * predictionAge;
                    center = new Vector2(
                        Mathf.Clamp(center.x, size.x * 0.5f, 1f - size.x * 0.5f),
                        Mathf.Clamp(center.y, size.y * 0.5f, 1f - size.y * 0.5f));
                    predictedRect = new Rect(center - size * 0.5f, size);
                }

                state.CurrentRect = SmoothRect(
                    state.CurrentRect,
                    predictedRect,
                    positionAlpha,
                    sizeAlpha);
                if (state.ObservedThisFrame)
                {
                    state.Opacity = Mathf.MoveTowards(
                        state.Opacity,
                        1f,
                        safeDelta / fadeInSeconds);
                    continue;
                }

                if (age <= lostHoldSeconds)
                {
                    state.Opacity = Mathf.MoveTowards(
                        state.Opacity,
                        1f,
                        safeDelta / fadeInSeconds);
                    continue;
                }

                state.Opacity = Mathf.MoveTowards(
                    state.Opacity,
                    0f,
                    safeDelta / fadeOutSeconds);
                if (state.Opacity <= 0f)
                {
                    removalBuffer.Add(state.TrackId);
                }
            }

            for (int i = 0; i < removalBuffer.Count; i++)
            {
                states.Remove(removalBuffer[i]);
            }
        }

        public IReadOnlyList<PersonWindowSnapshot> GetSnapshots(
            int maximumCount)
        {
            snapshotBuffer.Clear();
            foreach (State state in states.Values)
            {
                if (state.Opacity <= 0f)
                {
                    continue;
                }

                snapshotBuffer.Add(new PersonWindowSnapshot(
                    state.TrackId,
                    state.CurrentRect,
                    state.Opacity,
                    state.Risk,
                    state.ObservedThisFrame,
                    HoldRemaining(state, lastUpdateSeconds),
                    state.PredictionAgeSeconds));
            }

            snapshotBuffer.Sort(CompareSnapshots);
            int safeMaximum = Math.Max(0, maximumCount);
            if (snapshotBuffer.Count > safeMaximum)
            {
                snapshotBuffer.RemoveRange(
                    safeMaximum,
                    snapshotBuffer.Count - safeMaximum);
            }

            return snapshotBuffer;
        }

        public bool TryGetSnapshot(
            int trackId,
            out PersonWindowSnapshot snapshot)
        {
            State state;
            if (states.TryGetValue(trackId, out state)
                && state.Opacity > 0f)
            {
                snapshot = new PersonWindowSnapshot(
                    state.TrackId,
                    state.CurrentRect,
                    state.Opacity,
                    state.Risk,
                    state.ObservedThisFrame,
                    HoldRemaining(state, lastUpdateSeconds),
                    state.PredictionAgeSeconds);
                return true;
            }

            snapshot = null;
            return false;
        }

        public float GetMaximumHoldRemainingSeconds(double timestampSeconds)
        {
            float maximum = 0f;
            foreach (State state in states.Values)
            {
                if (state.Opacity > 0f)
                {
                    maximum = Mathf.Max(
                        maximum,
                        HoldRemaining(state, timestampSeconds));
                }
            }

            return maximum;
        }

        private float HoldRemaining(State state, double timestampSeconds)
        {
            double age = Math.Max(
                0.0,
                timestampSeconds - state.LastObservedSeconds);
            return Mathf.Max(0f, lostHoldSeconds - (float)age);
        }

        public void Reset()
        {
            states.Clear();
            snapshotBuffer.Clear();
            removalBuffer.Clear();
            lastUpdateSeconds = 0.0;
        }

        private static Rect SmoothRect(
            Rect current,
            Rect target,
            float positionAlpha,
            float sizeAlpha)
        {
            Vector2 center = Vector2.Lerp(
                current.center,
                target.center,
                Mathf.Clamp01(positionAlpha));
            Vector2 size = Vector2.Lerp(
                current.size,
                target.size,
                Mathf.Clamp01(sizeAlpha));
            return new Rect(center - size * 0.5f, size);
        }

        private static int CompareSnapshots(
            PersonWindowSnapshot left,
            PersonWindowSnapshot right)
        {
            int observed = right.ObservedThisFrame.CompareTo(
                left.ObservedThisFrame);
            if (observed != 0)
            {
                return observed;
            }

            int risk = right.Risk.CompareTo(left.Risk);
            return risk != 0
                ? risk
                : right.Opacity.CompareTo(left.Opacity);
        }
    }
}
