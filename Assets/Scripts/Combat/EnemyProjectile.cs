using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 적 원거리 공격용 투사체 (구버전 HomingProjectile 이식 — 도적 백우치).
    ///
    /// - 스플라인 좌표와 무관하게 월드 공간에서 직진/유도 이동한다 (구버전도 월드 이동).
    /// - 대미지는 발사자(ProjectileLauncher)가 Launch()로 주입한다 — 수치 소유는 AttackAction.
    /// - 충돌 판정은 AttackHitbox와 동일 규약: Hurtbox → IDamageReceiver,
    ///   적대 판별은 AIController.IsHostile 단일 소유, 자기 자신/아군 타격 방지.
    /// - Trigger 이벤트를 위해 Kinematic Rigidbody를 스스로 보장한다 (AttackHitbox와 동일).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class EnemyProjectile : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("초기 속도 (m/s). 구버전 백우치 투사체 1=50, 2=80")]
        [SerializeField] private float speed = 50f;
        [Tooltip("가속도 (m/s^2). 구버전 5")]
        [SerializeField] private float acceleration = 5f;
        [Tooltip("수명 (초). 경과 시 자동 파괴. 구버전 7")]
        [SerializeField] private float lifetime = 7f;

        [Header("Homing")]
        [Tooltip("유도 여부. 켜면 타겟 방향으로 선회하며 비행")]
        [SerializeField] private bool homing = true;
        [Tooltip("유도 선회 속도 (도/초)")]
        [SerializeField] private float turnSpeed = 360f;
        [Tooltip("유도 시 조준할 타겟 기준 높이 오프셋 (m)")]
        [SerializeField] private float aimHeightOffset = 1f;

        [Header("Hit")]
        [Tooltip("명중 지점에 생성할 VFX 프리팹 (선택)")]
        [SerializeField] private GameObject hitVfxPrefab;
        [Tooltip("명중 VFX 자동 파괴 시간 (초). 0 이하 = 파괴 안 함")]
        [SerializeField] private float hitVfxLifetime = 3f;

        [Header("Explosion (optional)")]
        [Tooltip("충돌/소멸 시 생성할 폭발 프리팹(Explosion). 비우면 폭발 없음 — 대포 탄환용 (구버전 Projectile→폭발 연결)")]
        [SerializeField] private Explosion explosionPrefab;
        [Tooltip("명중 시 폭발 생성")]
        [SerializeField] private bool explodeOnHit = false;
        [Tooltip("수명 소멸 시 폭발 생성")]
        [SerializeField] private bool explodeOnExpire = false;
        [Tooltip("접촉 단일 대미지를 줄지. 폭발로 대미지를 줄 경우 꺼서 중복 방지")]
        [SerializeField] private bool dealContactDamage = true;

        private GameObject _attackerGO;
        private Character _attackerCharacter;
        private float _damage;
        private Transform _homingTarget;
        private float _currentSpeed;
        private float _dieTime;
        private bool _launched;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;

            // 정지 콜라이더끼리는 Trigger 이벤트가 발생하지 않으므로 Kinematic Rigidbody 보장 (AttackHitbox와 동일)
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        /// <summary>
        /// 발사 초기화. ProjectileLauncher가 Instantiate 직후 호출한다.
        /// direction은 월드 방향(수평), homingTarget이 있으면 유도 비행.
        /// </summary>
        public void Launch(Character attacker, float damage, Vector3 direction, Transform homingTarget)
        {
            _attackerCharacter = attacker;
            _attackerGO = attacker != null ? attacker.gameObject : null;
            _damage = damage;
            _homingTarget = homingTarget;
            _currentSpeed = speed;
            _dieTime = Time.time + lifetime;
            _launched = true;

            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

private void Update()
        {
            if (!_launched) return;

            _currentSpeed += acceleration * Time.deltaTime;

            if (homing && _homingTarget != null)
            {
                Vector3 aimPoint = _homingTarget.position + Vector3.up * aimHeightOffset;
                Vector3 to = aimPoint - transform.position;
                if (to.sqrMagnitude > 0.0001f)
                {
                    Quaternion look = Quaternion.LookRotation(to.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
                }
            }

            transform.position += transform.forward * (_currentSpeed * Time.deltaTime);

            if (Time.time >= _dieTime)
            {
                if (explodeOnExpire) SpawnExplosion(transform.position);
                Destroy(gameObject);
            }
        }

private void OnTriggerEnter(Collider other)
        {
            if (!_launched) return;

            var hurtbox = other.GetComponent<Hurtbox>();
            if (hurtbox == null) return;

            var receiver = hurtbox.Receiver;
            if (receiver == null) return;

            // 자기 자신(발사자) 타격 방지
            if (_attackerGO != null && hurtbox.OwnerRoot != null &&
                (hurtbox.OwnerRoot == _attackerGO.transform || hurtbox.OwnerRoot.IsChildOf(_attackerGO.transform)))
                return;

            // 진영(Faction) 필터 — 규칙은 AIController.IsHostile 단일 소유 (AttackHitbox와 동일)
            if (_attackerCharacter != null && receiver is Character victimCharacter &&
                !AIController.IsHostile(_attackerCharacter, victimCharacter))
                return;

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            Vector3 dir = hurtbox.OwnerRoot != null
                ? hurtbox.OwnerRoot.position - (_attackerGO != null ? _attackerGO.transform.position : transform.position)
                : transform.forward;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

            // 접촉 단일 대미지 (폭발로 대미지를 줄 경우 dealContactDamage=false로 중복 방지)
            if (dealContactDamage)
            {
                receiver.ReceiveDamage(new DamageInfo(_attackerGO, _damage, hitPoint, dir));
                if (_attackerCharacter != null && _attackerCharacter.Combat != null)
                    _attackerCharacter.Combat.NotifyAttackLanded(receiver as Character);
            }

            SpawnHitVfx(hitPoint);
            if (explodeOnHit) SpawnExplosion(hitPoint);
            Destroy(gameObject);
        }

        /// <summary>충돌/소멸 지점에 공용 폭발 생성 — 공격자(진영)를 넘겨 폭발이 적만 타격하게 한다.</summary>
        private void SpawnExplosion(Vector3 position)
        {
            if (explosionPrefab == null) return;
            var exp = Instantiate(explosionPrefab, position, Quaternion.identity);
            exp.SetAttacker(_attackerCharacter);
        }

        private void SpawnHitVfx(Vector3 position)
        {
            if (hitVfxPrefab == null) return;
            var vfx = Instantiate(hitVfxPrefab, position, Quaternion.identity);
            if (hitVfxLifetime > 0f)
                Destroy(vfx, hitVfxLifetime);
        }
    }
}
