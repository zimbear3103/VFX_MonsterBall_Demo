using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 공격 입력 종류. Controller가 입력 버튼을 이 의도로 변환해 CommandManager에 전달한다.
    /// 콤보 Transition의 RequiredInput 매칭에 사용. 향후 Skill 입력 확장용 슬롯 예약(시스템은 미구현).
    /// </summary>
    public enum AttackInputType
    {
        Light = 0,
        Heavy = 1,
        /// <summary>강공격 버튼(Y) 홀드(PlayerController.heavyHoldThreshold 이상). 탭은 Heavy, 홀드는 HeavyHold로 분기 (2026-09-04).</summary>
        HeavyHold = 2,
        Skill1 = 10,
        Skill2 = 11,
    }

    /// <summary>콤보 Transition의 지상/공중 조건. Any = 제한 없음.</summary>
    public enum ComboGroundCondition
    {
        Any = 0,
        Grounded = 1,
        Airborne = 2,
    }

    /// <summary>
    /// 방향 입력 조건 (스틱/키 ±0.5 기준). None = 제한 없음. 구버전 ↓+Y 파생 등 표현용.
    /// 수평/수직이 동시에 눌린 경우 "마지막으로 누른 축"이 우선한다(CharacterCommandManager.PreferHorizontalDirection).
    /// Left/Right = 화면(스플라인 진행) 기준. Horizontal = 좌우 무관 수평 입력(잡기 파생 등).
    /// Forward/Backward = 캐릭터가 보는 방향 기준(FacingRight) — 앞(→+X 등) 잡기 파생용 (2026-09-07).
    /// </summary>
    public enum ComboDirectionCondition
    {
        None = 0,
        Up = 1,
        Down = 2,
        Left = 3,
        Right = 4,
        Horizontal = 5,
        Forward = 6,
        Backward = 7,
    }

    /// <summary>
    /// 콤보 분기 1개 = "어떤 입력 + 어떤 조건이면 → 어떤 다음 공격".
    /// AttackAction.Transitions[]의 항목. 위에서부터 첫 번째로 조건을 만족하는 항목이 선택된다(우선순위 = 순서).
    /// 조건 평가/실행은 CharacterCombat이 담당하고, 여기는 순수 데이터만 담는다.
    /// 다른 ComboGroup의 AttackAction으로도 연결 가능(그룹은 정리용 경계일 뿐).
    /// </summary>
    [System.Serializable]
    public class AttackTransition
    {
        [Tooltip("이 분기를 발동시키는 공격 입력 종류 (Light = 약공격, Heavy = 강공격)")]
        public AttackInputType input = AttackInputType.Light;

        [Tooltip("true = 현재 공격이 실제로 적중했을 때만 이 분기 사용 가능 (허공이면 불가)")]
        public bool requireHit = false;

        [Tooltip("지상/공중 조건. Any = 제한 없음 (현재 지상 콤보는 Any 사용, 향후 공중 콤보 확장용)")]
        public ComboGroundCondition ground = ComboGroundCondition.Any;
        [Tooltip("방향 입력 조건. None = 제한 없음 (예: Down = ↓ 홈드 중에만)")]
        public ComboDirectionCondition direction = ComboDirectionCondition.None;


        [Tooltip("이어질 다음 공격. 다른 ComboGroup 소속 AttackAction도 지정 가능")]
        public AttackAction nextAttack;
    }
}
