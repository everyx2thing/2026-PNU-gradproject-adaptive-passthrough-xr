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

    public sealed class PersonWindowSnapshot
    {
        public readonly int TrackId;
        public readonly Rect Rect;
        public readonly float Opacity;
        public readonly float Risk;
        public readonly bool ObservedThisFrame;

        public PersonWindowSnapshot(
            int trackId,
            Rect rect,
            float opacity,
            float risk,
            bool observedThisFrame)
        {
            TrackId = trackId;
            Rect = rect;
            Opacity = Mathf.Clamp01(opacity);
            Risk = Mathf.Clamp01(risk);
            ObservedThisFrame = observedThisFrame;
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
        }

        private readonly Dictionary<int, State> states =
            new Dictionary<int, State>();
        private readonly float positionSmoothingSeconds;
        private readonly float sizeSmoothingSeconds;
        private readonly float fadeInSeconds;
        private readonly float lostHoldSeconds;
        private readonly float fadeOutSeconds;
        private readonly List<int> removalBuffer = new List<int>();
        private readonly List<PersonWindowSnapshot> snapshotBuffer =
            new List<PersonWindowSnapshot>();

        public PersonWindowTracker(
            float positionSmoothingSeconds = 0.15f,
            float sizeSmoothingSeconds = 0.25f,
            float fadeInSeconds = 0.20f,
            float lostHoldSeconds = 0.60f,
            float fadeOutSeconds = 0.30f)
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

            state.TargetRect = targetRect;
            state.Risk = Mathf.Clamp01(risk);
            state.LastObservedSeconds = timestampSeconds;
            state.ObservedThisFrame = true;
        }

        public void Update(double timestampSeconds, float deltaTime)
        {
            float safeDelta = Mathf.Max(0f, deltaTime);
            float positionAlpha =
                1f - Mathf.Exp(-safeDelta / positionSmoothingSeconds);
            float sizeAlpha =
                1f - Mathf.Exp(-safeDelta / sizeSmoothingSeconds);
            removalBuffer.Clear();

            foreach (State state in states.Values)
            {
                state.CurrentRect = SmoothRect(
                    state.CurrentRect,
                    state.TargetRect,
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

                double age = Math.Max(
                    0.0,
                    timestampSeconds - state.LastObservedSeconds);
                if (age <= lostHoldSeconds)
                {
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
                    state.ObservedThisFrame));
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
                    state.ObservedThisFrame);
                return true;
            }

            snapshot = null;
            return false;
        }

        public void Reset()
        {
            states.Clear();
            snapshotBuffer.Clear();
            removalBuffer.Clear();
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
