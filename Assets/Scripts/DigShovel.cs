using System;
using UnityEngine;

// The first-person shovel. Lives in the Player prefab under the camera:
//   Shovel (this, Animator)  - sways with walking and looking
//     Pivot                  - moved by the swing clips (Assets/Animations)
//       one model per shovel level, origin = grip, handle along +Z
// The swing itself is animation: the clips call OnImpact, OnToss, OnReady and OnDone
// here through animation events, and DigPlayer listens.
[RequireComponent(typeof(Animator))]
public class DigShovel : MonoBehaviour
{
    [Tooltip("One model per shovel level: Toy, Garden, Steel.")]
    public GameObject[] models;
    [Tooltip("Tip of the blade: where tossed earth leaves the shovel.")]
    public Transform blade;
    [Tooltip("The clump of earth on the blade between the bite and the toss.")]
    public MeshRenderer load;

    [Header("Impact")]
    [Tooltip("Seconds the swing freezes when the blade bites.")]
    public float hitStop = 0.04f;

    [Header("Sway")]
    public float bobAmount = 0.014f;
    [Tooltip("How far the shovel lags behind when you turn (degrees per degree/second).")]
    public float lookLag = 0.012f;
    public float maxLag = 4f;
    public float idleSway = 0.004f;

    public event Action Impact;
    public event Action Toss;

    // False from the start of a swing until the clip says the next one may begin.
    public bool Ready { get; private set; } = true;

    // Two states with the same clip: a held button cross-fades from one swing into the next.
    static readonly int SwingState = Animator.StringToHash("Swing");
    static readonly int SwingState2 = Animator.StringToHash("Swing 2");
    static readonly int BounceState = Animator.StringToHash("Bounce");

    Animator animator;
    Vector3 restPosition;
    Quaternion restRotation;
    Vector2 lag;
    float stopTimer;
    float busyTimer;
    bool second;
    Vector3 loadScale;
    float loadGrow;

    void Awake()
    {
        animator = GetComponent<Animator>();
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        if (load != null) loadScale = load.transform.localScale;
    }

    public void SetLevel(int level)
    {
        for (int i = 0; i < models.Length; i++)
            if (models[i] != null) models[i].SetActive(i == level);
    }

    public void Swing()
    {
        Ready = false;
        busyTimer = 1.5f;
        animator.speed = 1f;
        stopTimer = 0f;
        HideLoad();
        animator.CrossFadeInFixedTime(second ? SwingState2 : SwingState, 0.07f, 0, 0f);
        second = !second;
    }

    // The blade came out of the ground with earth on it.
    public void ShowLoad(Material material, float amount)
    {
        if (load == null) return;
        load.sharedMaterial = material;
        load.transform.localRotation = Quaternion.Euler(UnityEngine.Random.Range(-8f, 8f), UnityEngine.Random.Range(0f, 360f), 0f);
        load.transform.localScale = loadScale * 0.2f;
        loadGrow = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(amount)) * UnityEngine.Random.Range(0.9f, 1.1f);
        load.gameObject.SetActive(true);
    }

    public void HideLoad()
    {
        if (load != null) load.gameObject.SetActive(false);
    }

    // The blade bit into the ground: hold the pose for a moment.
    public void HitStop()
    {
        stopTimer = hitStop;
        animator.speed = 0f;
    }

    // The blade hit something it cannot dig: recoil.
    public void Bounce()
    {
        animator.speed = 1f;
        stopTimer = 0f;
        HideLoad();
        animator.CrossFadeInFixedTime(BounceState, 0.03f, 0, 0f);
    }

    // ---- Animation events (see the Swing and Bounce clips) -----------------------------
    void OnImpact() { Impact?.Invoke(); }
    void OnToss() { Toss?.Invoke(); }
    void OnReady() { Ready = true; }
    void OnDone() { Ready = true; }

    void Update()
    {
        if (stopTimer > 0f)
        {
            stopTimer -= Time.unscaledDeltaTime;
            if (stopTimer <= 0f) animator.speed = 1f;
        }

        // The load swells onto the blade as it is levered out of the ground.
        if (load != null && load.gameObject.activeSelf && stopTimer <= 0f)
            load.transform.localScale = Vector3.MoveTowards(load.transform.localScale, loadScale * loadGrow, loadScale.z * 9f * Time.deltaTime);

        // Never get stuck waiting for an event that did not come.
        if (!Ready)
        {
            busyTimer -= Time.deltaTime;
            if (busyTimer <= 0f) Ready = true;
        }
    }

    // Walking bob, a slow idle drift, and a little lag behind the camera when turning.
    public void Sway(float bobPhase, float walk, Vector2 lookDegreesPerSecond)
    {
        float dt = Time.deltaTime;
        Vector2 target = Vector2.ClampMagnitude(-lookDegreesPerSecond * lookLag, maxLag);
        lag = Vector2.Lerp(lag, target, 1f - Mathf.Exp(-10f * dt));

        float t = Time.time;
        Vector3 offset = new Vector3(Mathf.Cos(bobPhase) * bobAmount, -Mathf.Abs(Mathf.Sin(bobPhase)) * bobAmount * 1.2f, 0f) * walk;
        offset += new Vector3(Mathf.Sin(t * 0.9f), Mathf.Sin(t * 1.3f + 1f), 0f) * idleSway;
        offset += new Vector3(lag.x, -lag.y, 0f) * 0.004f;

        transform.localPosition = restPosition + offset;
        transform.localRotation = restRotation * Quaternion.Euler(lag.y, lag.x, lag.x * 0.5f);
    }
}
