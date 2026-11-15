// Triplanar sampling of photoscanned PBR maps (base colour, OpenGL normal, AO), for
// surfaces with no UVs: the dug ground and the clods that come out of it.
// Normals are blended with Ben Golus' "whiteout" method, signs corrected per axis.
#ifndef DIG_TRIPLANAR_INCLUDED
#define DIG_TRIPLANAR_INCLUDED

struct TriCoords
{
    float3 weights;
    float2 uvX, uvY, uvZ;
    half3 axisSign;
    half3 normal;           // the geometric normal the projections are blended around
};

TriCoords MakeTriCoords(float3 position, half3 normal, float tilesPerMetre, float sharpness)
{
    TriCoords t;
    float3 w = pow(abs(normal), sharpness);
    t.weights = w / max(w.x + w.y + w.z, 1e-4);
    t.axisSign = half3(normal.x < 0 ? -1 : 1, normal.y < 0 ? -1 : 1, normal.z < 0 ? -1 : 1);
    float3 p = position * tilesPerMetre;
    t.uvX = p.zy; t.uvY = p.xz; t.uvZ = p.xy;
    t.uvX.x *= t.axisSign.x;
    t.uvY.x *= t.axisSign.y;
    t.uvZ.x *= -t.axisSign.z;
    t.normal = normal;
    return t;
}

// Same coordinates at another scale (a second sample breaks up visible tiling).
TriCoords ScaleTri(TriCoords t, float factor, float2 offset)
{
    t.uvX = t.uvX * factor + offset;
    t.uvY = t.uvY * factor + offset;
    t.uvZ = t.uvZ * factor + offset;
    return t;
}

half3 WhiteoutNormal(half3 tnX, half3 tnY, half3 tnZ, TriCoords t)
{
    half3 n = t.normal;
    tnX.x *= t.axisSign.x;
    tnY.x *= t.axisSign.y;
    tnZ.x *= -t.axisSign.z;
    tnX = half3(tnX.xy + n.zy, abs(tnX.z) * n.x);
    tnY = half3(tnY.xy + n.xz, abs(tnY.z) * n.y);
    tnZ = half3(tnZ.xy + n.xy, abs(tnZ.z) * n.z);
    return normalize(tnX.zyx * t.weights.x + tnY.xzy * t.weights.y + tnZ.xyz * t.weights.z);
}

struct TriSample
{
    half3 albedo;
    half3 normal;
    half ao;
};

// One material on all three axes. The Y axis may use a different material (loose earth
// on floors, compact earth on walls): pass the same textures twice when it should not.
TriSample SampleTri(TEXTURE2D_PARAM(sideDiff, samp), TEXTURE2D_PARAM(sideNrm, samp2), TEXTURE2D_PARAM(sideAO, samp3),
                    TEXTURE2D_PARAM(topDiff, samp4), TEXTURE2D_PARAM(topNrm, samp5), TEXTURE2D_PARAM(topAO, samp6),
                    TriCoords t, half normalStrength)
{
    TriSample s;
    half3 cX = SAMPLE_TEXTURE2D(sideDiff, samp, t.uvX).rgb;
    half3 cY = SAMPLE_TEXTURE2D(topDiff, samp4, t.uvY).rgb;
    half3 cZ = SAMPLE_TEXTURE2D(sideDiff, samp, t.uvZ).rgb;
    s.albedo = cX * t.weights.x + cY * t.weights.y + cZ * t.weights.z;

    half aX = SAMPLE_TEXTURE2D(sideAO, samp3, t.uvX).r;
    half aY = SAMPLE_TEXTURE2D(topAO, samp6, t.uvY).r;
    half aZ = SAMPLE_TEXTURE2D(sideAO, samp3, t.uvZ).r;
    s.ao = aX * t.weights.x + aY * t.weights.y + aZ * t.weights.z;

    half3 nX = UnpackNormalScale(SAMPLE_TEXTURE2D(sideNrm, samp2, t.uvX), normalStrength);
    half3 nY = UnpackNormalScale(SAMPLE_TEXTURE2D(topNrm, samp5, t.uvY), normalStrength);
    half3 nZ = UnpackNormalScale(SAMPLE_TEXTURE2D(sideNrm, samp2, t.uvZ), normalStrength);
    s.normal = WhiteoutNormal(nX, nY, nZ, t);
    return s;
}

TriSample LerpSample(TriSample a, TriSample b, half k)
{
    TriSample s;
    s.albedo = lerp(a.albedo, b.albedo, k);
    s.normal = normalize(lerp(a.normal, b.normal, k));
    s.ao = lerp(a.ao, b.ao, k);
    return s;
}

// ---- Small value noise, for large-scale variation that hides tiling ----------------
float DigHash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float DigNoise3(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(DigHash3(i + float3(0, 0, 0)), DigHash3(i + float3(1, 0, 0)), f.x),
                     lerp(DigHash3(i + float3(0, 1, 0)), DigHash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(DigHash3(i + float3(0, 0, 1)), DigHash3(i + float3(1, 0, 1)), f.x),
                     lerp(DigHash3(i + float3(0, 1, 1)), DigHash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float DigFbm(float3 p)
{
    return DigNoise3(p) * 0.5 + DigNoise3(p * 2.3 + 7.1) * 0.3 + DigNoise3(p * 5.9 + 3.3) * 0.2;
}

#endif
