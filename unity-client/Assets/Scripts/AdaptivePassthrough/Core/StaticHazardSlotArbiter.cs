using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public readonly struct StaticHazardSlotCandidate
    {
        public readonly StaticHazardKey Key;
        public readonly bool Active;
        public readonly bool Emergency;
        public readonly float Risk;
        public readonly float TtcRisk;
        public readonly float ClosingSpeedMetersPerSecond;

        public StaticHazardSlotCandidate(
            StaticHazardKey key,
            bool active,
            bool emergency,
            float risk,
            float ttcRisk,
            float closingSpeedMetersPerSecond)
        {
            Key = key;
            Active = active && key != StaticHazardKey.None;
            Emergency = emergency;
            Risk = Mathf.Clamp01(risk);
            TtcRisk = Mathf.Clamp01(ttcRisk);
            ClosingSpeedMetersPerSecond = Mathf.Max(
                0f,
                closingSpeedMetersPerSecond);
        }
    }

    /// <summary>
    /// Stable two-slot selector shared by production presentation and tests.
    /// It preserves assignments and applies a risk-margin dwell before a
    /// non-emergency candidate may displace an incumbent.
    /// </summary>
    public sealed class StaticHazardSlotArbiter
    {
        private readonly StaticHazardKey[] keys;
        private readonly float replacementRiskMargin;
        private readonly float replacementConfirmSeconds;
        private StaticHazardKey pendingCandidate;
        private StaticHazardKey pendingIncumbent;
        private double pendingSince;

        public StaticHazardSlotArbiter(
            int slotCount = 2,
            float replacementRiskMargin = 0.10f,
            float replacementConfirmSeconds = 0.30f)
        {
            keys = new StaticHazardKey[Mathf.Clamp(slotCount, 1, 2)];
            this.replacementRiskMargin = Mathf.Max(
                0f,
                replacementRiskMargin);
            this.replacementConfirmSeconds = Mathf.Max(
                0f,
                replacementConfirmSeconds);
        }

        public int SlotCount => keys.Length;
        public string LastReplacementReason { get; private set; } = "none";

        public StaticHazardKey GetKey(int index)
        {
            return index >= 0 && index < keys.Length
                ? keys[index]
                : StaticHazardKey.None;
        }

        public void Update(
            StaticHazardSlotCandidate[] candidates,
            int candidateCount,
            double timestampSeconds)
        {
            int count = candidates == null
                ? 0
                : Mathf.Clamp(candidateCount, 0, candidates.Length);
            double now = Math.Max(0.0, timestampSeconds);
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] != StaticHazardKey.None
                    && Find(candidates, count, keys[i]) < 0)
                {
                    keys[i] = StaticHazardKey.None;
                }
            }

            while (true)
            {
                int empty = FindEmptySlot();
                if (empty < 0)
                {
                    break;
                }

                int best = FindBestUnassigned(candidates, count);
                if (best < 0)
                {
                    break;
                }

                keys[empty] = candidates[best].Key;
                LastReplacementReason = "empty-slot";
            }

            int challengerIndex = FindBestUnassigned(candidates, count);
            if (challengerIndex < 0)
            {
                ClearPending();
                return;
            }

            int incumbentSlot = FindWorstAssigned(candidates, count);
            if (incumbentSlot < 0)
            {
                ClearPending();
                return;
            }

            StaticHazardSlotCandidate challenger =
                candidates[challengerIndex];
            int incumbentIndex = Find(
                candidates,
                count,
                keys[incumbentSlot]);
            if (incumbentIndex < 0)
            {
                keys[incumbentSlot] = challenger.Key;
                LastReplacementReason = "empty-slot";
                ClearPending();
                return;
            }

            StaticHazardSlotCandidate incumbent = candidates[incumbentIndex];
            if (challenger.Emergency && !incumbent.Emergency)
            {
                keys[incumbentSlot] = challenger.Key;
                LastReplacementReason = "emergency";
                ClearPending();
                return;
            }

            if (challenger.Risk
                < incumbent.Risk + replacementRiskMargin)
            {
                ClearPending();
                return;
            }

            if (pendingCandidate != challenger.Key
                || pendingIncumbent != incumbent.Key)
            {
                pendingCandidate = challenger.Key;
                pendingIncumbent = incumbent.Key;
                pendingSince = now;
                return;
            }

            if (now - pendingSince < replacementConfirmSeconds)
            {
                return;
            }

            keys[incumbentSlot] = challenger.Key;
            LastReplacementReason = "confirmed-margin";
            ClearPending();
        }

        public void Reset()
        {
            Array.Clear(keys, 0, keys.Length);
            LastReplacementReason = "none";
            ClearPending();
        }

        public static int Compare(
            StaticHazardSlotCandidate left,
            StaticHazardSlotCandidate right)
        {
            int emergency = right.Emergency.CompareTo(left.Emergency);
            if (emergency != 0)
            {
                return emergency;
            }

            int risk = right.Risk.CompareTo(left.Risk);
            if (risk != 0)
            {
                return risk;
            }

            int ttc = right.TtcRisk.CompareTo(left.TtcRisk);
            if (ttc != 0)
            {
                return ttc;
            }

            int closing = right.ClosingSpeedMetersPerSecond.CompareTo(
                left.ClosingSpeedMetersPerSecond);
            return closing != 0 ? closing : left.Key.CompareTo(right.Key);
        }

        private int FindEmptySlot()
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == StaticHazardKey.None)
                {
                    return i;
                }
            }
            return -1;
        }

        private int FindBestUnassigned(
            StaticHazardSlotCandidate[] candidates,
            int count)
        {
            int selected = -1;
            for (int i = 0; i < count; i++)
            {
                if (!candidates[i].Active || IsAssigned(candidates[i].Key))
                {
                    continue;
                }

                if (selected < 0
                    || Compare(candidates[i], candidates[selected]) < 0)
                {
                    selected = i;
                }
            }
            return selected;
        }

        private int FindWorstAssigned(
            StaticHazardSlotCandidate[] candidates,
            int count)
        {
            int selectedSlot = -1;
            int selectedCandidate = -1;
            for (int slot = 0; slot < keys.Length; slot++)
            {
                int index = Find(candidates, count, keys[slot]);
                if (index < 0)
                {
                    return slot;
                }

                if (selectedCandidate < 0
                    || Compare(candidates[index],
                        candidates[selectedCandidate]) > 0)
                {
                    selectedSlot = slot;
                    selectedCandidate = index;
                }
            }
            return selectedSlot;
        }

        private bool IsAssigned(StaticHazardKey key)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == key)
                {
                    return true;
                }
            }
            return false;
        }

        private static int Find(
            StaticHazardSlotCandidate[] candidates,
            int count,
            StaticHazardKey key)
        {
            for (int i = 0; i < count; i++)
            {
                if (candidates[i].Active && candidates[i].Key == key)
                {
                    return i;
                }
            }
            return -1;
        }

        private void ClearPending()
        {
            pendingCandidate = StaticHazardKey.None;
            pendingIncumbent = StaticHazardKey.None;
            pendingSince = 0.0;
        }
    }
}
