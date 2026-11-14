using UnityEngine;

// A letter, shown line by line on a full-screen paper overlay. One per stratum, plus the
// ending. Assets live in Assets/Data/Letters (created by DigIn/Build Scene, editable after).
[CreateAssetMenu(menuName = "DigIn/Letter", fileName = "Letter")]
public class LetterData : ScriptableObject
{
    [Tooltip("Small heading above the letter. Leave empty for none.")]
    public string title;
    [Tooltip("Each line fades in on its own.")]
    [TextArea(1, 3)] public string[] lines;
}
