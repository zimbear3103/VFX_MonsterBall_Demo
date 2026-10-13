using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 피격 판정 영역. (Hitbox = 공격하는 영역, Hurtbox = 공격을 받는 영역)
    /// Trigger Collider와 함께 두고, 데미지는 부모의 IDamageReceiver(EnemyHealth 등)로 전달된다.
    /// 이번 단계에서는 몬스터 몸통 하나면 충분하다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hurtbox : MonoBehaviour
    {
        [Tooltip("명시적 리시버 (비우면 부모에서 IDamageReceiver 자동 검색)")]
        [SerializeField] private MonoBehaviour receiverBehaviour;

        private IDamageReceiver _receiver;
        private bool _resolved;

        /// <summary>이 Hurtbox가 데미지를 전달할 대상.</summary>
        public IDamageReceiver Receiver
        {
            get
            {
                if (!_resolved)
                {
                    _receiver = receiverBehaviour as IDamageReceiver;
                    if (_receiver == null)
                        _receiver = GetComponentInParent<IDamageReceiver>();
                    _resolved = true;
                }
                return _receiver;
            }
        }

        /// <summary>소유자 루트 (자기 자신 타격 방지 판정용).</summary>
        public Transform OwnerRoot => (_receiver as Component) != null ? ((Component)_receiver).transform : transform;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }
    }
}
