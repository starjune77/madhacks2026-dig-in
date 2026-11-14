using UnityEngine;

// One memory object: what it is, the line on its card, and the depth it turns up at.
// Assets live in Assets/Data/Memories (created by DigIn/Build Scene, editable after).
[CreateAssetMenu(menuName = "DigIn/Memory", fileName = "Memory")]
public class MemoryData : ScriptableObject
{
    public string title;
    [TextArea(2, 4)] public string line;
    [Tooltip("Metres below the surface. Fixed so the demo is reproducible.")]
    public float depth;
    [Tooltip("0 = Childhood, 1 = School Days, 2 = Growing Up")]
    public int stratum;
    [Tooltip("Illustrated card. Empty until the card art exists.")]
    public Sprite card;
    [Tooltip("This memory's own object in the dirt. Empty = the stratum's stand-in.")]
    public MemoryFind findPrefab;
}
