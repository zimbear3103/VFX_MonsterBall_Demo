using UnityEngine;
using UnityEngine.Events;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 프레임워크 정합 파괴 가능 프롭 (구 Aiara.BreakableProp의 ver2 포팅).
    /// 구버전은 TDE Health.OnDeath에 의존했지만, ver2에서는 IDamageReceiver로 피격을 직접 받아
    /// 누적 피해가 maxHealth 이상이면 파괴한다. 파괴 시 debris 프리팹 스폰 + 폭발력 + 위에 얹힌 프롭 연쇄 파괴.
    ///
    /// 피격 경로: 플레이어 공격(AttackHitbox) → 상자의 자식 Hurtbox(트리거) → GetComponentInParent로 이 컴포넌트.
    /// AttackHitbox의 진영 필터는 receiver가 Character일 때만 작동하므로, 이 프롭은 진영과 무관하게 타격된다.
    /// </summary>
    [AddComponentMenu("Yeolha/Environment/Breakable Prop")]
    public class BreakableProp : MonoBehaviour, IDamageReceiver
    {
        [Header("Health")]
        [Tooltip("파괴에 필요한 총 피해량. 1이면 한 방에 파괴")]
        [SerializeField] private float maxHealth = 1f;

        [Header("Debris")]
        [Tooltip("파괴 시 스폰할 파편 프리팹(예: crate_02_debris). 자식 Rigidbody에 폭발력이 적용된다")]
        [SerializeField] private GameObject debrisPrefab;

        [Header("Explosion")]
        [Tooltip("파편에 가할 폭발력")]
        [SerializeField] private float explosionForce = 4f;
        [Tooltip("폭발 반경")]
        [SerializeField] private float explosionRadius = 1.5f;
        [Tooltip("폭발 상승 보정")]
        [SerializeField] private float explosionUpwardModifier = 0f;

        [Header("Chain Break")]
        [Tooltip("파괴 시 바로 위에 얹힌 다른 BreakableProp도 함께 파괴한다(공중 부양 방지)")]
        [SerializeField] private bool chainBreakAbove = true;
        [Tooltip("자기 콜라이더 상단부터 위로 탐지할 거리")]
        [SerializeField] private float chainBreakDistance = 1.0f;

        [Header("Events")]
        [Tooltip("파괴 순간 발화(사운드/이펙트 MMFeedbacks 연결용)")]
        public UnityEvent onBroken;

        private float _hp;
        private bool _died;
        private Collider _cachedCollider;
        private Bounds _cachedBounds;
        private bool _hasCachedBounds;

        /// <summary>이미 파괴됐는지.</summary>
        public bool IsBroken => _died;

        private void Awake()
        {
            _hp = Mathf.Max(1f, maxHealth);
            _cachedCollider = GetComponentInChildren<Collider>();
            RefreshBoundsCache();
        }

        private void RefreshBoundsCache()
        {
            if (_cachedCollider != null && _cachedCollider.enabled)
            {
                _cachedBounds = _cachedCollider.bounds;
                _hasCachedBounds = true;
            }
        }

        /// <summary>IDamageReceiver 진입점. 누적 피해가 maxHealth 이상이면 파괴.</summary>
        public void ReceiveDamage(DamageInfo damage)
        {
            if (_died) return;

            RefreshBoundsCache(); // 콜라이더가 살아있을 때 bounds 확보(파괴 직전)
            _hp -= Mathf.Max(0f, damage.Damage);
            if (_hp <= 0f)
                Break(damage.HitPoint);
        }

        /// <summary>즉시 파괴. 연쇄 파괴에서도 호출된다.</summary>
        public void Break(Vector3 hitPoint)
        {
            if (_died) return;
            _died = true;

            TryChainBreakAbove();
            SpawnDebris(hitPoint);
            onBroken?.Invoke();

            Destroy(gameObject);
        }

        private void SpawnDebris(Vector3 hitPoint)
        {
            if (debrisPrefab == null) return;

            GameObject debris = Instantiate(debrisPrefab, transform.position, transform.rotation);
            debris.transform.localScale = transform.localScale;

            // 파편 물리 영구 잔존 방지: 정착 후 kinematic 동결(프리팹 미수정, 런타임 부착)
            if (debris.GetComponent<DebrisSettle>() == null)
                debris.AddComponent<DebrisSettle>();

            int count = debris.transform.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = debris.transform.GetChild(i);
                if (child.TryGetComponent(out Rigidbody rb))
                {
                    rb.AddExplosionForce(
                        explosionForce, hitPoint, explosionRadius,
                        explosionUpwardModifier, ForceMode.Impulse);
                }
            }
        }

        /// <summary>자기 콜라이더 바로 위 영역을 OverlapBox로 훑어, 얹혀 있던 BreakableProp을 연쇄 파괴한다.</summary>
        private void TryChainBreakAbove()
        {
            if (!chainBreakAbove || !_hasCachedBounds) return;

            Bounds b = _cachedBounds;
            Vector3 center = new Vector3(b.center.x, b.max.y + chainBreakDistance * 0.5f, b.center.z);
            Vector3 halfExtents = new Vector3(b.extents.x * 0.9f, chainBreakDistance * 0.5f, b.extents.z * 0.9f);

            Collider[] hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].transform.IsChildOf(transform)) continue;

                BreakableProp other = hits[i].GetComponentInParent<BreakableProp>();
                if (other == null || other == this || other._died) continue;

                // 아래에서 부서져 올라오는 폭발처럼 보이도록 히트 지점을 자기 상단으로 지정
                other.Break(new Vector3(b.center.x, b.max.y, b.center.z));
            }
        }
    }
}
