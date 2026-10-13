using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 취약(Vulnerable) 상태 — 패링(일반/퍼펙트)당한 공격자가 일정 시간 이동/점프/공격을 잠그고 무방비로 서 있는다.
    ///
    /// 진입: CharacterDefense.OnParrySuccess → 공격자 CharacterCombat.ReactToParried → EnterVulnerable(duration).
    /// 지속 시간은 패링한 쪽 CharacterDefense.ParryVulnerableDuration(일반)/PerfectParryVulnerableDuration(퍼펙트)이 결정하고
    /// Combat.VulnerableDuration에 저장된다.
    /// 애니는 별도 파라미터 없이 ReactToParried가 쏜 Parried 트리거를 그대로 유지한다.
    ///
    /// 이탈:
    ///   - 타이머 만료 → 지상이면 입력 기준 복귀(Idle/Move/Run), 공중이면 Fall.
    ///   - 피격 → CharacterCombat.ReceiveDamage가 방어 판정을 건너뛰고(무방비) 일반 피격 반응(Hit/Knockdown/Airborne)으로 덮어쓴다.
    /// (상태=잠금/타이밍, 진입 규칙=Combat 소유 — Groggy/Hit와 동일한 책임 분리.)
    /// </summary>
    public class CharacterVulnerableState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Vulnerable;

        private float _timer;

        public override void Enter()
        {
            _timer = 0f;
            Movement.CanMove = false;
            Movement.CanJump = false;
            Movement.SetMoveSpeedMultiplier(0f);
            if (Combat != null) Combat.CancelCurrentAttack();
        }

        public override void Tick(float deltaTime)
        {
            _timer += deltaTime;
            float duration = Combat != null ? Combat.VulnerableDuration : 0f;
            if (_timer < duration)
                return;

            if (Movement.IsGrounded)
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
            else
                Machine.ChangeState(CharacterStateType.Fall);
        }

        public override void Exit()
        {
            Movement.CanMove = true;
            Movement.CanJump = true;
        }
    }
}
