using System.Collections.Generic;
using UnityEngine;

namespace TeamVR.Experiment
{
    // Fixed, hand-tunable projectile schedule for one round: which beat each
    // entry spawns on, from which of the 9 direction spawn points, at what
    // speed, and whether it's a target or a bomb. Never references the
    // AdaptivePassthrough risk pipeline. Every participant who gets the same
    // condition sees the exact same schedule - entries are authored data,
    // never generated at runtime. Timing/count structure (5s beat, escalating
    // simultaneous bursts) is shared across conditions; only direction (and
    // the type mix layout) differs per condition asset.
    [CreateAssetMenu(
        fileName = "ProjectileScheduleSet",
        menuName = "Experiment/Projectile Schedule Set")]
    public sealed class ProjectileScheduleSet : ScriptableObject
    {
        [SerializeField] private List<BallSpawnEntry> entries =
            new List<BallSpawnEntry>();

        public IReadOnlyList<BallSpawnEntry> Entries => entries;

        // Editor/authoring helper only - lets tooling seed a schedule when
        // the asset is first created. Safe to call at runtime too since it
        // is just data assignment, but round data should stay fixed once
        // the experiment starts.
        public void SetEntries(IEnumerable<BallSpawnEntry> newEntries)
        {
            entries.Clear();
            entries.AddRange(newEntries);
        }
    }
}
