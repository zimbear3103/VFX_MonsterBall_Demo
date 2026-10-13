using System.Collections;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// AttackAction 자식에 붙는 투사체 발사기 (구버전 ProjectileWeapon 이식 — 도적 백우치).
    ///
    /// 사용 규약 (코어 수정 없음):
    ///   - AttackAction의 자식 GameObject(기본 비활성)에 부착하고,
    ///     AttackAction.attackVisuals의 TimedVisual target으로 등록한다.
    ///   - 공격 시작 후 startDelay 시점에 기존 타이밍 시스템이 이 오브젝트를 켜면
    ///     OnEnable에서 shotDelays 스케줄대로 발사한다. Stop/취소 시 꺼지며 코루틴 정리.
    ///   - 대미지 = 부모 Character.Stats.FinalAttackPower × 부모 AttackAction.DamageMultiplier.
    /// </summary>
    public class ProjectileLauncher : MonoBehaviour
    {
        [Header("Projectile")]
        [Tooltip("발사할 투사체 프리팹 (EnemyProjectile)")]
        [SerializeField] private EnemyProjectile projectilePrefab;
        [Tooltip("발사 위치 (비우면 자기 자신 Transform)")]
        [SerializeField] private Transform muzzle;

        [Header("Timing")]
        [Tooltip("활성화(OnEnable) 기준 각 발사 지연 시간(초). 예: 단발 [0], 4연발 [0, 0.09, 1.16, 1.27]")]
        [SerializeField] private float[] shotDelays = { 0f };

        private Coroutine _routine;

        private void OnEnable()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(FireRoutine());
        }

        private void OnDisable()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        private IEnumerator FireRoutine()
        {
            if (projectilePrefab == null || shotDelays == null || shotDelays.Length == 0)
                yield break;

            float elapsed = 0f;
            for (int i = 0; i < shotDelays.Length; i++)
            {
                float wait = shotDelays[i] - elapsed;
                if (wait > 0f)
                {
                    yield return new WaitForSeconds(wait);
                    elapsed = shotDelays[i];
                }
                Fire();
            }
            _routine = null;
        }

        private void Fire()
        {
            var attacker = GetComponentInParent<Character>();
            if (attacker == null || projectilePrefab == null) return;

            // 수치 소유 규약: 공격력(Ability→RuntimeStats) × 기술 배율(AttackAction)
            float multiplier = 1f;
            var action = GetComponentInParent<AttackAction>();
            if (action != null) multiplier = action.DamageMultiplier;
            float damage = (attacker.Stats != null ? attacker.Stats.FinalAttackPower : 0f) * multiplier;

            Transform origin = muzzle != null ? muzzle : transform;

            // 발사 방향: 타겟이 있으면 타겟 쪽(수평), 없으면 Facing 쪽 스플라인 접선
            Transform homingTarget = null;
            Vector3 direction;
            var enemy = attacker as EnemyCharacter;
            if (enemy != null && enemy.Target != null)
            {
                homingTarget = enemy.Target.transform;
                Vector3 to = homingTarget.position - origin.position;
                to.y = 0f;
                direction = to.sqrMagnitude > 0.0001f ? to.normalized : origin.forward;
            }
            else if (attacker.Movement != null)
            {
                direction = attacker.Movement.SplineForward * (attacker.Movement.FacingRight ? 1f : -1f);
            }
            else
            {
                direction = origin.forward;
            }

            var projectile = Instantiate(projectilePrefab, origin.position, Quaternion.LookRotation(direction, Vector3.up));
            projectile.Launch(attacker, damage, direction, homingTarget);
        }
    }
}
