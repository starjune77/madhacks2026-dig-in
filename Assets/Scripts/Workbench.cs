using UnityEngine;

// The workbench at the surface where the shovel is upgraded. Walk up and press E.
public class Workbench : MonoBehaviour
{
    [Tooltip("Where the player counts as 'at the bench'. Usually just in front of it.")]
    public Transform interactPoint;
    public float interactRadius = 1.6f;

    public bool InRange(Vector3 playerPosition)
    {
        Vector3 p = interactPoint != null ? interactPoint.position : transform.position;
        Vector3 d = playerPosition - p;
        d.y = 0f;
        return d.sqrMagnitude <= interactRadius * interactRadius && Mathf.Abs(playerPosition.y - p.y) < 2f;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(interactPoint != null ? interactPoint.position : transform.position, interactRadius);
    }
}
