using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GearUI
{
    public enum GameplayState
    {
        Build,  // arranging gears: big board + shop bar
        Combat  // board shrinks to the bottom-left, battle runs above it
    }

    /// <summary>
    /// Switches the gameplay screen between the gear-arranging state and the combat state.
    /// The same Board is reused: in Build it is scaled up (x1.209, matching the Figma frame),
    /// in Combat it shrinks back to its native size next to the booster column.
    /// </summary>
    public class GameplayUIController : MonoBehaviour
    {
        [Header("Board (pivot top-left, inside BottomArea)")]
        public RectTransform board;
        public Vector2 buildBoardPosition = new Vector2(13f, -199f);
        public float buildBoardScale = 376.075439f / 311f;
        public Vector2 combatBoardPosition = new Vector2(12f, -463f);
        public float combatBoardScale = 1f;

        [Header("State groups")]
        public CanvasGroup[] buildOnly;
        public CanvasGroup[] combatOnly;

        [Tooltip("Shop bar that slides down out of view when combat starts.")]
        public RectTransform[] buildSlideDown;
        [Tooltip("Anchored positions of buildSlideDown in the Build state (filled by the scene builder).")]
        public Vector2[] slideBase;
        public float slideDistance = 260f;

        [Header("Combat motion")]
        public Behaviour[] combatAnimators;

        [Header("Buttons")]
        public Button startButton;
        public Button exitCombatButton;

        [Header("Timing")]
        public float duration = 0.45f;
        public GameplayState startState = GameplayState.Build;

        public GameplayState State { get; private set; }

        float combatT;           // 0 = build, 1 = combat
        Coroutine running;

        void Awake()
        {
            if (slideBase == null || buildSlideDown == null || slideBase.Length != buildSlideDown.Length) CacheSlideBase();
            State = startState;
            ApplyBlend(State == GameplayState.Combat ? 1f : 0f);

            if (startButton != null) startButton.onClick.AddListener(EnterCombat);
            if (exitCombatButton != null) exitCombatButton.onClick.AddListener(EnterBuild);
        }

        void CacheSlideBase()
        {
            if (buildSlideDown == null) { slideBase = new Vector2[0]; return; }
            slideBase = new Vector2[buildSlideDown.Length];
            for (int i = 0; i < buildSlideDown.Length; i++)
                slideBase[i] = buildSlideDown[i] != null ? buildSlideDown[i].anchoredPosition : Vector2.zero;
        }

        public void EnterCombat() => SetState(GameplayState.Combat);
        public void EnterBuild() => SetState(GameplayState.Build);
        public void Toggle() => SetState(State == GameplayState.Build ? GameplayState.Combat : GameplayState.Build);

        public void SetState(GameplayState target, bool instant = false)
        {
            State = target;
            float to = target == GameplayState.Combat ? 1f : 0f;
            if (running != null) StopCoroutine(running);
            running = null;

            if (instant || !Application.isPlaying || !isActiveAndEnabled)
            {
                ApplyBlend(to);
                return;
            }
            running = StartCoroutine(Animate(to));
        }

        IEnumerator Animate(float to)
        {
            float from = combatT;
            float time = Mathf.Abs(to - from) * duration;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                ApplyBlend(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / time)));
                yield return null;
            }
            ApplyBlend(to);
            running = null;
        }

        /// <summary>Applies an interpolated layout between Build (0) and Combat (1).</summary>
        public void ApplyBlend(float t)
        {
            combatT = Mathf.Clamp01(t);

            if (board != null)
            {
                board.anchoredPosition = Vector2.LerpUnclamped(buildBoardPosition, combatBoardPosition, combatT);
                float s = Mathf.LerpUnclamped(buildBoardScale, combatBoardScale, combatT);
                board.localScale = new Vector3(s, s, 1f);
            }

            // Build-only content fades out in the first 60% of the move, combat content fades in over the last 60%.
            SetGroups(buildOnly, 1f - Mathf.Clamp01(combatT / 0.6f));
            SetGroups(combatOnly, Mathf.Clamp01((combatT - 0.4f) / 0.6f));

            if (slideBase == null || buildSlideDown == null || slideBase.Length != buildSlideDown.Length) CacheSlideBase();
            for (int i = 0; i < buildSlideDown.Length; i++)
                if (buildSlideDown[i] != null)
                    buildSlideDown[i].anchoredPosition = slideBase[i] + new Vector2(0f, -slideDistance * combatT);

            if (combatAnimators != null)
                foreach (var b in combatAnimators)
                    if (b != null) b.enabled = Application.isPlaying && combatT >= 1f;
        }

        static void SetGroups(CanvasGroup[] groups, float alpha)
        {
            if (groups == null) return;
            foreach (var g in groups)
            {
                if (g == null) continue;
                g.alpha = alpha;
                bool on = alpha > 0.5f;
                g.interactable = on;
                g.blocksRaycasts = on;
                bool visible = alpha > 0.001f;
                if (g.gameObject.activeSelf != visible) g.gameObject.SetActive(visible);
            }
        }

#if ENABLE_INPUT_SYSTEM
        void Update()
        {
            // Debug shortcut: Space toggles between the two states.
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) Toggle();
        }
#endif

        [ContextMenu("Preview/Build state")]
        public void PreviewBuild() { State = GameplayState.Build; ApplyBlend(0f); }

        [ContextMenu("Preview/Combat state")]
        public void PreviewCombat() { State = GameplayState.Combat; ApplyBlend(1f); }
    }
}
