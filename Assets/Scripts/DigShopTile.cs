using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// An item tile on the cat's price board. Under the mouse it lifts a little (its shadow
// grows) and, if it has one, shows its description. Runs on unscaled time.
public class DigShopTile : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public RectTransform lift;
    public Shadow shadow;
    [Tooltip("Shown only under the mouse (optional).")]
    public CanvasGroup reveal;
    [Tooltip("The whole board: while it is hidden, nothing stays raised.")]
    public CanvasGroup board;
    public float height = 7f;
    public float speed = 8f;

    bool over;
    float t;
    Vector2 rest;
    Vector2 restShadow;
    bool ready;

    void Init()
    {
        if (ready) return;
        ready = true;
        if (lift != null) rest = lift.anchoredPosition;
        if (shadow != null) restShadow = shadow.effectDistance;
    }

    public void OnPointerEnter(PointerEventData e) => over = true;
    public void OnPointerExit(PointerEventData e) => over = false;

    void OnEnable() { Init(); Apply(); }

    void OnDisable()
    {
        over = false;
        t = 0f;
        Apply();
    }

    void Update()
    {
        // A hidden board (alpha 0, not clickable) never keeps a tile raised.
        if (board != null && !board.blocksRaycasts) over = false;
        float target = over ? 1f : 0f;
        if (Mathf.Approximately(t, target)) return;
        t = Mathf.MoveTowards(t, target, Time.unscaledDeltaTime * speed);
        Apply();
    }

    void Apply()
    {
        Init();
        float e = Mathf.SmoothStep(0f, 1f, t);
        if (lift != null) lift.anchoredPosition = rest + Vector2.up * height * e;
        if (shadow != null) shadow.effectDistance = restShadow * (1f + e);
        if (reveal != null) reveal.alpha = e;
    }
}
