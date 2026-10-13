using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 날아가는 피격자의 "몸통 충돌" 판정 헬퍼 (AttackAction.bodyCollision).
    ///
    /// 밀림(Hit·Push)/넉다운/에어본으로 날아가는 캐릭터가 비행 중 다른 적과 겹치면
    /// 그 적에게도 피해를 주고 넉다운시킨다(볼링핀 연쇄). 캐릭터 간 하드 충돌은
    /// 꺼져 있으므로(소프트 콜리전) 물리 충돌 대신 OverlapSphere로 Hurtbox를 탐색한다.
    ///
    /// 소유/수명: 강제 반응 상태(Hit(밀림)/Knockdown/Airborne)가 Enter에서 TryCreate로 만들고
    /// 비행 구간 Tick마다 Sweep을 호출한다. 상태가 끝나면 참조를 버리면 된다(비-MonoBehaviour).
    ///
    /// 규칙:
    ///   - 피해자당 1회만 타격 (비행 1회 = 활성 구간 1회와 동일한 중복 방지)
    ///   - 원 공격자(Attacker) 기준 적대(AIController.IsHostile) 대상만 타격 — 아군 오사 방지
    ///   - 연쇄 피격자의 DamageInfo에는 BodyImpact가 실리지 않아 무한 연쇄가 없다
    ///   - 비행 속도가 MinImpactSpeed 미만이면 판정하지 않는다 (감쇠 후 스침 방지)
    /// </summary>
    public class FlyingBodyImpact
    {
        // 연쇄 넉다운 밀림 = 본인 현재 비행 속도 × 비율 (구프로젝트 CharacterAirborneBodyDamage.KnockbackForceRatio=0.5 이관).
        // 세게 날아갈수록 부딪힌 적도 세게 밀린다. Min은 쓰러짐 최소 보장, Max는 과도한 연쇄 방지.
        private const float ChainForceRatio = 0.5f;
        private const float ChainForceMin = 3f;
        private const float ChainForceMax = 10f;
        private const float ChainKnockbackDuration = 0.25f;
        // 이 속도(m/s) 미만으로 느려지면 몸통 판정을 멈춘다.
        private const float MinImpactSpeed = 2f;
        // 판정 중심의 지면 기준 높이(가슴 높이).
        private const float CenterHeight = 0.9f;

        private static readonly Collider[] _buffer = new Collider[16];

        private readonly Transform _victimRoot;
        private readonly CharacterMovement _victimMovement;
        private readonly GameObject _attacker;
        private readonly Character _attackerCharacter;
        private readonly float _damage;
        private readonly float _radius;
        private readonly Vector3 _fallbackDirection;
        private readonly HashSet<Character> _hit = new HashSet<Character>();

        private FlyingBodyImpact(Transform victimRoot, CharacterMovement victimMovement,
            GameObject attacker, float damage, float radius, Vector3 fallbackDirection)
        {
            _victimRoot = victimRoot;
            _victimMovement = victimMovement;
            _attacker = attacker;
            _attackerCharacter = attacker != null ? attacker.GetComponentInParent<Character>() : null;
            _damage = damage;
            _radius = radius;
            _fallbackDirection = fallbackDirection;
        }

        /// <summary>
        /// 강제 반응 상태 Enter에서 호출 — ReactionData에 BodyImpact가 실려 있으면 헬퍼를 만든다.
        /// 없으면 null (호출부는 null이면 Sweep을 건너뛴다).
        /// </summary>
        public static FlyingBodyImpact TryCreate(CharacterStateManager machine, CharacterStateManager.ReactionData r)
        {
            if (r.BodyImpactDamage <= 0f || machine == null || machine.Root == null || machine.Movement == null)
                return null;
            float radius = r.BodyImpactRadius > 0f ? r.BodyImpactRadius : 0.8f;
            return new FlyingBodyImpact(machine.Root, machine.Movement, r.Attacker, r.BodyImpactDamage, radius, r.Direction);
        }

        /// <summary>비행 구간 Tick마다 호출 — 겹친 적대 Hurtbox에 피해 + 넉다운을 1회씩 적용한다.</summary>
        public void Sweep()
        {
            if (_victimRoot == null) return;

            // 실제로 날아가는 중일 때만 (수평+수직 속도 기준).
            Vector3 hVel = _victimMovement != null ? _victimMovement.HorizontalVelocity : Vector3.zero;
            float vVel = _victimMovement != null ? _victimMovement.VerticalVelocity : 0f;
            float speedSqr = hVel.sqrMagnitude + vVel * vVel;
            if (speedSqr < MinImpactSpeed * MinImpactSpeed) return;
            float chainForce = Mathf.Clamp(Mathf.Sqrt(speedSqr) * ChainForceRatio, ChainForceMin, ChainForceMax);

            Vector3 center = _victimRoot.position + Vector3.up * CenterHeight;
            int count = Physics.OverlapSphereNonAlloc(center, _radius, _buffer, ~0, QueryTriggerInteraction.Collide);
            if (count <= 0) return;

            Vector3 dir = hVel;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized
                : (_fallbackDirection.sqrMagnitude > 0.0001f ? _fallbackDirection.normalized : _victimRoot.forward);

            for (int i = 0; i < count; i++)
            {
                var hurtbox = _buffer[i] != null ? _buffer[i].GetComponent<Hurtbox>() : null;
                if (hurtbox == null || !(hurtbox.Receiver is Character target)) continue;

                // 자기 자신(날아가는 본인) / 원 공격자 제외
                if (hurtbox.OwnerRoot == _victimRoot || hurtbox.OwnerRoot.IsChildOf(_victimRoot)) continue;
                if (target == _attackerCharacter) continue;

                // 원 공격자 기준 적대 관계만 (아군 오사 방지 — 규칙은 AIController.IsHostile 단일 소유)
                if (_attackerCharacter != null && !AIController.IsHostile(_attackerCharacter, target)) continue;

                if (!_hit.Add(target)) continue;

                var info = new DamageInfo(_attacker, _damage, center, dir);
                info.Reaction = new HitReactionSpec
                {
                    Kind = HitReactionKind.Knockdown,
                    HorizontalForce = chainForce,
                    PushDuration = ChainKnockbackDuration,
                };
                target.ReceiveDamage(info);
            }
        }
    }
}
