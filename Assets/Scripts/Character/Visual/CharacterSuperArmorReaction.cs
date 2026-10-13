using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 슈퍼아머 피격 연출 — 슈퍼아머 중에 맞았을 때 일반 히트 리액션(빨강) 대신 재생되는 전용 플래시 + 흔들림.
    /// (이전의 "슈퍼아머 동안 상시 발광"은 제거. 슈퍼아머는 맞는 순간에만 드러난다.)
    ///
    /// 훅: CharacterCombat.HitLanded. 이벤트 시점에 슈퍼아머가 활성이면 이 컴포넌트가 반응하고,
    /// CharacterHitReaction은 같은 조건에서 자기 연출을 건너뛴다(중복 방지).
    /// 슈퍼아머 상태는 CharacterCombat의 public 게터를 읽기만 한다:
    ///  - AttackSuperArmorActive : 현재 공격(AttackAction.SuperArmor) 기반 슈퍼아머
    ///  - SuperArmorActive       : 잠재능력(나찰) 등 외부 슈퍼아머 (IncludeLatentSuperArmor로 포함 여부 선택)
    /// (Character/Combat/State/AI 구조 변경 없이 기존 이벤트/상태만 쓰는 순수 시각 컴포넌트.)
    ///
    /// [중요] 복구는 SetPropertyBlock(null, index)로 블록을 완전히 제거한다(CharacterHitReaction과 동일 이유 —
    /// 블록이 남으면 SRP Batcher가 깨져 rim 등 다른 프로퍼티가 머티리얼 값이 아니라 셰이더 기본값으로 렌더된다).
    /// </summary>
    [AddComponentMenu("Yeolha/Character/Character Super Armor Reaction")]
    public class CharacterSuperArmorReaction : MonoBehaviour
    {
        [Header("Emission Flash")]
        [Tooltip("슈퍼아머 피격 시 이미션 플래시 사용 여부")]
        public bool UseEmissionFlash = true;

        [ColorUsage(true, true)]
        [Tooltip("슈퍼아머 피격 시 바꿀 이미션 색 (HDR). 강도를 올리면 더 밝게 빛난다.")]
        public Color FlashEmissionColor = new Color(0.2f, 0.8f, 3f, 1f);

        [Min(0f)]
        [Tooltip("플래시 색을 유지하는 시간(초)")]
        public float HoldDuration = 0.06f;

        [Min(0f)]
        [Tooltip("원래 이미션 색으로 되돌아가는 시간(초)")]
        public float FadeDuration = 0.2f;

        [Header("Model Shake")]
        [Tooltip("슈퍼아머 피격 시 모델 흔들림 사용 여부 (경직 없이 버티는 느낌 — 약하게 권장)")]
        public bool UseShake = true;

        [Min(0f)]
        [Tooltip("흔드는 시간(초)")]
        public float ShakeDuration = 0.1f;

        [Min(0f)]
        [Tooltip("흔드는 세기(미터). 시간에 따라 0으로 감쇠")]
        public float ShakeMagnitude = 0.02f;

        [Tooltip("true면 흔들림/플래시 시간이 타임스케일(히트스탑·슬로우모션)에 영향받지 않는다")]
        public bool UseUnscaledTime = true;

        [Tooltip("흔들 대상 트랜스폼(시각 루트). 비우면 Animator가 붙은 자식을 자동 사용")]
        public Transform ShakeTarget;

        [Header("Super Armor Source")]
        [Tooltip("잠재능력(나찰) 등 외부 슈퍼아머(SuperArmorActive)도 포함할지 여부. 끄면 공격 슈퍼아머만 반응.")]
        public bool IncludeLatentSuperArmor = false;

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
        private MaterialPropertyBlock _propBlock;
        private readonly List<Slot> _slots = new List<Slot>();
        private Coroutine _emissionCoroutine;
        private bool _subscribed;
        private bool _initialized;

        private Vector3 _shakeOrigin;
        private float _shakeTimeLeft;
        private bool _shaking;

        private float DeltaTime => UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

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
            if (_shaking && ShakeTarget != null) ShakeTarget.localPosition = _shakeOrigin;
            _shaking = false;
        }

        /// <summary>슬롯 수집·흔들 대상 확보와 캐시 초기화(멱등).</summary>
        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _character = GetComponent<Character>();
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

        /// <summary>
        /// 지금 슈퍼아머가 활성인가(이 컴포넌트가 피격 연출을 담당하는 조건).
        /// CharacterHitReaction이 같은 HitLanded에서 자기 연출을 건너뛸지 판단할 때 읽는다.
        /// </summary>
        public bool IsSuperArmorActive
        {
            get
            {
                if (!isActiveAndEnabled) return false;
                if (_character == null)
                {
                    _character = GetComponent<Character>();
                    if (_character == null) return false;
                }
                CharacterCombat combat = _character.Combat;
                if (combat == null) return false;
                if (combat.AttackSuperArmorActive) return true;
                if (IncludeLatentSuperArmor && combat.SuperArmorActive) return true;
                return false;
            }
        }

        private void OnHitReceived(DamageInfo damage)
        {
            if (!isActiveAndEnabled) return;
            if (!IsSuperArmorActive) return;
            if (_character != null && _character.IsDead) return;

            if (UseEmissionFlash && _slots.Count > 0)
            {
                if (_emissionCoroutine != null) StopCoroutine(_emissionCoroutine);
                _emissionCoroutine = StartCoroutine(EmissionRoutine());
            }

            if (UseShake && ShakeTarget != null && ShakeMagnitude > 0f && ShakeDuration > 0f)
            {
                if (!_shaking) { _shakeOrigin = ShakeTarget.localPosition; _shaking = true; }
                _shakeTimeLeft = ShakeDuration;
            }
        }

        private IEnumerator EmissionRoutine()
        {
            ApplyEmission(FlashEmissionColor);

            float hold = Mathf.Max(0f, HoldDuration);
            float ht = 0f;
            while (ht < hold)
            {
                ht += DeltaTime;
                yield return null;
            }

            float fade = Mathf.Max(0f, FadeDuration);
            float t = 0f;
            while (t < fade)
            {
                t += DeltaTime;
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

        private void LateUpdate()
        {
            if (!_shaking || ShakeTarget == null) return;

            _shakeTimeLeft -= DeltaTime;
            if (_shakeTimeLeft <= 0f)
            {
                ShakeTarget.localPosition = _shakeOrigin;
                _shaking = false;
                return;
            }

            float amp = ShakeMagnitude * (_shakeTimeLeft / Mathf.Max(0.0001f, ShakeDuration));
            Vector3 offset = Random.insideUnitSphere * amp;
            ShakeTarget.localPosition = _shakeOrigin + offset;
        }
    }
}
