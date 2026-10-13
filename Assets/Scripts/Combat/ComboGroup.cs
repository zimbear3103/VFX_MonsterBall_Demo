using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 콤보 계열 정리용 논리 그룹 (예: NormalAttack_GroundCombo).
    /// AttackRoot 아래에서 같은 계열 AttackAction들을 자식으로 묶고, 시작 공격을 정의한다.
    ///
    /// ※ 그룹은 "정리 + 시작 공격 정의" 용도일 뿐, Transition을 제한하는 경계가 아니다.
    /// 다른 그룹의 AttackAction으로도 AttackTransition.nextAttack 연결이 가능하다.
    /// 런타임 콤보 흐름은 CharacterCombat + AttackAction.Transitions가 담당한다.
    /// </summary>
    public class ComboGroup : MonoBehaviour
    {
        [Header("Group")]
        [Tooltip("그룹 이름. 비우면 GameObject 이름 사용")]
        [SerializeField] private string groupName = "";

        [Tooltip("이 콤보 계열의 시작 공격. 비우면 첫 번째 자식 AttackAction")]
        [SerializeField] private AttackAction starterAttack;

        [Header("Starter Condition")]
        [Tooltip("이 그룹을 '시작 공격'으로 사용할지. false = 파생 전용 그룹(Transition으로만 진입)")]
        [SerializeField] private bool starterEnabled = true;
        [Tooltip("시작 입력 종류 (Light = 약공격 시작, Heavy = 강공격 시작)")]
        [SerializeField] private AttackInputType starterInput = AttackInputType.Light;
        [Tooltip("시작 상태 조건: Grounded=지상 / Running=달리기 중 / Airborne=공중 / AirCombo=공중콤보 Arm 중 / Any=제한 없음 / DashChord=지상+대시키·공격버튼 동시 입력")]
        [SerializeField] private StarterStateCondition starterState = StarterStateCondition.Grounded;
        [Tooltip("시작 방향 조건 (예: Down = ↓+버튼 시작)")]
        [SerializeField] private ComboDirectionCondition starterDirection = ComboDirectionCondition.None;
        [Tooltip("같은 입력에 여러 그룹이 매칭될 때 높은 값이 우선 (구버전 Priority 이식)")]
        [SerializeField] private int starterPriority = 0;

        public bool StarterEnabled => starterEnabled;
        public AttackInputType StarterInput => starterInput;
        public StarterStateCondition StarterState => starterState;
        public ComboDirectionCondition StarterDirection => starterDirection;
        public int StarterPriority => starterPriority;

        public string GroupName => string.IsNullOrEmpty(groupName) ? gameObject.name : groupName;

        /// <summary>이 그룹의 시작 공격. 미지정 시 첫 번째 자식 AttackAction.</summary>
        public AttackAction StarterAttack => starterAttack != null ? starterAttack : GetComponentInChildren<AttackAction>(true);

        /// <summary>이 그룹에 속한 모든 AttackAction (자식 기준).</summary>
        public AttackAction[] Attacks => GetComponentsInChildren<AttackAction>(true);
    }

    /// <summary>콤보 그룹 시작 상태 조건. 구버전 StateComboGroup.TargetState 이식.</summary>
    public enum StarterStateCondition
    {
        Grounded = 0,
        Running = 1,
        Airborne = 2,
        AirCombo = 3,
        Any = 4,
        /// <summary>지상 + (대시 키를 누른 채 공격 버튼 입력 또는 달리기 중). (2026-09-04 대시 강공격 입력 변경)</summary>
        DashChord = 5,
    }
}
