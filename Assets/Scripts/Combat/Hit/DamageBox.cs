using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Test/utility damage zone. Any IDamageReceiver (Character) staying inside the
    /// trigger box takes damage every tick interval, through the shared hit pipeline
    /// (IDamageReceiver.ReceiveDamage -> CharacterCombat -> RuntimeStats).
    /// Player is identified via PlayerController.ControlledCharacterComponent
    /// (controller is a separate object, not a parent of the character).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class DamageBox : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField] private float damage = 10f;
        [SerializeField] private float tickInterval = 1f;
        [SerializeField] private bool damagePlayerOnly = true;

        [Header("Debug")]
        [SerializeField] private bool logFiltered = true;

        private readonly Dictionary<IDamageReceiver, float> _nextTickTime = new Dictionary<IDamageReceiver, float>();
        private Character _playerCharacter;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;

            // Trigger callbacks require a Rigidbody on at least one side.
            if (GetComponent<Rigidbody>() == null)
            {
                var rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        private Character FindPlayerCharacter()
        {
            if (_playerCharacter != null) return _playerCharacter;
            var pc = FindFirstObjectByType<PlayerController>();
            _playerCharacter = pc != null ? pc.ControlledCharacterComponent : null;
            return _playerCharacter;
        }

        private void OnTriggerStay(Collider other)
        {
            var receiver = other.GetComponentInParent<IDamageReceiver>();
            if (receiver == null)
            {
                if (logFiltered) Debug.Log("[DamageBox] " + other.name + " — IDamageReceiver 없음(무시)");
                return;
            }

            if (damagePlayerOnly)
            {
                var player = FindPlayerCharacter();
                if (player == null || !ReferenceEquals(receiver, player))
                {
                    if (logFiltered) Debug.Log("[DamageBox] " + other.transform.root.name + " — 플레이어 아님(무시)");
                    return;
                }
            }

            if (_nextTickTime.TryGetValue(receiver, out float next) && Time.time < next) return;
            _nextTickTime[receiver] = Time.time + tickInterval;

            var dir = other.transform.position - transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;

            receiver.ReceiveDamage(new DamageInfo(gameObject, damage, other.ClosestPoint(transform.position), dir));
            Debug.Log("[DamageBox] " + other.transform.root.name + " took " + damage + " damage");
        }

        private void OnTriggerExit(Collider other)
        {
            var receiver = other.GetComponentInParent<IDamageReceiver>();
            if (receiver != null) _nextTickTime.Remove(receiver);
        }
    }
}
