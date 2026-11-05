using UnityEngine;

namespace GearUI
{
    /// <summary>Rotates a UI element continuously (gear rotor / boat wheel).</summary>
    public class Spinner : MonoBehaviour
    {
        public float degreesPerSecond = -120f;

        void Update() => transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
    }
}
