namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 콤보 분기 게이트 — AttackAction 프리팹에 함께 붙은 컴포넌트가 구현하면, CharacterCombat이 Transition/시동기 해석 시
    /// 해당 AttackAction으로 분기하기 전에 CanBranch를 묻는다. false면 그 분기는 건너뛰고 다음 후보(목록 순서)를 본다.
    /// 예: GrabAttackLink — 잡을 대상이 없으면 잡기 공격으로 분기하지 않아 헛스윙(폴백 스윙)이 나오지 않는다 (구버전 IComboBranchGate 이관).
    /// </summary>
    public interface IComboBranchGate
    {
        bool CanBranch(Character attacker);
    }
}
