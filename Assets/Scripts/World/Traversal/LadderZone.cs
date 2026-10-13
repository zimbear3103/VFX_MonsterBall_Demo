using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// A ladder's bottom or top detection zone. Pure trigger relay: when a Character enters,
    /// it registers the parent ladder as that character's available ladder (with the atTop
    /// flag), and clears it on exit. It never changes CharacterState itself — the grounded/air
    /// states decide via CharacterState.CheckLadder.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Yeolha/Traversal/Ladder Zone")]
    public class LadderZone : MonoBehaviour
    {
        public enum Kind { Bottom, Top }

        [SerializeField] private Kind kind = Kind.Bottom;
        [SerializeField] private LadderTraversable ladder;

        public Kind ZoneKind => kind;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            if (ladder == null) ladder = GetComponentInParent<LadderTraversable>();
        }

        private void Awake()
        {
            if (ladder == null) ladder = GetComponentInParent<LadderTraversable>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (ladder == null) ladder = GetComponentInParent<LadderTraversable>();
            if (ladder == null) return;
            var character = other.GetComponentInParent<Character>();
            if (character == null) return;
            ladder.NotifyZoneEnter(kind == Kind.Top, character);
        }

        private void OnTriggerStay(Collider other)
        {
            if (ladder == null) return;
            var character = other.GetComponentInParent<Character>();
            if (character == null) return;
            // 진입 누락/배치 상태 진입을 대비해 매 물리 프레임 가용성 재확인.
            ladder.NotifyZoneEnter(kind == Kind.Top, character);
        }

        private void OnTriggerExit(Collider other)
        {
            if (ladder == null) return;
            var character = other.GetComponentInParent<Character>();
            if (character == null) return;
            ladder.NotifyZoneExit(character);
        }
    }
}
