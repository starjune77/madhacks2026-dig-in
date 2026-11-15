using UnityEngine;

// A small thing resting on diggable ground (the stones around the dig spot, the trowel).
// When the ground near it is dug or covered, it comes loose and falls like a clod, so
// nothing ever hangs in the air.
public class DigLoosen : MonoBehaviour
{
    public DigVolume volume;
    [Tooltip("Ground disturbed within this distance (m, beyond the dig itself) loosens it.")]
    public float reach = 0.35f;

    bool loose;

    void OnEnable() { if (volume != null) volume.Disturbed += OnDisturbed; }
    void OnDisable() { if (volume != null) volume.Disturbed -= OnDisturbed; }

    void OnDisturbed(Vector3 point, float radius)
    {
        if (loose) return;
        Vector3 d = transform.position - point;
        d.y = 0f;
        if (d.magnitude > radius + reach) return;
        loose = true;
        var body = gameObject.AddComponent<Rigidbody>();
        body.mass = 2f;
        body.angularDamping = 1.5f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.WakeUp();
    }

    void Update()
    {
        if (loose && transform.position.y < DigVolume.Min.y - 2f) gameObject.SetActive(false);
    }
}
