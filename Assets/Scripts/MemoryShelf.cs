using TMPro;
using UnityEngine;

// The shelf by the back door. Nine slots, one per memory, in the same order as
// DigGame.memories (top row = Childhood, middle = School Days, bottom = Growing Up).
// Each slot holds a small framed card that is hidden until the memory is found.
public class MemoryShelf : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        [Tooltip("Optional marker shown while the slot is empty.")]
        public GameObject empty;
        [Tooltip("The framed card, hidden until the memory is found.")]
        public GameObject filled;
        public TMP_Text title;
        public Renderer illustration;
    }

    public Slot[] slots = new Slot[9];

    [Tooltip("Illustration colour per stratum while a memory has no card art.")]
    public Color[] stratumTints =
    {
        new Color(0.93f, 0.80f, 0.62f),
        new Color(0.88f, 0.72f, 0.62f),
        new Color(0.78f, 0.78f, 0.80f),
    };

    MaterialPropertyBlock block;

    void Awake()
    {
        for (int i = 0; i < slots.Length; i++) Show(i, false);
    }

    // Puts the framed card for this memory on its slot.
    public void Fill(int index, MemoryData data)
    {
        if (index < 0 || index >= slots.Length || slots[index] == null) return;
        var slot = slots[index];
        if (slot.title != null) slot.title.text = data.title;

        if (slot.illustration != null)
        {
            if (block == null) block = new MaterialPropertyBlock();
            slot.illustration.GetPropertyBlock(block);
            if (data.card != null)
            {
                block.SetTexture("_BaseMap", data.card.texture);
                block.SetColor("_BaseColor", Color.white);
            }
            else
            {
                block.SetColor("_BaseColor", stratumTints[Mathf.Clamp(data.stratum, 0, stratumTints.Length - 1)]);
            }
            slot.illustration.SetPropertyBlock(block);
        }
        Show(index, true);
    }

    public bool IsFilled(int index)
    {
        return index >= 0 && index < slots.Length && slots[index] != null &&
               slots[index].filled != null && slots[index].filled.activeSelf;
    }

    void Show(int index, bool filled)
    {
        if (index < 0 || index >= slots.Length || slots[index] == null) return;
        if (slots[index].empty != null) slots[index].empty.SetActive(!filled);
        if (slots[index].filled != null) slots[index].filled.SetActive(filled);
    }
}
