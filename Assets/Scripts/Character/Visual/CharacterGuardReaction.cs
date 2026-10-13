using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 가드/패링 성공 연출 — 이미션 색 플래시(흰 스파크) + 약한 모델 흔들림.
    /// CharacterHitReaction과 동일한 기계(이미션 MPB 플래시 + LateUpdate 흔들림)를 쓰되,
    /// 피격이 아니라 방어 성공에 반응한다.
    ///
    /// 훅: CharacterDefense.GuardSucceeded(가드 성립) + ParrySucceeded(패링 성립, bool=퍼펙트).
    /// 가드/패링은 같은 연출(흰 스파크)을 쓴다 — 이벤트만 다르고 표현은 공용.
    /// 빨강 피격 연출(CharacterHitReaction)은 HitLanded(방어되지 않은 실제 피격)만 구독하므로 서로 겹치지 않는다.
    /// (Character/Combat/Defense 구조 변경 없이 기존/신규 이벤트만 쓰는 순수 시각 컴포넌트.)
    /// 카운터 버프(퍼펙트 패링 보상, 2026-09-15): CharacterDefense.HasCounterBuff가 true인 동안 플래시가 꺼지지 않고
    /// 온몸 발광을 유지하며, 버프가 끝나면(공격으로 소모/피격 상실/만료) CounterBuffFadeDuration으로 복귀한다.
    /// </summary>
    [AddComponentMenu("Yeolha/Character/Character Guard Reaction")]
    public class CharacterGuardReaction : MonoBehaviour
    {
        [Header("Emission Flash")]
        [Tooltip("가드/패링 시 이미션 플래시 사용 여부")]
        public bool UseEmissionFlash = true;

        [ColorUsage(true, true)]
        [Tooltip("가드/패링 시 바꿀 이미션 색 (HDR, 기본 흰 스파크)")]
        public Color FlashEmissionColor = new Color(2f, 2f, 2f, 1f);

        [Min(0f)]
        [Tooltip("플래시 색을 유지하는 시간(초)")]
        public float HoldDuration = 0.03f;

        [Min(0f)]
        [Tooltip("원래 이미션 색으로 되돌아가는 시간(초)")]
        public float FadeDuration = 0.15f;

        [Header("Counter Buff Hold")]
        [Tooltip("카운터 버프(퍼펙트 패링 보상, CharacterDefense.HasCounterBuff) 보유 중 온몸 발광을 유지한다. 버프 소모/상실/만료 시 아래 Fade로 복귀")]
        public bool UseCounterBuffHold = true;

        [ColorUsage(true, true)]
        [Tooltip("카운터 버프 유지 중 이미션 색 (HDR). 기본은 패링 플래시와 같은 흰색")]
        public Color CounterBuffEmissionColor = new Color(2f, 2f, 2f, 1f);

        [Min(0f)]
        [Tooltip("버프 종료 후 원래 이미션 색으로 되돌아가는 시간(초)")]
        public float CounterBuffFadeDuration = 0.15f;

        [Header("Model Shake")]
        [Tooltip("가드/패링 시 모델 흔들림 사용 여부")]
        public bool UseShake = true;

        [Min(0f)]
        [Tooltip("흔드는 시간(초) — 가드/패링은 약하게")]
        public float ShakeDuration = 0.1f;

        [Min(0f)]
        [Tooltip("흔드는 세기(미터) — 막은 느낌이라 작게")]
        public float ShakeMagnitude = 0.02f;

        [Tooltip("true면 흔들림 시간이 타임스케일(패링 히트스탑·슬로우모션)에 영향받지 않는다(unscaledDeltaTime)")]
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
        private MaterialPropertyBlock _propBlock;
        private readonly List<Slot> _slots = new List<Slot>();
        private Coroutine _emissionCoroutine;
        private bool _subscribed;
        private bool _initialized;

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
            var def = _character != null ? _character.Defense : null;
            if (def == null) return;
            def.GuardSucceeded += OnGuardSucceeded;
            def.ParrySucceeded += OnParrySucceeded;
            def.CounterBuffChanged += OnCounterBuffChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            var def = _character != null ? _character.Defense : null;
            if (def != null)
            {
                def.GuardSucceeded -= OnGuardSucceeded;
                def.ParrySucceeded -= OnParrySucceeded;
                def.CounterBuffChanged -= OnCounterBuffChanged;
            }
            _subscribed = false;
        }

        // 가드/패링은 같은 흰 스파크 연출을 공유한다(이벤트만 분리).
        private void OnGuardSucceeded() => PlayReaction();
        private void OnParrySucceeded(bool perfect) => PlayReaction();

        /// <summary>카운터 버프 부여 시 발광 루틴이 없으면 시작(플래시 루틴이 이미 돌고 있으면 그 루틴이 유지 구간으로 이어진다). 종료는 루틴이 HasCounterBuff 폴링으로 감지.</summary>
        private void OnCounterBuffChanged(bool active)
        {
            if (!active || !isActiveAndEnabled || !UseCounterBuffHold || !UseEmissionFlash || _slots.Count == 0) return;
            if (_emissionCoroutine == null) _emissionCoroutine = StartCoroutine(EmissionRoutine());
        }

        private bool CounterBuffHeld => UseCounterBuffHold && _character != null && _character.Defense != null && _character.Defense.HasCounterBuff;

        private void PlayReaction()
        {
            if (!isActiveAndEnabled) return;

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
            if (hold > 0f) yield return new WaitForSeconds(hold);

            // 카운터 버프(퍼펙트 패링) 보유 중: 플래시가 꺼지지 않고 온몸 발광을 유지한다 — 버프 소모(공격)/상실(피격)/만료까지.
            Color from = FlashEmissionColor;
            float fade = Mathf.Max(0f, FadeDuration);
            if (CounterBuffHeld)
            {
                from = CounterBuffEmissionColor;
                fade = Mathf.Max(0f, CounterBuffFadeDuration);
                while (CounterBuffHeld)
                {
                    ApplyEmission(CounterBuffEmissionColor); // 매 프레임 재적용 — 다른 MPB 라이터(피격 플래시 등)가 덮어도 유지
                    yield return null;
                }
            }

            float t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                float k = fade > 0f ? Mathf.Clamp01(t / fade) : 1f;
                for (int i = 0; i < _slots.Count; i++)
                {
                    Slot slot = _slots[i];
                    if (slot.Renderer == null) continue;
                    Color c = Color.Lerp(from, slot.OriginalEmission, k);
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

        private void RestoreEmission()
        {
            if (_propBlock == null) return;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Renderer == null) continue;
                slot.Renderer.GetPropertyBlock(_propBlock, slot.Index);
                _propBlock.SetColor(EmissionColorId, slot.OriginalEmission);
                if (slot.HasEmissionToggle) _propBlock.SetFloat(EmissionToggleId, slot.OriginalEmissionToggle);
                slot.Renderer.SetPropertyBlock(_propBlock, slot.Index);
            }
        }

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

            float amp = ShakeMagnitude * (_shakeTimeLeft / Mathf.Max(0.0001f, ShakeDuration));
            Vector3 offset = Random.insideUnitSphere * amp;
            ShakeTarget.localPosition = _shakeOrigin + offset;
        }
    }
}
