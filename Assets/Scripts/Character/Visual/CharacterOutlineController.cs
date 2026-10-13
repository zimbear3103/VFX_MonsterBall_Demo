using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 상태에 따라 외곽선(Outline) 색을 갱신한다.
    /// 구 프로젝트 Aiara.CharacterOutlineController 이관 — 각 렌더러의 전용 아웃라인 머티리얼 슬롯에
    /// MaterialPropertyBlock으로 _Outline_Thickness / _Fade_Distance / _Outline_Color만 기록한다.
    /// 머티리얼 인스턴스 생성 없음(sharedMaterial 변경 없음).
    ///
    /// 구버전과 달리 슬롯을 프리팹에 미리 박지 않고, Awake에서 대상 SkinnedMeshRenderer의
    /// sharedMaterials 배열 끝에 OutlineMaterial(Unblockable.mat)을 런타임으로 덧붙인다.
    /// (배열 교체는 해당 렌더러 컴포넌트에만 적용되며 머티리얼 에셋은 건드리지 않는다.)
    ///
    /// 상태 → 색 매핑:
    ///  - 빨강: 손대포 홀드 차지(HandCannonSpin) 공격 중, 잠재능력(나찰) 발동 중
    ///  - 파랑: 강공격 홀드 차징 중 (PowerChargeProgress에 따라 투명→파랑 페이드인)
    ///    (※ 플레이어 프리팹은 BlueColor를 빨강으로 저작해 실제로는 빨강 — 2026-09-15 현재)
    ///  - 가드색(기본 파랑): 가드 자세(CharacterDefense.IsGuarding) 유지 중 (2026-09-15)
    /// 우선순위: 외부 오버라이드(키별) > 빨강 > 차징 > 가드 > 없음.
    /// 구버전의 단일 bool 오버라이드가 중첩 시 서로 꺼버리던 문제를 키 기반 스택으로 개선.
    /// </summary>
    [AddComponentMenu("Yeolha/Character/Character Outline Controller")]
    public class CharacterOutlineController : MonoBehaviour
    {
        [Serializable]
        public class OutlineMaterialEntry
        {
            [Tooltip("아웃라인 슬롯을 가진(또는 덧붙일) 렌더러")]
            public Renderer Renderer;
            [Tooltip("아웃라인 머티리얼 인덱스. -1이면 런타임에 OutlineMaterial을 배열 끝에 덧붙인다.")]
            public int MaterialIndex = -1;
        }

        [Header("Outline Material")]
        [Tooltip("아웃라인 전용 머티리얼(Unblockable.mat). Shader Graphs/Outline ShaderGraph 사용, Thickness 0으로 저작돼 있어야 한다.")]
        public Material OutlineMaterial;
        [Tooltip("true면 자식의 모든 SkinnedMeshRenderer를 자동 수집해 아웃라인 슬롯을 덧붙인다.")]
        public bool AutoCollectRenderers = true;
        [Tooltip("AutoCollectRenderers가 false일 때 사용할 대상 목록")]
        public List<OutlineMaterialEntry> OutlineEntries;

        [Header("Outline Values")]
        [Tooltip("아웃라인 활성 시 두께 (구버전 프리팹 값 0.002)")]
        public float ActiveThickness = 0.002f;
        [Tooltip("아웃라인 비활성 시 두께")]
        public float InactiveThickness = 0f;
        [Tooltip("아웃라인 활성 시 페이드 거리 (구버전 값 20)")]
        public float ActiveFadeDistance = 20f;
        [Tooltip("아웃라인 비활성 시 페이드 거리")]
        public float InactiveFadeDistance = 0f;

        [Header("State Colors")]
        [Tooltip("강공격 홀드 차징 아웃라인 색 (구버전 BlueColor)")]
        public Color BlueColor = new Color(0.1f, 0.3f, 0.8f, 1f);
        [Tooltip("손대포 차지/잠재능력 아웃라인 색 (구버전 RedColor)")]
        public Color RedColor = new Color(0.8f, 0.1f, 0.1f, 1f);
        [Tooltip("가드 자세 유지 중 아웃라인 색 (2026-09-15)")]
        public Color GuardColor = new Color(0.15f, 0.45f, 1f, 1f);

        [Header("State Sources")]
        [Tooltip("이 이름의 공격이 실행 중이면 빨간 아웃라인 (손대포 홀드 차지)")]
        public string HandCannonSpinAttackName = "HandCannonSpin";
        [Tooltip("잠재능력(나찰) 발동 중 빨간 아웃라인 표시 여부")]
        public bool RedOutlineOnLatentAbility = true;
        [FormerlySerializedAs("BlueOutlineOnSuperArmor")]
        [Tooltip("강공격 홀드 차징 중 파란 아웃라인 표시 여부")]
        public bool BlueOutlineOnPowerCharge = true;
        [Tooltip("가드 자세(CharacterDefense.IsGuarding) 중 GuardColor 아웃라인 표시 여부")]
        public bool OutlineOnGuard = true;

        private static readonly int _outlineThicknessID = Shader.PropertyToID("_Outline_Thickness");
        private static readonly int _fadeDistanceID = Shader.PropertyToID("_Fade_Distance");
        private static readonly int _outlineColorID = Shader.PropertyToID("_Outline_Color");

        private struct Slot
        {
            public Renderer Renderer;
            public int Index;
        }

        private struct OverrideEntry
        {
            public int Priority;
            public Color Color;
        }

        private Character _character;
        private CharacterLatentAbility _latentAbility;
        private MaterialPropertyBlock _propBlock;
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<object, OverrideEntry> _overrides = new Dictionary<object, OverrideEntry>();
        private bool _outlineActive;
        private Color _currentColor;
        private float _currentBlend = 1f;
        private bool _initialized;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            if (_initialized) SetOutlineInactive();
        }

        private void OnDisable()
        {
            _overrides.Clear();
            if (_initialized) SetOutlineInactive();
        }

        /// <summary>슬롯 수집/추가와 캐시 초기화(멱등).</summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _character = GetComponent<Character>();
            _latentAbility = GetComponentInChildren<CharacterLatentAbility>(true);
            EnsurePropBlock();
            _slots.Clear();

            if (AutoCollectRenderers)
            {
                foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    RegisterRenderer(smr, -1);
            }
            else if (OutlineEntries != null)
            {
                for (int i = 0; i < OutlineEntries.Count; i++)
                {
                    OutlineMaterialEntry entry = OutlineEntries[i];
                    if (entry != null && entry.Renderer != null)
                        RegisterRenderer(entry.Renderer, entry.MaterialIndex);
                }
            }

            SetOutlineInactive();
        }

        private void RegisterRenderer(Renderer renderer, int materialIndex)
        {
            if (renderer == null) return;

            Material[] mats = renderer.sharedMaterials;
            int index = materialIndex;

            if (index < 0)
            {
                // 이미 아웃라인 머티리얼 슬롯이 있으면 그대로 사용
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && (mats[i] == OutlineMaterial ||
                        (mats[i].shader != null && mats[i].shader.name.Contains("Outline"))))
                    {
                        index = i;
                        break;
                    }
                }

                // 없으면 배열 끝에 덧붙인다
                if (index < 0)
                {
                    if (OutlineMaterial == null) return;
                    Material[] extended = new Material[mats.Length + 1];
                    for (int i = 0; i < mats.Length; i++) extended[i] = mats[i];
                    extended[mats.Length] = OutlineMaterial;
                    renderer.sharedMaterials = extended;
                    index = mats.Length;
                }
            }
            else if (index >= mats.Length)
            {
                return;
            }

            _slots.Add(new Slot { Renderer = renderer, Index = index });
        }

        private void EnsurePropBlock()
        {
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (!_initialized) return;

            bool active = false;
            Color color = default;
            float blend = 1f;

            // 1) 외부 오버라이드 (최고 우선순위 항목)
            if (_overrides.Count > 0)
            {
                int best = int.MinValue;
                foreach (KeyValuePair<object, OverrideEntry> kv in _overrides)
                {
                    if (kv.Value.Priority >= best)
                    {
                        best = kv.Value.Priority;
                        color = kv.Value.Color;
                    }
                }
                active = true;
            }
            else if (_character != null && !_character.IsDead)
            {
                CharacterCombat combat = _character.Combat;

                // 2) 빨강: 손대포 홀드 차지(회전 베기) 실행 중
                if (combat != null && combat.IsAttacking &&
                    !string.IsNullOrEmpty(HandCannonSpinAttackName) &&
                    combat.CurrentAttackName == HandCannonSpinAttackName)
                {
                    active = true;
                    color = RedColor;
                }
                // 3) 빨강: 잠재능력(나찰) 발동 중 (구버전 LatentAbilityOutlineColor)
                else if (RedOutlineOnLatentAbility && _latentAbility != null && _latentAbility.IsActive)
                {
                    active = true;
                    color = RedColor;
                }
                // 4) 파랑: 강공격 홀드 차징 중 — 진행도(PowerChargeProgress)에 따라 투명→파랑 페이드인
                else if (BlueOutlineOnPowerCharge && combat != null && combat.IsPowerCharging)
                {
                    active = true;
                    color = BlueColor;
                    blend = combat.PowerChargeProgress;
                }
                // 5) 가드색: 가드 자세 유지 중 (2026-09-15)
                else if (OutlineOnGuard && _character.Defense != null && _character.Defense.IsGuarding)
                {
                    active = true;
                    color = GuardColor;
                }
            }

            if (active)
            {
                if (!_outlineActive || _currentColor != color || _currentBlend != blend)
                    SetOutlineActive(color, blend);
            }
            else if (_outlineActive)
            {
                SetOutlineInactive();
            }
        }

        /// <summary>
        /// 상태 폴링보다 우선하는 아웃라인 색 오버라이드를 등록한다.
        /// 같은 key로 다시 호출하면 갱신. priority가 높을수록 우선.
        /// (허주/연출 등 외부 시스템 확장점 — 구버전 SetOutlineColor 대체)
        /// </summary>
        public void SetOutlineOverride(object key, Color color, int priority = 0)
        {
            if (key == null) return;
            _overrides[key] = new OverrideEntry { Priority = priority, Color = color };
        }

        /// <summary>key로 등록된 오버라이드를 해제한다. 다른 key의 오버라이드는 유지된다.</summary>
        public void ClearOutlineOverride(object key)
        {
            if (key == null) return;
            _overrides.Remove(key);
        }

        private void SetOutlineActive(Color color, float blend = 1f)
        {
            _outlineActive = true;
            _currentColor = color;
            _currentBlend = Mathf.Clamp01(blend);
            float thickness = Mathf.Lerp(InactiveThickness, ActiveThickness, _currentBlend);
            ApplyToSlots(thickness, ActiveFadeDistance, color, true);
        }

        private void SetOutlineInactive()
        {
            _outlineActive = false;
            ApplyToSlots(InactiveThickness, InactiveFadeDistance, default, false);
        }

        private void ApplyToSlots(float thickness, float fadeDistance, Color color, bool setColor)
        {
            EnsurePropBlock();
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Renderer == null) continue;

                slot.Renderer.GetPropertyBlock(_propBlock, slot.Index);
                _propBlock.SetFloat(_outlineThicknessID, thickness);
                _propBlock.SetFloat(_fadeDistanceID, fadeDistance);
                if (setColor) _propBlock.SetColor(_outlineColorID, color);
                slot.Renderer.SetPropertyBlock(_propBlock, slot.Index);
            }
        }
    }
}
