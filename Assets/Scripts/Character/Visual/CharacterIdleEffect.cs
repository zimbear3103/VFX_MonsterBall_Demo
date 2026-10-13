using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Idle 상태에서만 지정한 이펙트 GameObject들을 켜는 순수 시각 컴포넌트.
    ///
    /// 동작:
    ///   Idle 진입 → IdleEffects 전부 SetActive(true)
    ///   Idle 이탈 → IdleEffects 전부 SetActive(false)
    /// (이동/공격/피격/사망 등 Idle이 아닌 모든 상태에서 자동으로 꺼진다.)
    ///
    /// CharacterStateManager.StateChanged 이벤트만 구독한다 — Character/State/Combat 등
    /// 코어 프레임워크의 구조나 책임은 전혀 건드리지 않는다(PlayerRespawnController와 동일한 방식).
    /// Character와 같은 GameObject(루트)에 붙이는 것을 권장하며, 자식에 붙어도 부모에서 Character를 찾는다.
    /// </summary>
    [AddComponentMenu("Yeolha/Character/Character Idle Effect")]
    public class CharacterIdleEffect : MonoBehaviour
    {
        [Header("Effects")]
        [Tooltip("Idle 상태에서만 켤 이펙트 오브젝트들. Idle 진입 시 SetActive(true), 이탈 시 SetActive(false).")]
        public List<GameObject> IdleEffects = new List<GameObject>();

        private Character _character;
        private CharacterStateManager _stateManager;
        private bool _subscribed;
        private bool _hasApplied;
        private bool _effectsOn;

        private void Awake()
        {
            ResolveCharacter();
        }

        private void OnEnable()
        {
            TrySubscribe();
            ApplyForState(CurrentState());
        }

        private void Start()
        {
            // Awake 시점에 StateManager가 아직 준비되지 않았을 수 있어 재시도한다.
            TrySubscribe();
            ApplyForState(CurrentState());
        }

        private void OnDisable()
        {
            Unsubscribe();
            SetEffects(false);
        }

        private void ResolveCharacter()
        {
            if (_character != null) return;
            _character = GetComponent<Character>();
            if (_character == null) _character = GetComponentInParent<Character>();
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            ResolveCharacter();
            if (_character == null) return;
            _stateManager = _character.StateManager;
            if (_stateManager == null) return;
            _stateManager.StateChanged += OnStateChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_stateManager != null) _stateManager.StateChanged -= OnStateChanged;
            _subscribed = false;
        }

        private CharacterStateType CurrentState()
        {
            if (_stateManager != null) return _stateManager.CurrentStateType;
            if (_character != null) return _character.CurrentState;
            return CharacterStateType.Idle;
        }

        private void OnStateChanged(CharacterStateType previous, CharacterStateType next)
        {
            ApplyForState(next);
        }

        private void ApplyForState(CharacterStateType state)
        {
            SetEffects(state == CharacterStateType.Idle);
        }

        private void SetEffects(bool on)
        {
            if (_hasApplied && _effectsOn == on) return;
            _hasApplied = true;
            _effectsOn = on;

            if (IdleEffects == null) return;
            for (int i = 0; i < IdleEffects.Count; i++)
            {
                GameObject go = IdleEffects[i];
                if (go != null && go.activeSelf != on)
                    go.SetActive(on);
            }
        }
    }
}
