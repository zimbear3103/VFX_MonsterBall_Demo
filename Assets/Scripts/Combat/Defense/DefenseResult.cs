namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 방어 판정 결과 — CharacterDefense.Resolve가 반환하고 CharacterCombat.ReceiveDamage가 분기한다.
    /// 구버전 GuardableHealth.DefenseResult 이식(ShotParry 등 일부는 후속 단계).
    /// </summary>
    public enum DefenseResult
    {
        /// <summary>방어 없음 — 평타 피격 처리로 진행.</summary>
        None,
        /// <summary>가드 성공 — 데미지 감소, Hit 상태 진입 없음.</summary>
        Guard,
        /// <summary>가드 브레이크 — 게이지 소진 등으로 가드 실패, 풀 데미지 + 경직.</summary>
        GuardBreak,
        /// <summary>패링 성공 — 데미지 0, 공격자 반응(경직/프레임스톱). (Phase 2)</summary>
        Parry,
        /// <summary>퍼펙트 패링 — 패링 강화판, 공격자 강경직 + 콤보 취소. (Phase 2)</summary>
        PerfectParry,
    }
}
