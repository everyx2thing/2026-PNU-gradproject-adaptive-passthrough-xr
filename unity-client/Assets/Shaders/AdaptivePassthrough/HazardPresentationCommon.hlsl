#ifndef TEAMVR_HAZARD_PRESENTATION_COMMON_INCLUDED
#define TEAMVR_HAZARD_PRESENTATION_COMMON_INCLUDED

float3 HazardWorldPosition(
    float2 uv,
    float3 bottomLeft,
    float3 bottomRight,
    float3 topRight,
    float3 topLeft)
{
    float3 bottom = lerp(bottomLeft, bottomRight, uv.x);
    float3 top = lerp(topLeft, topRight, uv.x);
    return lerp(bottom, top, uv.y);
}

float HazardRectangleInsideDistance(float2 uv, float aspect)
{
    float2 edge = min(uv, 1.0 - uv);
    float safeAspect = max(aspect, 0.001);
    float physicalDistance = min(edge.x * safeAspect, edge.y);
    return physicalDistance / max(min(safeAspect, 1.0), 0.001);
}

float HazardCapsuleInsideDistance(float2 uv, float aspect)
{
    float safeAspect = max(aspect, 0.001);
    float2 capsulePoint = float2(
        (uv.x - 0.5) * safeAspect,
        uv.y - 0.5);
    float radius = min(0.5, safeAspect * 0.5);
    float halfSegment = max(0.0, 0.5 - radius);
    capsulePoint.y -= clamp(
        capsulePoint.y,
        -halfSegment,
        halfSegment);
    float signedDistance = length(capsulePoint) - radius;
    return -signedDistance / max(min(safeAspect, 1.0), 0.001);
}

float HazardInsideDistance(float2 uv, float shape, float aspect)
{
    return shape > 0.5 && shape < 1.5
        ? HazardCapsuleInsideDistance(uv, aspect)
        : HazardRectangleInsideDistance(uv, aspect);
}

float HazardRevealMask(
    float2 uv,
    float shape,
    float aspect,
    float feather)
{
    float insideDistance = HazardInsideDistance(uv, shape, aspect);
    return smoothstep(0.0, max(feather, 0.001), insideDistance);
}

#endif
