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

    void Awake()
    {
        for (int i = 0; i < slots.Length; i++) Show(i, false);
    }

    public void Fill(int index)
    {
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
