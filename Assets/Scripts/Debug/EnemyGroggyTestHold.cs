using System.Reflection;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// [Debug] Execution (Jeolmyeong-gi) repeat-test helper. Keeps this enemy permanently Groggy and
    /// prevents it from actually dying / despawning when executed, so the finisher can be tested over and over.
    ///
    /// Why a plain revive is not enough: CharacterCombat.PlayDeathFeedback() deactivates the GameObject
    /// (SetActive(false)) after deathFeedbackDelay when despawnOnDeathFeedback is on (enemies like the green
    /// ghost use it). Once the object is inactive this component stops running and can no longer revive it.
    /// So, while active, this component neutralizes that enemy's death-despawn (and optionally its death VFX)
    /// via reflection - caching and restoring the originals - and revives Dead -> full HP + Groggy each LateUpdate.
    ///
    /// Pure debug helper. Touches no framework code. Do not ship on release prefabs.
    /// </summary>
    [AddComponentMenu("Yeolha/Debug/Enemy Groggy Test Hold")]
    [RequireComponent(typeof(Character))]
    public class EnemyGroggyTestHold : MonoBehaviour
    {
        [Header("Test Groggy")]
        [Tooltip("켜면 이 적을 항상 그로기로 유지하고, 처형/사망 시 즉시 풀피+그로기로 부활시켜 절명기를 반복 테스트한다")]
        [SerializeField] private bool keepGroggy = true;

        [Tooltip("그로기 유지 중 매 프레임 HP를 최대치로 채워 일반 공격 사망을 막는다 (처형 반복 테스트 안정화)")]
        [SerializeField] private bool pinHealthWhileGroggy = true;

        [Tooltip("테스트 중 사망 폭발 이펙트(deathFeedbackKey)도 끈다. 끄더라도 비활성화(despawn)는 항상 막는다")]
        [SerializeField] private bool suppressDeathVfx = true;

        private Character _character;
        private CharacterCombat _combat;

        // Reflection into CharacterCombat's private death-despawn config (debug-only).
        private static FieldInfo _fDespawn;
        private static FieldInfo _fDeathKey;
        private static bool _fieldsResolved;

        private bool _despawnSuppressed;
        private bool _keySuppressed;
        private bool _origDespawn;
        private string _origDeathKey;

        /// <summary>테스트 그로기 유지 On/Off (런타임 토글 가능).</summary>
        public bool KeepGroggy { get => keepGroggy; set => keepGroggy = value; }

        private void Awake()
        {
            _character = GetComponent<Character>();
            _combat = _character != null ? _character.Combat : null;
            ResolveFields();
        }

        private void OnDisable()
        {
            RestoreDeathConfig();
        }

        private void LateUpdate()
        {
            if (_character == null) return;
            if (_combat == null) _combat = _character.Combat;
            if (_combat == null) return;

            if (!keepGroggy) { RestoreDeathConfig(); return; }

            // Make sure this enemy's death cannot deactivate/explode it away during the test.
            ApplyDeathConfigSuppression();

            CharacterStateType state = _character.CurrentState;

            // Confirmed execution death (or any death) -> revive to full HP + Groggy (object stays active
            // because despawn is suppressed above).
            if (_character.IsDead || state == CharacterStateType.Dead)
            {
                _character.ResetHealth();
                _combat.EnterGroggy();
                return;
            }

            // While the execution is playing, do not interfere -> camera / anim finish fully.
            if (state == CharacterStateType.Executing || state == CharacterStateType.Executed)
                return;

            // Not groggy (recovered via timer / hit reaction) -> back to groggy.
            if (state != CharacterStateType.Groggy)
            {
                _combat.EnterGroggy();
                return;
            }

            // Keep HP topped while groggy -> a normal combo cannot chip it to death.
            if (pinHealthWhileGroggy && _character.CurrentHP < _character.MaxHP)
                _character.Heal(_character.MaxHP);
        }

        private static void ResolveFields()
        {
            if (_fieldsResolved) return;
            _fieldsResolved = true;
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
            _fDespawn = typeof(CharacterCombat).GetField("despawnOnDeathFeedback", F);
            _fDeathKey = typeof(CharacterCombat).GetField("deathFeedbackKey", F);
            if (_fDespawn == null)
                Debug.LogWarning("[EnemyGroggyTestHold] CharacterCombat.despawnOnDeathFeedback not found - enemy may still despawn on execution. Field renamed?");
        }

        // Neutralize this enemy's death-despawn (and optionally death VFX) so an execution kill cannot
        // deactivate the GameObject. Originals are cached and restored when the test is turned off.
        private void ApplyDeathConfigSuppression()
        {
            if (_combat == null) return;
            if (!_despawnSuppressed && _fDespawn != null)
            {
                _origDespawn = (bool)_fDespawn.GetValue(_combat);
                _fDespawn.SetValue(_combat, false);
                _despawnSuppressed = true;
            }
            if (suppressDeathVfx && !_keySuppressed && _fDeathKey != null)
            {
                _origDeathKey = (string)_fDeathKey.GetValue(_combat);
                _fDeathKey.SetValue(_combat, string.Empty);
                _keySuppressed = true;
            }
        }

        private void RestoreDeathConfig()
        {
            if (_combat == null) return;
            if (_despawnSuppressed && _fDespawn != null)
            {
                _fDespawn.SetValue(_combat, _origDespawn);
                _despawnSuppressed = false;
            }
            if (_keySuppressed && _fDeathKey != null)
            {
                _fDeathKey.SetValue(_combat, _origDeathKey);
                _keySuppressed = false;
            }
        }
    }
}
