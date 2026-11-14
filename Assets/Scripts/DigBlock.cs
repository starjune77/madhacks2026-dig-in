using UnityEngine;

// One diggable cube in the shaft.
public class DigBlock : MonoBehaviour
{
    public int layer;      // 0 = top layer
    public int stratum;    // 0 = childhood (dirt), 1 = school (clay), 2 = grown up (stone)
    public float maxHp;
    public float hp;

    Vector3 baseScale;

    public void Init(int layer, int stratum, float hp)
    {
        this.layer = layer;
        this.stratum = stratum;
        this.maxHp = hp;
        this.hp = hp;
        baseScale = transform.localScale;
    }

    // Returns true when the block is destroyed.
    public bool Hit(float damage)
    {
        hp -= damage;
        if (hp <= 0f) return true;

        // Cheap damage feedback: the block shrinks a little as it cracks.
        float t = hp / maxHp;
        transform.localScale = baseScale * (0.85f + 0.15f * t);
        return false;
    }
}
