using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    /// <summary>
    /// Per-person hysteresis. Tracking observations always continue, while
    /// only force or risk-qualified observations renew the visual hold.
    /// </summary>
    public sealed class PersonRevealEligibilityTracker
    {
        public const float DefaultOnRisk = 0.60f;
        public const float DefaultOffRisk = 0.45f;

        private sealed class State
        {
            public bool Active;
            public bool LatestEligible;
        }

        private readonly Dictionary<int, State> states =
            new Dictionary<int, State>();
        private readonly HashSet<int> liveIds = new HashSet<int>();
        private readonly List<int> expiredIds = new List<int>();

        /// <summary>
        /// Compatibility overload. policyEnabled is intentionally ignored:
        /// global policy aggregates risk for fallback feedback, but must not
        /// block a force-qualified person window.
        /// </summary>
        public bool Evaluate(
            DynamicRiskAssessment assessment,
            bool featureEnabled,
            bool policyEnabled,
            float onRisk)
        {
            float safeOn = Clamp01(onRisk);
            return Evaluate(
                assessment,
                featureEnabled,
                safeOn,
                Math.Max(0f, safeOn - 0.15f));
        }

        public bool Evaluate(
            DynamicRiskAssessment assessment,
            bool featureEnabled,
            float onRisk,
            float offRisk)
        {
            if (assessment == null || !assessment.ObservedThisFrame)
            {
                return false;
            }

            State state = GetOrCreate(assessment.TrackId);
            if (!featureEnabled)
            {
                ResetState(state);
                return false;
            }

            if (assessment.ForcePassthrough)
            {
                state.Active = true;
                state.LatestEligible = true;
                return true;
            }

            float safeOn = Clamp01(onRisk);
            float safeOff = Math.Min(safeOn, Clamp01(offRisk));
            if (!state.Active)
            {
                state.Active = assessment.Score >= safeOn;
            }
            else if (assessment.Score < safeOff)
            {
                state.Active = false;
            }

            state.LatestEligible = state.Active;
            return state.LatestEligible;
        }

        public bool IsLatestEligible(int trackId)
        {
            return states.TryGetValue(trackId, out State state)
                && state.LatestEligible;
        }

        public bool TryGetLatestEligibility(int trackId, out bool eligible)
        {
            if (states.TryGetValue(trackId, out State state))
            {
                eligible = state.LatestEligible;
                return true;
            }

            eligible = false;
            return false;
        }

        public void PruneExcept(IReadOnlyList<int> liveTrackIds)
        {
            liveIds.Clear();
            if (liveTrackIds != null)
            {
                for (int i = 0; i < liveTrackIds.Count; i++)
                {
                    liveIds.Add(liveTrackIds[i]);
                }
            }

            expiredIds.Clear();
            foreach (int trackId in states.Keys)
            {
                if (!liveIds.Contains(trackId))
                {
                    expiredIds.Add(trackId);
                }
            }

            for (int i = 0; i < expiredIds.Count; i++)
            {
                states.Remove(expiredIds[i]);
            }
        }

        public void Reset()
        {
            states.Clear();
            liveIds.Clear();
            expiredIds.Clear();
        }

        private State GetOrCreate(int trackId)
        {
            if (!states.TryGetValue(trackId, out State state))
            {
                state = new State();
                states.Add(trackId, state);
            }
            return state;
        }

        private static void ResetState(State state)
        {
            state.Active = false;
            state.LatestEligible = false;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
