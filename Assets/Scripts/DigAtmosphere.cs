using UnityEngine;

// Lighting by depth: the deeper you go, the dimmer and warmer it gets, and the
// lantern near the player takes over. Surface values are read from the scene's
// lighting settings at start; deep values are set here in the inspector.
public class DigAtmosphere : MonoBehaviour
{
    public Transform viewer;     // the player camera
    public Light sun;
    public Light lantern;

    [Header("Depth curve")]
    [Tooltip("Depth (m) at which the deep look is fully reached.")]
    public float fullDepth = 6f;
    [Tooltip("Shape of the transition from surface (0) to deep (1) over depth 0..fullDepth.")]
    public AnimationCurve blend = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Deep look")]
    public Color deepSky = new Color(0.20f, 0.13f, 0.08f);
    public Color deepEquator = new Color(0.16f, 0.10f, 0.06f);
    public Color deepGround = new Color(0.08f, 0.05f, 0.03f);
    public Color deepFog = new Color(0.16f, 0.10f, 0.06f);
    public float deepFogDensity = 0.06f;
    [Range(0f, 1f)] public float deepSunScale = 0.25f;
    [Tooltip("Ambient and reflection intensity deep down (the sky lights the surface).")]
    [Range(0f, 1f)] public float deepAmbient = 0.12f;

    [Header("Lantern")]
    public float lanternSurfaceIntensity = 0f;
    public float lanternDeepIntensity = 2.2f;
    public float flicker = 0.08f;
    [Tooltip("Set by the game from the shop's lantern upgrades: multiplies brightness and reach.")]
    public float lanternPower = 1f;
    float baseRange;

    Color surfaceSky, surfaceEquator, surfaceGround, surfaceFog;
    float surfaceFogDensity, surfaceSunIntensity, surfaceAmbient, surfaceReflection;

    void Start()
    {
        surfaceSky = RenderSettings.ambientSkyColor;
        surfaceEquator = RenderSettings.ambientEquatorColor;
        surfaceGround = RenderSettings.ambientGroundColor;
        surfaceFog = RenderSettings.fogColor;
        surfaceFogDensity = RenderSettings.fogDensity;
        if (sun != null) surfaceSunIntensity = sun.intensity;
        surfaceAmbient = RenderSettings.ambientIntensity;
        surfaceReflection = RenderSettings.reflectionIntensity;
    }

    void Update()
    {
        if (viewer == null) return;

        float depth = Mathf.Max(0f, -viewer.position.y);
        float t = Mathf.Clamp01(blend.Evaluate(Mathf.Clamp01(depth / fullDepth)));

        RenderSettings.ambientSkyColor = Color.Lerp(surfaceSky, deepSky, t);
        RenderSettings.ambientEquatorColor = Color.Lerp(surfaceEquator, deepEquator, t);
        RenderSettings.ambientGroundColor = Color.Lerp(surfaceGround, deepGround, t);
        RenderSettings.fogColor = Color.Lerp(surfaceFog, deepFog, t);
        RenderSettings.fogDensity = Mathf.Lerp(surfaceFogDensity, deepFogDensity, t);
        if (sun != null) sun.intensity = surfaceSunIntensity * Mathf.Lerp(1f, deepSunScale, t);
        RenderSettings.ambientIntensity = surfaceAmbient * Mathf.Lerp(1f, deepAmbient, t);
        RenderSettings.reflectionIntensity = surfaceReflection * Mathf.Lerp(1f, deepAmbient, t);

        if (lantern != null)
        {
            float n = Mathf.PerlinNoise(Time.time * 3f, 0.37f) - 0.5f;
            float lanternT = Mathf.Clamp01(depth / 2.5f);   // lantern comes up quickly once in the hole
            if (baseRange <= 0f) baseRange = lantern.range;
            lantern.range = baseRange * Mathf.Sqrt(lanternPower);
            lantern.intensity = Mathf.Lerp(lanternSurfaceIntensity, lanternDeepIntensity * lanternPower, lanternT) * (1f + n * flicker);
            lantern.enabled = lantern.intensity > 0.01f;
        }
    }

    void OnDisable()
    {
        // Leave the scene's lighting as it was when play mode stops.
        if (!Application.isPlaying || surfaceFogDensity <= 0f) return;
        RenderSettings.ambientSkyColor = surfaceSky;
        RenderSettings.ambientEquatorColor = surfaceEquator;
        RenderSettings.ambientGroundColor = surfaceGround;
        RenderSettings.fogColor = surfaceFog;
        RenderSettings.fogDensity = surfaceFogDensity;
        RenderSettings.ambientIntensity = surfaceAmbient;
        RenderSettings.reflectionIntensity = surfaceReflection;
    }
}
