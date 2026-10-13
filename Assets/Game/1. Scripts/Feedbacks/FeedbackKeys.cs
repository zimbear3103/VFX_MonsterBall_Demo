namespace Aiara
{
    /// <summary>
    /// FeedbackManager에서 사용하는 통합 피드백 키 상수 모음.
    /// 캐릭터/어빌리티/무기 어디서든 동일 키를 호출하면 같은 MMF_Player가 재생됩니다.
    /// 키 추가/이름 변경 시 인스펙터의 FeedbackRegistrar Entries도 함께 갱신해야 합니다.
    /// </summary>
    public static class FeedbackKeys
    {
        // 이동/점프
        public const string Run = "Run";
        public const string HeavyWalk = "HeavyWalk";
        public const string Jump = "Jump";
        public const string JumpBoost = "JumpBoost";
        public const string Landing = "Landing";
        public const string LandingSmall = "Landing_small"; // 적 그라운드 바운스 2회째 이후 랜딩
        public const string HeavyLanding = "HeavyLanding";
        public const string DashStart = "DashStart";
        public const string DashEnd = "DashEnd";
        public const string CancelMove = "CancelMove";
        public const string Breath = "Breath";

        // 일반 공격
        public const string Slash = "Slash";
        public const string SlashCharged = "SlashCharged"; // 파워차징 릴리즈 전용(파란 이미션 검기)
        public const string Claw = "Claw";
        public const string Sting = "Sting";
        public const string StingJangHyu = "StingJangHyu"; // 플레이어 4타 찌르기(지연 단축판)
        public const string AttackMark = "AttackMark";
        public const string Punch = "Punch"; // 캐리 피니셔 발차기 임팩트 (Punch_Feedback)

        // 스모크
        public const string NormalSmoke = "NormalSmoke";
        public const string HeavySmoke = "HeavySmoke";
        public const string BigSmoke = "BigSmoke";
        public const string UpperSmoke = "UpperSmoke";
        public const string SpinAttackSmoke = "SpinAttackSmoke";

        // 특수 공격
        public const string Grab = "Grab";
        public const string HandCannon = "HandCannon";
        public const string MuzzleFlash = "MuzzleFlash";
        public const string Lens = "Lens";
        public const string SpinAttack = "SpinAttack";
        public const string HandCannonBurst = "HandCannonBurst";
        public const string MiniBurst = "MiniBurst";
        public const string GroundBurst = "GroundBurst";
        public const string Bomb = "Bomb";
        public const string AdditionalBomb = "AdditionalBomb";
        public const string SelfDestruct = "SelfDestruct";
        public const string BirdAuraBlade = "BirdAuraBlade";
        public const string CrasherHomingMissile = "CrasherHomingMissile";

        // 피격
        public const string Hit = "Hit";
        public const string HitLight = "HitLight";
        public const string HitCritical = "HitCritical";
        public const string HitBomb = "HitBomb";
        public const string ElectricHit = "ElectricHit";
        public const string ElectricHitLight = "ElectricHitLight";
        public const string ElectricHitCritical = "ElectricHitCritical";

        // 절명기
        public const string ExecutionHit = "ExecutionHit";
        public const string ExecutionSplash = "ExecutionSplash";
        public const string BloodBurst = "BloodBurst";

        // 가드/패링
        public const string Guard = "Guard";
        public const string GuardBreak = "GuardBreak";
        public const string Parry = "Parry";
        public const string PerfectParry = "PerfectParry";
        public const string PerfectParried = "PerfectParried";
        public const string ParrySmoke = "ParrySmoke";

        // 처형 인트로
        public const string Takedown = "Takedown";

        // 카메라 (FeedbackManager_JHS Variant에서 등록)
        public const string GrabShake = "GrabShake";
        public const string HeavyZoomOut = "HeavyZoomOut";

        // 사망
        public const string Death = "Death";

        // 잠재능력/자폭 폭발 (적 사망 폭발 이펙트 재사용)
        // LatentBurst = 한자(MobDestroy) 제외판, SelfDestructBurst = 한자 포함판
        public const string LatentBurst = "LatentBurst";
        public const string SelfDestructBurst = "SelfDestructBurst";
    }
}
