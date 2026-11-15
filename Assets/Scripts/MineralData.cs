using UnityEngine;

// One kind of mineral: what it is, the line it whispers, what the cat pays for it, and
// how common it is in each stratum. Assets live in Assets/Data/Minerals.
[CreateAssetMenu(menuName = "DigIn/Mineral", fileName = "Mineral")]
public class MineralData : ScriptableObject
{
    public string title;
    [TextArea(2, 3)] public string line;
    [Tooltip("Coins the shop pays for one.")]
    public int value = 5;
    [Tooltip("How common it is in Childhood, School Days and Growing Up (0 = never).")]
    public Vector3 weightByStratum = new Vector3(1f, 0.5f, 0.2f);
    [Tooltip("The model (Meshy).")]
    public GameObject model;
    [Tooltip("Longest side of the specimen, metres.")]
    public float size = 0.14f;
    public Color glint = new Color(1f, 0.95f, 0.85f);
    [Tooltip("The shop's picture of it, rendered from the model (Assets/Textures/Shop).")]
    public Sprite icon;
}
