using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 애니메이션 클립 이벤트 공용 수신기. Animator가 있는 GameObject(Visual)에 부착한다.
    ///
    /// 구버전 클립들은 functionName="AnimationEvent" + string 파라미터(예: "Attack1",
    /// "AttackSmoke1", "Attack1_1_Effect") 형태의 이벤트를 갖고 있으며, 이 컴포넌트가
    /// 그 이벤트를 받아 현재 실행 중인 AttackAction(CharacterCombat.CurrentAction)으로
    /// 그대로 전달한다. 어떤 이벤트로 무엇을 할지는 각 AttackAction의 데이터
    /// (attackVisuals[].animationEventName / hitboxOnEventName)가 결정한다 — 캐릭터별 분기 없음.
    ///
    /// 공격 미실행 중이거나 매핑되지 않은 이벤트(WalkEffect, Reset 등)는 조용히 무시한다.
    /// </summary>
    [AddComponentMenu("Yeolha/Combat/Attack Animation Event Receiver")]
    public class AttackAnimationEventReceiver : MonoBehaviour
    {
        [Tooltip("이벤트 수신 로그 출력 (디버그용)")]
        [SerializeField] private bool verboseLogging = false;

        private Character _character;

        private void Awake()
        {
            _character = GetComponentInParent<Character>();
        }

        /// <summary>클립 이벤트 진입점 (functionName="AnimationEvent", string 파라미터).</summary>
        public void AnimationEvent(string eventName)
        {
            if (verboseLogging)
                Debug.Log($"[AttackAnimEvent][{name}] '{eventName}'", this);

            if (_character == null || _character.StateManager == null) return;
            var combat = _character.StateManager.Combat;
            var action = combat != null ? combat.CurrentAction : null;
            if (action != null)
                action.HandleAnimationEvent(eventName);
        }

        /// <summary>사다리 등반 클립 이벤트 — 위로 한 칸 이동. (AnimationEvent functionName = "climbingUp")</summary>
        public void climbingUp()
        {
            if (verboseLogging) Debug.Log($"[AnimEvent][{name}] climbingUp", this);
            if (_character != null && _character.StateManager != null)
                _character.StateManager.NotifyClimbStep(1);
        }

        /// <summary>사다리 등반 클립 이벤트 — 아래로 한 칸 이동. (AnimationEvent functionName = "climbingDown")</summary>
        public void climbingDown()
        {
            if (verboseLogging) Debug.Log($"[AnimEvent][{name}] climbingDown", this);
            if (_character != null && _character.StateManager != null)
                _character.StateManager.NotifyClimbStep(-1);
        }

        /// <summary>사다리 탈출 클립 마지막 프레임 이벤트 — 하차 잠금 해제.
        /// (AnimationEvent functionName = "ladderExitEnd") 이벤트를 넣지 않으면 사다리의
        /// Exit Lock Duration 타이머로 풀린다.</summary>
        public void ladderExitEnd()
        {
            if (verboseLogging) Debug.Log($"[AnimEvent][{name}] ladderExitEnd", this);
            if (_character != null && _character.StateManager != null)
                _character.StateManager.NotifyLadderExitEnd();
        }

    }
}
