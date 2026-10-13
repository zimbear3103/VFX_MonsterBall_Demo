namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 행동 상태 ID (Player / Enemy 공용).
    /// 캐릭터의 현재 행동 상태는 CharacterStateManager.CurrentStateType 하나만 진실의 원천이다.
    /// 이동과 전투를 분리하지 않고 하나의 상태 머신에서 통합 관리한다.
    /// 공격은 큰 행동 상태로 Attack 하나만 두고, 실제 어떤 공격을 실행 중인지는
    /// CharacterCombat.CurrentAttack(AttackType)이 별도로 관리한다.
    /// 피격 반응은 Hit 하나만 둔다(이번 단계에서는 Down/Stun/Dead로 확장하지 않는다).
    /// </summary>
    public enum CharacterStateType
    {
        // ─── 이동 ───
        Idle,
        Move,
        Run,
        Jump,
        Fall,
        Land,
        Dash,

        // ─── Traversal (ladder / ledge / mantle) ───
        LadderClimb,
        LedgeHang,
        Mantle,


        // ─── 전투 ───
        Attack,
        Guard,

        // ─── 특수(체인 그랩) ───
        ChainGrab,

        // ─── 특수(콤보 파생 잡기) ───
        Grab,       // ActionGrab 시전 중(공격자). 시퀀스는 CharacterActionGrab이 소유

        // ─── 강제 피격 반응 (공격 데이터가 지정) ───
        Knockdown,
        Airborne,
        Vulnerable, // 패링(일반/퍼펙트)당한 공격자. 이동/공격 잠금 + 무방비(가드/패링 불가). 시간 만료 시 복귀

        // ─── 처형(절명기): 그로기 → 처형 시전/피처형 ───
        Groggy,     // 투혼 0으로 무력화된 적. 처형 대상이 될 수 있음(취약)
        Executing,  // 처형 시전 중(공격자). 무적 + 접근 + 확정 처형
        Executed,   // 처형 당하는 중(피격자). 잠금 후 확정 사망

        // ─── 피격 ───
        Hit,

        // ─── 사망 ───
        Dead,
    }
}
