using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 피격 반응 연출 — 이미션 색 플래시 + 모델 흔들림.
    /// 구 프로젝트 GuardableHealth 히트 연출 이관(단, _BaseColor 대신 _EmissionColor 사용).
    ///
    ///  1) 이미션 플래시: 피격 시 각 렌더러의 _EmissionColor를 지정 색으로 바꿔 HoldDuration 동안 유지,
    ///     이후 FadeDuration 동안 원래 색으로 되돌린다. MaterialPropertyBlock만 사용(머티리얼 애셋 불변).
    ///  2) 모델 흔들림: 지정한 시각 루트(비우면 Animator 트랜스폼)의 localPosition을 ShakeDuration 동안
    ///     ShakeMagnitude 크기로 감쇠 흔들고 원위치로 복귀. LateUpdate에서 적용해 애니메이션을 덮는다.
    ///
    /// CharacterCombat.HitLanded 이벤트를 구독해 피격마다 발동한다.
    /// 단, 슈퍼아머 중 피격(CharacterSuperArmorReaction.IsSuperArmorActive)은 건너뛴다 — 그쪽이 전용 연출을 재생.
    ///  3) 차징 틴트(2026-09-15): 강공격 홀드 차징(CharacterCombat.IsPowerCharging) 중에는 피격 플래시와 같은 방식으로
    ///     몸 이미션을 PowerChargeEmissionColor로 물들인다(진행도 PowerChargeProgress에 따라 페이드인, 릴리즈 시 복구).
    ///     피격 플래시가 겹치면 플래시가 우선하고, 플래시가 끝나면 차징 틴트로 되돌아간다.
    /// (Character/Combat/State/AI 구조 변경 없이 기존 이벤트만 쓰는 순수 시각 컴포넌트.)
    ///
    /// [중요] 복구는 반드시 SetPropertyBlock(null, index)로 블록을 '완전히 제거'한다. 값만 되돌리고 블록을
    /// 남겨두면, 블록이 붙어있는 동안 SRP Batcher가 깨진 채로 rim 등 다른 프로퍼티가 머티리얼 값이 아니라
    /// 셰이더 기본값으로 렌더돼 플래시가 끝난 뒤에도 rim이 사라져 보인다. (빈 블록·비인덱스 null로는 제거 안 됨)
    /// </summary>
    [AddComponentMenu("Yeolha/Character/Character Hit Reaction")]
    public class CharacterHitReaction : MonoBehaviour
    {
        [Header("Emission Flash")]
        [Tooltip("피격 시 이미션 플래시 사용 여부")]
        public bool UseEmissionFlash = true;

        [ColorUsage(true, true)]
        [Tooltip("피격 시 바꿀 이미션 색 (HDR). 강도를 올리면 더 밝게 빛난다.")]
        public Color FlashEmissionColor = new Color(3f, 0f, 0f, 1f);

        [Min(0f)]
        [Tooltip("플래시 색을 유지하는 시간(초) — 몇 초간 빨간색일지")]
        public float HoldDuration = 0.05f;

        [Min(0f)]
        [Tooltip("원래 이미션 색으로 되돌아가는 시간(초) — 몇 초에 걸쳐 러프하게 복귀")]
        public float FadeDuration = 0.25f;

        [Header("Power Charge Tint")]
        [Tooltip("강공격 홀드 차징(HeavyHoldSkill 등) 중 몸을 피격 때처럼 물들인다 (2026-09-15)")]
        public bool UsePowerChargeTint = true;

        [ColorUsage(true, true)]
        [Tooltip("차징 중 이미션 색 (HDR). 기본은 피격 플래시와 같은 빨강")]
        public Color PowerChargeEmissionColor = new Color(3f, 0f, 0f, 1f);

        [Tooltip("true면 차징 진행도(PowerChargeProgress 0→1)에 따라 원래 색→차징 색으로 페이드인. false면 차징 시작 즉시 풀 색")]
        public bool PowerChargeTintFollowsProgress = true;

        [Header("Model Shake")]
        [Tooltip("피격 시 모델 흔들림 사용 여부")]
        public bool UseShake = true;

        [Min(0f)]
        [Tooltip("흔드는 시간(초) — 몇 초간 떨릴지")]
        public float ShakeDuration = 0.15f;

        [Min(0f)]
        [Tooltip("흔드는 세기(미터) — 얼마나 떨릴지. 시간에 따라 0으로 감쇠")]
        public float ShakeMagnitude = 0.05f;

        [Tooltip("true면 흔들림 시간이 타임스케일(히트스탑·슬로우모션)에 영향받지 않는다(unscaledDeltaTime)")]
        public bool ShakeUnscaledTime = true;

        [Tooltip("흔들 대상 트랜스폼(시각 루트). 비우면 Animator가 붙은 자식을 자동 사용")]
        public Transform ShakeTarget;

        [Header("Targets")]
        [Tooltip("true면 자식의 모든 SkinnedMeshRenderer를 자동 수집한다.")]
        public bool AutoCollectRenderers = true;

        [Tooltip("AutoCollectRenderers가 false일 때 사용할 렌더러 목록")]
        public List<Renderer> ManualRenderers;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        // YSA Toon(Shader Graph)의 Emission 토글(Boolean 프로퍼티 → float). 머티리얼에서 꺼져 있으면(_Emission=0)
        // _EmissionColor를 바꿔도 출력이 0이라 플래시가 안 보인다 → 플래시 동안 블록에서 1로 켠다 (2026-09-09).
        private static readonly int EmissionToggleId = Shader.PropertyToID("_Emission");

        private struct Slot
        {
            public Renderer Renderer;
            public int Index;
            public Color OriginalEmission;
            public bool HasEmissionToggle;
            public float OriginalEmissionToggle;
        }

        private Character _character;
        private CharacterSuperArmorReaction _superArmor;
        private MaterialPropertyBlock _propBlock;
        private readonly List<Slot> _slots = new List<Slot>();
        private Coroutine _emissionCoroutine;
        private bool _subscribed;
        private bool _initialized;

        // power charge tint state (Update polling)
        private bool _chargeTintApplied;
        private float _chargeTintBlend = -1f;

        // shake state (LateUpdate driven)
        private Vector3 _shakeOrigin;
        private float _shakeTimeLeft;
        private bool _shaking;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (_emissionCoroutine != null)
            {
                StopCoroutine(_emissionCoroutine);
                _emissionCoroutine = null;
            }
            RestoreEmission();
            _chargeTintApplied = false;
            _chargeTintBlend = -1f;
            if (_shaking && ShakeTarget != null) ShakeTarget.localPosition = _shakeOrigin;
            _shaking = false;
        }

        // ─────────── 차징 틴트 (2026-09-15) ───────────

        private void Update()
        {
            if (!_initialized || _slots.Count == 0) return;

            bool wantTint = UsePowerChargeTint && _character != null && !_character.IsDead
                && _character.Combat != null && _character.Combat.IsPowerCharging;

            if (!wantTint)
            {
                if (_chargeTintApplied)
                {
                    _chargeTintApplied = false;
                    _chargeTintBlend = -1f;
                    if (_emissionCoroutine == null) RestoreEmission(); // 플래시 진행 중이면 플래시가 끝날 때 복구
                }
                return;
            }

            float blend = PowerChargeTintFollowsProgress ? Mathf.Clamp01(_character.Combat.PowerChargeProgress) : 1f;
            _chargeTintApplied = true;
            if (_emissionCoroutine != null) return; // 피격 플래시 우선 — 끝나면 다음 Update에서 틴트 재적용
            // 매 프레임 재적용: 가드/슈퍼아머 반응 컴포넌트가 같은 슬롯의 블록을 지워도(SetPropertyBlock null) 다음 프레임에 복구된다.
            _chargeTintBlend = blend;
            ApplyChargeTint(blend);
        }

        private void ApplyChargeTint(float blend)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Renderer == null) continue;
                Color c = Color.Lerp(slot.OriginalEmission, PowerChargeEmissionColor, blend);
                slot.Renderer.GetPropertyBlock(_propBlock, slot.Index);
                _propBlock.SetColor(EmissionColorId, c);
                if (slot.HasEmissionToggle) _propBlock.SetFloat(EmissionToggleId, 1f);
                slot.Renderer.SetPropertyBlock(_propBlock, slot.Index);
            }
        }

        /// <summary>슬롯 수집·흔들 대상 확보와 캐시 초기화(멱등).</summary>
        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _character = GetComponent<Character>();
            _superArmor = GetComponent<CharacterSuperArmorReaction>();
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

            if (ShakeTarget == null)
            {
                var anim = GetComponentInChildren<Animator>(true);
                if (anim != null) ShakeTarget = anim.transform;
            }

            CollectSlots();
        }

        private void CollectSlots()
        {
            _slots.Clear();

            if (AutoCollectRenderers)
            {
                foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    RegisterRenderer(smr);
            }
            else if (ManualRenderers != null)
            {
                for (int i = 0; i < ManualRenderers.Count; i++)
                    RegisterRenderer(ManualRenderers[i]);
            }
        }

        /// <summary>_EmissionColor를 가진 머티리얼 슬롯만 등록하고 원래 색을 캐시한다(아웃라인 슬롯 제외).</summary>
        private void RegisterRenderer(Renderer renderer)
        {
            if (renderer == null) return;
            Material[] mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;
                if (m.shader != null && m.shader.name.Contains("Outline")) continue;
                if (!m.HasProperty(EmissionColorId)) continue;
                _slots.Add(new Slot
                {
                    Renderer = renderer,
                    Index = i,
                    OriginalEmission = m.GetColor(EmissionColorId),
                    HasEmissionToggle = m.HasProperty(EmissionToggleId),
                    OriginalEmissionToggle = m.HasProperty(EmissionToggleId) ? m.GetFloat(EmissionToggleId) : 0f,
                });
            }
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            if (_character == null) _character = GetComponent<Character>();
            if (_character == null || _character.Combat == null) return;
            _character.Combat.HitLanded += OnHitReceived;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_character != null && _character.Combat != null)
                _character.Combat.HitLanded -= OnHitReceived;
            _subscribed = false;
        }

        private void OnHitReceived(DamageInfo damage)
        {
            if (!isActiveAndEnabled) return;
            // 슈퍼아머 중 피격은 CharacterSuperArmorReaction이 전용 연출을 담당 — 일반 히트 연출은 건너뛴다.
            if (_superArmor != null && _superArmor.IsSuperArmorActive) return;

            if (UseEmissionFlash && _slots.Count > 0)
            {
                if (_emissionCoroutine != null) StopCoroutine(_emissionCoroutine);
                _emissionCoroutine = StartCoroutine(EmissionRoutine());
            }

            if (UseShake && ShakeTarget != null && ShakeMagnitude > 0f && ShakeDuration > 0f)
            {
                if (!_shaking) { _shakeOrigin = ShakeTarget.localPosition; _shaking = true; }
                _shakeTimeLeft = ShakeDuration; // 재피격 시 시간 갱신
            }
        }

        private IEnumerator EmissionRoutine()
        {
            ApplyEmission(FlashEmissionColor);

            float hold = Mathf.Max(0f, HoldDuration);
            float ht = 0f;
            while (ht < hold)
            {
                ht += Time.deltaTime;
                yield return null;
            }

            float fade = Mathf.Max(0f, FadeDuration);
            float t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                float k = fade > 0f ? Mathf.Clamp01(t / fade) : 1f;
                for (int i = 0; i < _slots.Count; i++)
                {
                    Slot slot = _slots[i];
                    if (slot.Renderer == null) continue;
                    Color c = Color.Lerp(FlashEmissionColor, slot.OriginalEmission, k);
                    slot.Renderer.GetPropertyBlock(_propBlock, slot.Index);
                    _propBlock.SetColor(EmissionColorId, c);
                    if (slot.HasEmissionToggle) _propBlock.SetFloat(EmissionToggleId, 1f);
                    slot.Renderer.SetPropertyBlock(_propBlock, slot.Index);
                }
                yield return null;
            }

            RestoreEmission();
            _emissionCoroutine = null;
            // 플래시 종료 시 차징 중이면 틴트로 복귀 (다음 Update에서 진행도 기준으로 재적용)
            if (_chargeTintApplied) { _chargeTintBlend = -1f; }
        }

        private void ApplyEmission(Color c)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Renderer == null) continue;
                slot.Renderer.GetPropertyBlock(_propBlock, slot.Index);
                _propBlock.SetColor(EmissionColorId, c);
                    if (slot.HasEmissionToggle) _propBlock.SetFloat(EmissionToggleId, 1f);
                slot.Renderer.SetPropertyBlock(_propBlock, slot.Index);
            }
        }

        /// <summary>
        /// 블록을 렌더러에서 완전히 제거해 원상복구한다. SetPropertyBlock(null, index)만이 실제로 제거되며,
        /// 이로써 SRP Batcher가 다시 붙어 rim 등 다른 프로퍼티가 머티리얼 값으로 렌더된다.
        /// (값을 원색으로 되돌리고 블록을 남기거나, 빈 블록/비인덱스 null을 쓰면 rim이 계속 사라져 보인다.)
        /// </summary>
        private void RestoreEmission()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Renderer == null) continue;
                slot.Renderer.SetPropertyBlock(null, slot.Index);
            }
        }

        // 애니메이션 이후에 흔들림을 적용해 로컬 포즈를 덮는다.
        private void LateUpdate()
        {
            if (!_shaking || ShakeTarget == null) return;

            _shakeTimeLeft -= ShakeUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (_shakeTimeLeft <= 0f)
            {
                ShakeTarget.localPosition = _shakeOrigin;
                _shaking = false;
                return;
            }

            float amp = ShakeMagnitude * (_shakeTimeLeft / Mathf.Max(0.0001f, ShakeDuration)); // 시간에 따라 감쇠
            Vector3 offset = Random.insideUnitSphere * amp;
            ShakeTarget.localPosition = _shakeOrigin + offset;
        }
    }
}
