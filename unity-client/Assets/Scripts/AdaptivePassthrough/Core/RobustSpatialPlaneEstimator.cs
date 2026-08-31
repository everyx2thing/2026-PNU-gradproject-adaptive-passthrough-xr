using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    /// <summary>
    /// Deterministic, allocation-free RANSAC plane fit for the small spatial
    /// probe clusters used by the Quest runtime and the Unity presentation lab.
    /// </summary>
    public static class RobustSpatialPlaneEstimator
    {
        public static bool TryFit(
            Vector3[] points,
            Vector3[] normals,
            int count,
            bool[] inliers,
            out Vector3 center,
            out Vector3 normal,
            out int inlierCount,
            int maximumCandidates = 32,
            float maximumResidualMeters = 0.04f,
            float maximumNormalAngleDegrees = 30f,
            float minimumInlierRatio = 0.60f)
        {
            center = Vector3.zero;
            normal = Vector3.zero;
            inlierCount = 0;
            if (points == null
                || normals == null
                || inliers == null
                || count < 3
                || points.Length < count
                || normals.Length < count
                || inliers.Length < count)
            {
                return false;
            }

            Array.Clear(inliers, 0, count);
            float normalDot = Mathf.Cos(
                Mathf.Clamp(maximumNormalAngleDegrees, 0f, 90f)
                * Mathf.Deg2Rad);
            float maximumResidual = Mathf.Max(0.0001f,
                maximumResidualMeters);
            int candidateCount = Mathf.Max(1, maximumCandidates);
            Vector3 bestNormal = Vector3.zero;
            Vector3 bestPoint = Vector3.zero;
            int bestCount = 0;
            float bestResidual = float.PositiveInfinity;

            for (int candidate = 0; candidate < candidateCount; candidate++)
            {
                int a = candidate % count;
                int b = (a + 1 + candidate * 5 % (count - 1)) % count;
                int c = (a + 1 + candidate * 11 % (count - 1)) % count;
                while (c == b)
                {
                    c = (c + 1) % count;
                    if (c == a)
                    {
                        c = (c + 1) % count;
                    }
                }

                Vector3 pa = points[a];
                Vector3 candidateNormal = Vector3.Cross(
                    points[b] - pa,
                    points[c] - pa);
                if (candidateNormal.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                candidateNormal.Normalize();
                int candidateInliers = 0;
                float residual = 0f;
                for (int i = 0; i < count; i++)
                {
                    float distance = Mathf.Abs(Vector3.Dot(
                        points[i] - pa,
                        candidateNormal));
                    Vector3 sampleNormal = normals[i];
                    bool normalCompatible = sampleNormal.sqrMagnitude
                            <= 0.0001f
                        || Mathf.Abs(Vector3.Dot(
                            sampleNormal.normalized,
                            candidateNormal)) >= normalDot;
                    if (distance <= maximumResidual && normalCompatible)
                    {
                        candidateInliers++;
                        residual += distance;
                    }
                }

                if (candidateInliers > bestCount
                    || candidateInliers == bestCount
                    && residual < bestResidual)
                {
                    bestCount = candidateInliers;
                    bestResidual = residual;
                    bestNormal = candidateNormal;
                    bestPoint = pa;
                }
            }

            // A controller fan commonly produces three collinear hit points.
            // In that case point triples cannot define a plane, but coherent
            // Environment Depth normals still can. Score those normals with
            // the same residual and agreement gates rather than dropping all
            // hand presentation geometry.
            int normalCandidateCount = Mathf.Min(count, candidateCount);
            for (int candidate = 0; candidate < normalCandidateCount;
                candidate++)
            {
                Vector3 candidateNormal = normals[candidate];
                if (candidateNormal.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                candidateNormal.Normalize();
                Vector3 point = points[candidate];
                int candidateInliers = 0;
                float residual = 0f;
                for (int i = 0; i < count; i++)
                {
                    float distance = Mathf.Abs(Vector3.Dot(
                        points[i] - point,
                        candidateNormal));
                    Vector3 sampleNormal = normals[i];
                    bool normalCompatible = sampleNormal.sqrMagnitude
                            <= 0.0001f
                        || Mathf.Abs(Vector3.Dot(
                            sampleNormal.normalized,
                            candidateNormal)) >= normalDot;
                    if (distance <= maximumResidual && normalCompatible)
                    {
                        candidateInliers++;
                        residual += distance;
                    }
                }

                if (candidateInliers > bestCount
                    || candidateInliers == bestCount
                    && residual < bestResidual)
                {
                    bestCount = candidateInliers;
                    bestResidual = residual;
                    bestNormal = candidateNormal;
                    bestPoint = point;
                }
            }

            int minimumInliers = Mathf.Max(
                3,
                Mathf.CeilToInt(count * Mathf.Clamp01(minimumInlierRatio)));
            if (bestCount < minimumInliers
                || bestNormal.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Vector3 normalSum = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                float distance = Mathf.Abs(Vector3.Dot(
                    points[i] - bestPoint,
                    bestNormal));
                Vector3 sampleNormal = normals[i];
                bool normalCompatible = sampleNormal.sqrMagnitude <= 0.0001f
                    || Mathf.Abs(Vector3.Dot(
                        sampleNormal.normalized,
                        bestNormal)) >= normalDot;
                if (distance > maximumResidual || !normalCompatible)
                {
                    continue;
                }

                inliers[i] = true;
                center += points[i];
                if (sampleNormal.sqrMagnitude > 0.0001f)
                {
                    Vector3 aligned = sampleNormal.normalized;
                    if (Vector3.Dot(aligned, bestNormal) < 0f)
                    {
                        aligned = -aligned;
                    }
                    normalSum += aligned;
                }
                inlierCount++;
            }

            if (inlierCount < minimumInliers)
            {
                Array.Clear(inliers, 0, count);
                center = Vector3.zero;
                return false;
            }

            center /= inlierCount;
            normal = normalSum.sqrMagnitude > 0.0001f
                ? Vector3.Slerp(bestNormal, normalSum.normalized, 0.65f)
                    .normalized
                : bestNormal;
            return true;
        }
    }
}
