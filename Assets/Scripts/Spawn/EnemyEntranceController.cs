using System.Collections;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 적 '등장 연출' 전용 드라이버 — 전투 AI와 분리.
    /// 등장 직전 단계(AirDrop 낙하 / OffScreen 즉시 진입 / Ground 즉시 진입)만 담당하고,
    /// 끝나면 EnemyCharacter.BeginEntering()으로 공통 진입(SpawnEntry=대열 합류)에 넘긴다.
    ///
    ///   Spawner → (AirDrop/OffScreen/Ground) EnemyEntranceController
    ///           → EnemyCharacter.BeginEntering() → 기존 AIController 정상 편입
    ///
    /// 스포너가 스폰 직후 프리팹에 부착하고 Begin*()을 호출한다. 진입이 시작되면 스스로 파괴된다.
    /// </summary>
    public class EnemyEntranceController : MonoBehaviour
    {
        private EnemyCharacter _enemy;
        private CharacterMovement _movement;

        // ── 벽 통과용 레이어 충돌 매트릭스 1회 설정 (게임 시작 시) ──
        // SpawnEntryPass 레이어는 벽(Wall_barrier=Bars 레이어)·캐릭터(CharacterBody)를 통과하되
        // 바닥/월드(Ground/Default 등)는 통과하지 않는다. CharacterController.Move가 레이어 충돌
        // 매트릭스를 따르므로(Physics.IgnoreCollision은 CC 스윕에 무효) 이 방식만 유효하다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SetupSpawnEntryLayerMatrix()
        {
            int pass = LayerMask.NameToLayer(EnemyCharacter.SpawnEntryPassLayerName);
            if (pass < 0) return; // 레이어 미정의 → 설정 생략(진입 시에도 통과 없이 안전하게 동작)

            IgnorePair(pass, LayerMask.NameToLayer("CharacterBody")); // 다른 캐릭터 몸통 통과(간격은 소프트 분리)
            IgnorePair(pass, LayerMask.NameToLayer("Bars"));          // 배틀 벽(Wall_barrier)이 이 레이어(8)
            Physics.IgnoreLayerCollision(pass, pass, true);           // 진입 중인 적끼리도 통과
            // Ground/Default 등과는 유지 → 착지·월드 충돌 정상.
        }

        private static void IgnorePair(int a, int b)
        {
            if (a >= 0 && b >= 0) Physics.IgnoreLayerCollision(a, b, true);
        }

        /// <summary>프리팹(적 GameObject)에 컨트롤러를 부착(중복 방지).</summary>
        public static EnemyEntranceController Attach(GameObject go)
        {
            var ctrl = go.GetComponent<EnemyEntranceController>();
            if (ctrl == null) ctrl = go.AddComponent<EnemyEntranceController>();
            return ctrl;
        }

        private bool Init(EnemyCharacter enemy)
        {
            _enemy = enemy;
            if (_enemy == null) { Destroy(this); return false; }
            _movement = _enemy.Movement;
            return true;
        }

        /// <summary>AirDrop: 위에서 낙하 → 착지 감지 → 공통 진입.</summary>
        public void BeginAirDrop(EnemyCharacter enemy, float diveSpeed)
        {
            if (!Init(enemy)) return;
            _enemy.SetEntranceControlled(true); // 낙하 동안 AI 판단 억제(공격 금지). 이동/중력은 그대로 진행.
            StartCoroutine(AirDropRoutine(diveSpeed));
        }

        /// <summary>OffScreen: 위치는 스포너가 화면 밖으로 잡아둠 → 즉시 공통 진입(벽 통과하며 걸어 들어옴).</summary>
        public void BeginOffScreen(EnemyCharacter enemy)
        {
            if (!Init(enemy)) return;
            _enemy.BeginEntering();
            Destroy(this);
        }

        /// <summary>Ground(기본): 지상 생성 → 즉시 공통 진입(대열 합류).</summary>
        public void BeginGround(EnemyCharacter enemy)
        {
            if (!Init(enemy)) return;
            _enemy.BeginEntering();
            Destroy(this);
        }

        private IEnumerator AirDropRoutine(float diveSpeed)
        {
            yield return null; // 1 프레임 후 실제 공중 상태가 반영됨(스폰 프레임의 stale grounded 대응)

            // 선택적 급강하(자기정리형 1회 속도 — _airborneActive를 쓰지 않아 별도 해제 불필요).
            if (diveSpeed > 0f && _movement != null) _movement.ApplyDownwardDive(diveSpeed);

            float timeout = Time.time + 8f; // 안전 타임아웃(끼임 방지)

            // 공중에 뜬 것 확인
            while (_movement != null && _movement.IsGrounded && Time.time < timeout)
                yield return null;
            // 착지 대기 (Ground 감지 = CharacterController.isGrounded 재사용)
            while (_movement != null && !_movement.IsGrounded && Time.time < timeout)
                yield return null;

            if (_enemy != null) _enemy.BeginEntering(); // 착지 후 공통 진입(대열 합류)
            Destroy(this);
        }

        private void OnDisable()
        {
            // 연출 중 비활성/파괴 시 진입제어 플래그가 남아 적이 영구 정지되지 않도록 복구.
            if (_enemy != null) _enemy.SetEntranceControlled(false);
        }
    }
}
