using UnityEngine;

namespace GearUI
{
    /// <summary>Fits this RectTransform to Screen.safeArea (notches, home indicator).</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        Rect lastSafeArea;
        Vector2Int lastScreen;

        void OnEnable() => Apply(true);
        void Update() => Apply(false);

        void Apply(bool force)
        {
            Rect safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == lastSafeArea && screen == lastScreen) return;
            if (screen.x <= 0 || screen.y <= 0) return;

            lastSafeArea = safe;
            lastScreen = screen;

            var rt = (RectTransform)transform;
            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= screen.x; min.y /= screen.y;
            max.x /= screen.x; max.y /= screen.y;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
