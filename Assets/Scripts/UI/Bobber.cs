using UnityEngine;

namespace GearUI
{
    /// <summary>Small idle bob for enemies / boat on the water.</summary>
    public class Bobber : MonoBehaviour
    {
        public float amplitude = 2f;
        public float frequency = 1.2f;
        public float phase;

        RectTransform rt;
        Vector2 basePos;

        void Awake()
        {
            rt = (RectTransform)transform;
            basePos = rt.anchoredPosition;
            if (phase == 0f) phase = Random.value * Mathf.PI * 2f;
        }

        void OnDisable()
        {
            if (rt != null) rt.anchoredPosition = basePos;
        }

        void Update()
        {
            rt.anchoredPosition = basePos + new Vector2(0f, Mathf.Sin(Time.time * frequency * Mathf.PI * 2f + phase) * amplitude);
        }
    }
}
