using UnityEngine;
using UnityEngine.SceneManagement;
using Aiara;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Player-only death/revive orchestration. Bridges the shared Character Dead state to the
    /// ported death UI (Aiara.ReviveGameOverUI) and respawn, WITHOUT putting any UI/player reference
    /// inside CharacterDeadState (which stays a generic, enemy-usable terminal state).
    ///
    /// Death detection uses the existing CharacterStateManager.StateChanged event (no core edit).
    /// The UI only presents and detects the hold-to-revive input, then calls back here; this
    /// controller owns the actual respawn. On revive it runs the fixed order:
    /// clear leftover runtime state -> restore resources -> (optional) reposition -> ChangeState(Idle).
    /// Enemies disengage/re-engage on their own via Character.IsDead.
    /// </summary>
    [RequireComponent(typeof(Character))]
    public class PlayerRespawnController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Ported GAME OVER / revive UI. If empty, resolved via ReviveGameOverUI.GetOrCreate() at death.")]
        [SerializeField] private ReviveGameOverUI reviveUI;

        [Header("Revives")]
        [Tooltip("Number of revives before the final game-over (restart) screen.")]
        [SerializeField, Min(0)] private int startingRevives = 1;
        [Tooltip("Seed the revive count from Ability.LifeCount (FinalLifeCount - 1) instead of Starting Revives.")]
        [SerializeField] private bool seedFromLifeCount = false;

        [Header("Respawn")]
        [Tooltip("Respawn in place at the death position. If false and a RespawnPoint is set, teleport there.")]
        [SerializeField] private bool respawnInPlace = true;
        [Tooltip("Optional respawn location used only when Respawn In Place is false.")]
        [SerializeField] private Transform respawnPoint;
        [Tooltip("Also restore FP to max on revive (Touhon is left as-is to preserve old behavior).")]
        [SerializeField] private bool restoreFPOnRevive = false;

        private Character _character;
        private CharacterStateManager _stateManager;
        private int _revivesLeft;
        private bool _awaitingChoice;

        private void Awake()
        {
            _character = GetComponent<Character>();
        }

        private void Start()
        {
            if (_character == null) _character = GetComponent<Character>();
            _character.Initialize();
            _stateManager = _character.StateManager;

            _revivesLeft = seedFromLifeCount && _character.Stats != null
                ? Mathf.Max(0, _character.Stats.FinalLifeCount - 1)
                : startingRevives;

            if (_stateManager != null)
                _stateManager.StateChanged += OnStateChanged;
        }

        private void OnDestroy()
        {
            if (_stateManager != null)
                _stateManager.StateChanged -= OnStateChanged;
        }

        // ─────────── Death detection (shared state -> player UI) ───────────

        private ReviveGameOverUI ResolveUI()
        {
            if (reviveUI == null) reviveUI = ReviveGameOverUI.GetOrCreate();
            return reviveUI;
        }

        private void Update()
        {
            // 안전망: 전이 이벤트를 놓쳤더라도 플레이어가 Dead 상태이면 사망 UI를 반드시 띄운다.
            if (!_awaitingChoice && _stateManager != null
                && _stateManager.CurrentStateType == CharacterStateType.Dead)
                TriggerDeathUI();
        }

        private void OnStateChanged(CharacterStateType previous, CharacterStateType next)
        {
            if (next == CharacterStateType.Dead) TriggerDeathUI();
        }

        private void TriggerDeathUI()
        {
            Debug.Log("트리거");
            if (_awaitingChoice) return;
            _awaitingChoice = true;

            var ui = ResolveUI();
            if (ui == null)
            {
                Debug.LogWarning("[PlayerRespawn] ReviveGameOverUI not found in scene; cannot show death screen.", this);
                return;
            }

            if (_revivesLeft > 0)
                ui.Show(OnReviveConfirmed);   // hold-to-revive
            else
                ui.Show(OnRestartConfirmed);  // revives exhausted -> restart
        }

        private void OnReviveConfirmed()
        {
            if (!_awaitingChoice) return;
            _revivesLeft = Mathf.Max(0, _revivesLeft - 1);
            _awaitingChoice = false;
            Respawn();
            if (reviveUI != null) reviveUI.Hide();
        }

        private void OnRestartConfirmed()
        {
            if (!_awaitingChoice) return;
            _awaitingChoice = false;
            if (reviveUI != null) reviveUI.Hide();
            var active = SceneManager.GetActiveScene();
            SceneManager.LoadScene(active.buildIndex);
        }

        // ─────────── Respawn: fixed order (clear -> restore -> reposition -> Idle) ───────────

        private void Respawn()
        {
            var combat = _character.Combat;
            var movement = _character.Movement;
            var commands = _character.CommandManager;

            // 1) Clear leftover runtime state carried from the death moment.
            combat?.CancelCurrentAttack();          // stop attack, disable hitbox, reset combo stage
            if (movement != null)
            {
                movement.EndKnockback();
                movement.EndAirborne();
                movement.ResetMovementVelocity();   // zero horizontal/vertical/dash/knockback/airborne
                movement.SetMoveSpeedMultiplier(1f);
            }
            commands?.OnDisableCleanup();           // drop buffered input + combo reservation

            // 2) Restore resources.
            _character.ResetHealth();               // HP -> max
            if (restoreFPOnRevive) _character.ResetFP();

            // 3) Reposition (in place by default).
            if (!respawnInPlace && respawnPoint != null && movement != null)
                movement.Teleport(respawnPoint.position);

            // 4) Leave Dead last, after state is clean.
            _stateManager?.ChangeState(CharacterStateType.Idle);
        }
    }
}
