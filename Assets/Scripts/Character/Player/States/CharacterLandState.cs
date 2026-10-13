namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 착지 상태. 향후 착지 애니메이션/VFX/SFX/강착지 연결 지점.
    /// 현재는 LandDuration(Inspector) 동안 유지 후 입력에 따라 지상 상태로 복귀한다.
    /// 이동 배율은 잠그지 않아 착지 중에도 관성/이동이 자연스럽게 이어진다.
    ///
    /// 전이:
    ///   접지 해제 + 하강      → Fall
    ///   Attack 입력           → Attack
    ///   Jump 입력             → Jump (착지 캔슬)
    ///   LandDuration 경과     → Idle / Move / Run (입력 기준)
    /// </summary>
    public class CharacterLandState : CharacterState
    {
        private float _timer;

        public override CharacterStateType Type => CharacterStateType.Land;

        public override void Enter()
        {
            _timer = 0f;
            // 착지 연기 (구버전 Landing 이벤트 — 클립 0.018s ≈ 즉시)
            // 최소 체공시간(0.2초) 이상 떴을 때만 피드백 플레이 — 미니점프 등 짧은 체공은 무음
            if (Machine.HadSufficientAirtime)
                Movement.PlayLandingFeedback();
            Machine.ResetLandingContext();
        }

        public override void Tick(float deltaTime)
        {
            if (CheckFall()) return;
            if (CheckAttack()) return;
            if (CheckJump()) return;

            _timer += deltaTime;
            if (_timer >= Machine.LandDuration)
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
        }
    }
}
