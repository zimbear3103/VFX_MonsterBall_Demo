using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 공용 공격 판정 컴포넌트. Attack Effect Prefab의 Hitbox 자식에 붙는다.
    ///
    /// 책임:
    ///   - Activate/Deactivate로 판정 창(Active Window) 제어
    ///   - Hurtbox 충돌 → IDamageReceiver로 Damage 전달
    ///   - 한 번의 활성 구간에서 같은 대상 중복 타격 방지
    ///   - 자기 자신(공격자) 타격 방지
    ///
    /// Input / Animator / 상태머신은 알지 않는다.
    /// 레이어 필터는 Physics Collision Matrix(PlayerHitbox ↔ EnemyHurtbox)가 담당.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class AttackHitbox : MonoBehaviour
    {
        [Tooltip("판정에 쓸 Trigger Collider (비우면 자기 자신)")]
        [SerializeField] private Collider hitCollider;

        [Header("Debug")]
        [Tooltip("Scene View에서 Hitbox 범위 Gizmo 표시 (비활성=회색, 활성=빨강)")]
        [SerializeField] private bool drawGizmo = true;

        private GameObject _attacker;
        private Character _attackerCharacter;
        private float _damage;
        // 피격 반응 프로파일 (AttackAction.ConfigureReaction가 활성화 전에 주입) — 활성 구간 내내 유지.
        private HitReactionSpec _reaction;
        private bool _unblockable;
        private bool _hitsDowned;
        // 몸통 충돌(볼링핀 연쇄) 프로파일 (AttackAction.ConfigureBodyImpact가 주입)
        private float _bodyImpactMultiplier;
        private float _bodyImpactRadius;
        // 치명타 프로파일 (AttackAction.ConfigureCritical가 주입) — 활성 구간 시작 시 1회 Roll.
        private bool _canCritical;
        private float _critMultiplier = 1.5f;
        private bool _forceCritical;
        private bool _isCriticalThisActivation;
        // 피격 이펙트 표시 프로파일 (AttackAction.Play가 주입) — 기본 true, false면 이 공격은 Hit 이펙트를 생략.
        private bool _showHitEffect = true;

        private bool _active;
        private readonly HashSet<IDamageReceiver> _hitThisActivation = new HashSet<IDamageReceiver>();

        /// <summary>현재 판정 창이 열려 있는가.</summary>
        public bool IsActive => _active;
        /// <summary>이번 활성 구간(마지막 Activate 이후)에 1회 이상 타격했는가. 콤보 히트 게이트(구버전 RequireHitFromComboIndex 이식)가 사용.</summary>
        public bool HasHitThisActivation => _hitThisActivation.Count > 0;

        /// <summary>
        /// 활성 구간당 1회, 첫 적중 시 발화. 여러 적이 한 스윙에 맞아도 1번만 호출된다.
        /// AttackAction이 적중 게이트 연출(슬로우모션/진동)을 여기에 연결한다.
        /// </summary>
        public System.Action HitLanded;

        /// <summary>
        /// 이 히트박스를 소유한 AttackAction — AttackAction.Play가 주입한다.
        /// 적중 통지(NotifyAttackLanded)에 함께 전달해, 통지 시점에 공격이 이미 인터럽트/종료되어
        /// Combat._currentAction이 비어 있어도 공격 데이터(공격자 자기상승 등)를 잃지 않게 한다.
        /// </summary>
        public AttackAction SourceAction { get; set; }

        /// <summary>피격 이펙트 표시 여부 — AttackAction.Play가 주입. false면 DamageInfo.ShowHitEffect=false로 전달되어 피격자가 Hit 이펙트를 생략한다. Activate/Deactivate로 초기화되지 않는다.</summary>
        public bool ShowHitEffect { get => _showHitEffect; set => _showHitEffect = value; }

        /// <summary>피격 리액션 억제 — AttackAction이 히트박스를 열 때(TimedVisual.hitReaction=false) 주입. true면 DamageInfo.SuppressHitReaction=true로 전달되어 피격자가 리액션 상태(경직/넉백/런치) 진입을 건너뛴다. 활성 구간 단위로 매번 재설정된다.</summary>
        public bool SuppressHitReaction { get; set; }

        /// <summary>피격 반응 프로파일 주입 — AttackAction.Play가 판정 시작 전에 호출. 값은 Activate/Deactivate로 초기화되지 않고 다음 Configure까지 유지된다.</summary>
        public void ConfigureReaction(HitReactionSpec reaction, bool unblockable, bool hitsDowned = false)
        {
            _reaction = reaction;
            _unblockable = unblockable;
            _hitsDowned = hitsDowned;
        }


        /// <summary>몸통 충돌 프로파일 주입 — AttackAction.Play가 판정 시작 전에 호출. multiplier 0 = 미사용.</summary>
        public void ConfigureBodyImpact(float damageMultiplier, float radius)
        {
            _bodyImpactMultiplier = damageMultiplier;
            _bodyImpactRadius = radius;
        }

        /// <summary>치명타 프로파일 주입 — AttackAction.Play가 판정 시작 전에 호출. ConfigureReaction과 동일하게 다음 Configure까지 유지.</summary>
        public void ConfigureCritical(bool canCritical, float critMultiplier, bool forceCritical)
        {
            _canCritical = canCritical;
            _critMultiplier = critMultiplier > 0f ? critMultiplier : 1.5f;
            _forceCritical = forceCritical;
        }

        
private void Awake()
        {
            if (hitCollider == null) hitCollider = GetComponent<Collider>();
            hitCollider.isTrigger = true;
            hitCollider.enabled = false;

            // 정지 콜라이더끼리는 Trigger 이벤트가 발생하지 않으므로 Kinematic Rigidbody 필요.
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        /// <summary>판정 시작. 새 활성 구간이므로 중복 타격 기록을 초기화한다.</summary>
        public void Activate(GameObject attacker, float finalDamage)
        {
            _attacker = attacker;
            _attackerCharacter = attacker != null ? attacker.GetComponentInParent<Character>() : null;
            _damage = finalDamage;
            // 치명타 Roll — 한 활성 구간(스윙)당 1회. 공격자 FinalCriticalRate가 기본 확률, ForceCritical이면 확정.
            _isCriticalThisActivation = _canCritical && _attackerCharacter != null &&
                (_forceCritical || Random.value < _attackerCharacter.Stats.FinalCriticalRate);

            _hitThisActivation.Clear();
            _active = true;
            hitCollider.enabled = true;
        }

        /// <summary>판정 종료.</summary>
        public void Deactivate()
        {
            _active = false;
            if (hitCollider != null) hitCollider.enabled = false;
        }

        private void OnDisable()
        {
            Deactivate();
        }

        private void OnTriggerEnter(Collider other) => TryHit(other);
        private void OnTriggerStay(Collider other) => TryHit(other);

        private void TryHit(Collider other)
        {
            if (!_active) return;

            var hurtbox = other.GetComponent<Hurtbox>();
            if (hurtbox == null) return;

            var receiver = hurtbox.Receiver;
            if (receiver == null) return;

            // 자기 자신 타격 방지
            if (_attacker != null && hurtbox.OwnerRoot != null &&
                (hurtbox.OwnerRoot == _attacker.transform || hurtbox.OwnerRoot.IsChildOf(_attacker.transform)))
                return;

            // 진영(Faction) 필터 — 적대 관계가 아니면 타격하지 않음 (아군 오사 방지, 규칙은 AIController.IsHostile 단일 소유)
            if (_attackerCharacter != null && receiver is Character victimCharacter &&
                !AIController.IsHostile(_attackerCharacter, victimCharacter))
                return;

            // 같은 활성 구간에서 같은 대상 재타격 금지
            if (!_hitThisActivation.Add(receiver)) return;

            Vector3 hitPoint = other.ClosestPoint(hitCollider.bounds.center);
            Vector3 dir = hurtbox.OwnerRoot.position -
                          (_attacker != null ? _attacker.transform.position : transform.position);
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

            float finalDamage = _isCriticalThisActivation ? _damage * _critMultiplier : _damage;
            var info = new DamageInfo(_attacker, finalDamage, hitPoint, dir);
            info.IsCritical = _isCriticalThisActivation;
            info.ShowHitEffect = _showHitEffect;
            info.Unblockable = _unblockable;
            info.Reaction = _reaction;
            info.SuppressHitReaction = SuppressHitReaction;
            info.HitsDowned = _hitsDowned;
            info.BodyImpactDamage = _bodyImpactMultiplier > 0f ? finalDamage * _bodyImpactMultiplier : 0f;
            info.BodyImpactRadius = _bodyImpactRadius;
            receiver.ReceiveDamage(info);

            // 공격자 측 적중 통지 — 투혼 충전/FP 적중 회복/처치 충전 (구버전 ChargeAttackHit 트리거 이식)
            // SourceAction을 함께 전달 — 같은 프레임에 공격이 인터럽트되어 _currentAction이 비어도 리프트 등 공격 데이터 유지.
            if (_attackerCharacter != null && _attackerCharacter.Combat != null)
                _attackerCharacter.Combat.NotifyAttackLanded(receiver as Character, SourceAction);

            // 적중 게이트 연출 콜백 — 활성 구간(스윙)당 첫 적중에서만 1회 (다수 적중 시 가중 방지)
            if (_hitThisActivation.Count == 1) HitLanded?.Invoke();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawGizmo) return;
            var col = hitCollider != null ? hitCollider : GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = _active ? new Color(1f, 0.15f, 0.1f, 0.9f) : new Color(0.6f, 0.6f, 0.6f, 0.5f);
            Gizmos.matrix = col.transform.localToWorldMatrix;
            if (col is BoxCollider box)
                Gizmos.DrawWireCube(box.center, box.size);
            else if (col is SphereCollider sphere)
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            else if (col is CapsuleCollider capsule)
                Gizmos.DrawWireCube(capsule.center, new Vector3(capsule.radius * 2f, capsule.height, capsule.radius * 2f));
        }
#endif
    }
}
