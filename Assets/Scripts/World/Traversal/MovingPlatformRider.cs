using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Thin trigger volume sitting just above a MovingPlatform's top surface. Characters inside
    /// it are registered as riders; the platform then carries only the grounded ones by delta.
    /// Pure relay — it never touches CharacterState.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Yeolha/Traversal/Moving Platform Rider Zone")]
    public class MovingPlatformRider : MonoBehaviour
    {
        [SerializeField] private MovingPlatform platform;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            if (platform == null) platform = GetComponentInParent<MovingPlatform>();
        }

        private void Awake()
        {
            if (platform == null) platform = GetComponentInParent<MovingPlatform>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (platform == null) return;
            var character = other.GetComponentInParent<Character>();
            if (character != null) platform.AddRider(character);
        }

        private void OnTriggerExit(Collider other)
        {
            if (platform == null) return;
            var character = other.GetComponentInParent<Character>();
            if (character != null) platform.RemoveRider(character);
        }
    }
}
