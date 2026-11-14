using UnityEngine;

// The shelf at the surface. Nine slots, one per memory, in the same order as
// DigGame.memories (row 1 = Childhood, row 2 = School Days, row 3 = Growing Up).
// Each slot has an "empty" marker and a "filled" object (the card) that is hidden
// until the memory is found.
public class MemoryShelf : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public GameObject empty;
        public GameObject filled;
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

    public int FilledCount()
    {
        int n = 0;
        foreach (var s in slots)
            if (s != null && s.filled != null && s.filled.activeSelf) n++;
        return n;
    }

    void Show(int index, bool filled)
    {
        if (index < 0 || index >= slots.Length || slots[index] == null) return;
        if (slots[index].empty != null) slots[index].empty.SetActive(!filled);
        if (slots[index].filled != null) slots[index].filled.SetActive(filled);
    }
}
