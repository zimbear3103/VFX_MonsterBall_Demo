using System;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 런타임 스탯 — CharacterAbility(Base Data, SO)를 기반으로 만드는 <b>인스턴스별 런타임 값</b>.
    ///
    /// 구 JangHyuStats를 대체한다. Base 수치는 CharacterAbility Asset에서 씨앗(seed)으로 복사해 오고,
    /// 버프/성장/장비 보너스와 CurrentHP 같은 <b>변하는 값</b>은 이 인스턴스에만 저장한다.
    ///   Final값 = (Ability에서 복사한 Base 런타임 사본) + Bonus
    ///
    /// 기획 22~24 / 51:
    ///   - ScriptableObject Asset 자체는 절대 수정하지 않는다.
    ///   - CurrentHP·사용 점프 횟수·대시 시간 등 런타임 상태는 Character 인스턴스별로 분리된다.
    ///   - 같은 Ability를 여러 캐릭터가 참조해도 서로 영향이 없다.
    ///
    /// Character가 소유(new)하고 Bind(ability)로 초기화한다.
    /// </summary>
    [Serializable]
    public class CharacterRuntimeStats
    {
        // ─────────── Ability에서 복사한 Base 런타임 사본 (원본 SO는 불변) ───────────

        private float _baseMaxHP = 100f;
        private float _baseDefense = 5f;
        private int _baseLifeCount = 1;

        private float _baseAttackPower = 10f;
        private float _baseCriticalRate = 0f;

        private float _baseMaxFP = 100f;
        private float _baseMaxTouhon = 0f;

        private float _baseMoveSpeed = 3f;
        private float _baseRunMultiplier = 2f;
        private float _baseJumpHeight = 1.6f;
        private int _baseJumpCount = 1;
        private float _baseDashSpeed = 18f;

        private float _baseHitDuration = 0.4f;
        private float _baseLandDuration = 0.12f;
        private float _baseGlobalAnimatorSpeed = 0.7f;
        private float _baseWalkAnimationSpeed = 2f;

        // ─────────── Bonus (성장/장비/버프 합산 — 런타임) ───────────

        private float _moveSpeedBonus;
        private float _jumpHeightBonus;
        private int _jumpCountBonus;
        private float _dashSpeedBonus;
        private float _attackPowerBonus;

        // ─────────── 런타임 자원 상태 ───────────

        /// <summary>현재 체력 (런타임). Ability.MaxHP가 아니라 이 인스턴스가 소유.</summary>
        public float CurrentHP { get; set; } = 100f;

        /// <summary>현재 FP (런타임 자원). 저장소는 이 인스턴스 — SO 불변.</summary>
        public float CurrentFP { get; set; } = 100f;

        /// <summary>현재 투혼 (런타임 자원). 전투 이벤트로만 증감한다.</summary>
        public float CurrentTouhon { get; set; } = 0f;

        /// <summary>이동 속도 외부 배율 (허주 등 디버프 축 — 상태가 덮어쓰는 Movement.MoveSpeedMultiplier와 별개). 기본 1.</summary>
        public float MoveSpeedExternalMultiplier { get; set; } = 1f;

        /// <summary>가하는 데미지 출력 배율 (허주 등 디버프 축). 기본 1.</summary>
        public float DamageOutputMultiplier { get; set; } = 1f;

        /// <summary>공격 애니메이션 재생 배속 외부 배율 (허주 등 디버프 축). 기본 1.</summary>
        public float AttackSpeedMultiplier { get; set; } = 1f;


        /// <summary>스탯이 변경되었을 때. UI/성장 시스템 연결용.</summary>
        public event Action StatsChanged;

        /// <summary>연결된 원본 Ability (조회용, 수정 금지).</summary>
        public CharacterAbility Ability { get; private set; }

        /// <summary>
        /// Ability 원본에서 Base 값을 런타임 사본으로 복사하고 CurrentHP를 최대로 채운다.
        /// ability가 null이면 코드 기본값을 유지한다(독립 실행 대비).
        /// </summary>
        public void Bind(CharacterAbility ability)
        {
            Ability = ability;
            if (ability != null)
            {
                _baseMaxHP = ability.MaxHP;
                _baseDefense = ability.Defense;
                _baseLifeCount = ability.LifeCount;

                _baseAttackPower = ability.AttackPower;
                _baseCriticalRate = ability.CriticalRate;

                _baseMaxFP = ability.MaxFP;
                _baseMaxTouhon = ability.MaxTouhon;

                _baseMoveSpeed = ability.MoveSpeed;
                _baseRunMultiplier = ability.RunMultiplier;
                _baseJumpHeight = ability.JumpHeight;
                _baseJumpCount = ability.MaxJumpCount;
                _baseDashSpeed = ability.DashSpeed;

                _baseHitDuration = ability.HitDuration;
                _baseLandDuration = ability.LandDuration;
                _baseGlobalAnimatorSpeed = ability.GlobalAnimatorSpeed;
                _baseWalkAnimationSpeed = ability.WalkAnimationSpeed;
            }

            CurrentHP = FinalMaxHP;
            CurrentFP = FinalMaxFP;
            CurrentTouhon = 0f;
            MoveSpeedExternalMultiplier = 1f;
            DamageOutputMultiplier = 1f;
            AttackSpeedMultiplier = 1f;
            RaiseChanged();
        }

        // ─────────── Final 값 (Base 런타임 사본 + Bonus) ───────────

        public float FinalMoveSpeed => Mathf.Max(0f, (_baseMoveSpeed + _moveSpeedBonus) * MoveSpeedExternalMultiplier);
        public float FinalRunMultiplier => Mathf.Max(0f, _baseRunMultiplier);
        public float FinalJumpHeight => Mathf.Max(0f, _baseJumpHeight + _jumpHeightBonus);
        public int FinalJumpCount => Mathf.Max(1, _baseJumpCount + _jumpCountBonus);
        public float FinalDashSpeed => Mathf.Max(0f, _baseDashSpeed + _dashSpeedBonus);
        public float FinalHitDuration => Mathf.Max(0f, _baseHitDuration);
        public float FinalLandDuration => Mathf.Max(0f, _baseLandDuration);
        public float FinalGlobalAnimatorSpeed => Mathf.Max(0.01f, _baseGlobalAnimatorSpeed);
        public float FinalWalkAnimationSpeed => Mathf.Max(0.01f, _baseWalkAnimationSpeed);

        public float FinalMaxHP => Mathf.Max(1f, _baseMaxHP);
        public float FinalAttackPower => Mathf.Max(0f, _baseAttackPower + _attackPowerBonus);
        public float FinalDefense => Mathf.Max(0f, _baseDefense);
        public float FinalCriticalRate => Mathf.Clamp01(_baseCriticalRate);
        public float FinalMaxFP => Mathf.Max(0f, _baseMaxFP);
        public float FinalMaxTouhon => Mathf.Max(0f, _baseMaxTouhon);
        public int FinalLifeCount => Mathf.Max(1, _baseLifeCount);

        // ─────────── 런타임 Base 조정 (디버그/성장 — SO는 건드리지 않음) ───────────

        public void SetBaseMoveSpeed(float value) { _baseMoveSpeed = value; RaiseChanged(); }
        public void SetBaseJumpHeight(float value) { _baseJumpHeight = value; RaiseChanged(); }
        public void SetBaseJumpCount(int value) { _baseJumpCount = Mathf.Max(1, value); RaiseChanged(); }
        public void SetBaseDashSpeed(float value) { _baseDashSpeed = value; RaiseChanged(); }

        public void SetMoveSpeedBonus(float value) { _moveSpeedBonus = value; RaiseChanged(); }
        public void SetJumpHeightBonus(float value) { _jumpHeightBonus = value; RaiseChanged(); }
        public void SetJumpCountBonus(int value) { _jumpCountBonus = value; RaiseChanged(); }
        public void SetDashSpeedBonus(float value) { _dashSpeedBonus = value; RaiseChanged(); }
        public void SetAttackPowerBonus(float value) { _attackPowerBonus = value; RaiseChanged(); }

        private void RaiseChanged() => StatsChanged?.Invoke();
    }
}
