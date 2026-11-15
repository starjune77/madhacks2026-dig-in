using UnityEngine;

// On every pooled clod (added by DigDebris at start). A clod that lands hard enough
// breaks into smaller pieces; the debris pool does the breaking.
public class DigClodBody : MonoBehaviour
{
    [System.NonSerialized] public DigDebris owner;
    [System.NonSerialized] public int index;

    void OnCollisionEnter(Collision collision)
    {
        if (owner != null) owner.Landed(index, collision.relativeVelocity.magnitude);
    }
}
