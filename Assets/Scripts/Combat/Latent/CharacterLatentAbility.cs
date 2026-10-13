using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 잠재능력 발동 컨트롤러 (구버전 LatentAbilityManager + 발동 트리거 이식).
    ///
    /// 설계:
    ///   - 슬롯(LatentAbility + 발동 확률) 목록을 들고, 트리거 입력 + 조건(투혼 MAX) 충족 시 각 슬롯을 확률로 발동.
    ///   - 발동은 소유 Character에만 효과를 건다(특정 공격 스크립트 직접 수정 없음).
    ///   - 트리거는 구버전 그대로 스틱 입력(LS+RS 동시) — 투혼이 가득 찼을 때만.
    ///   - 지속시간 경과 후 자동 해제 + 투혼 리셋값으로 복구.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    [RequireComponent(typeof(Character))]
    public class CharacterLatentAbility : MonoBehaviour
    {
        [System.Serializable]
        public class Slot
        {
            [Tooltip("발동할 잠재능력 데이터(SO). 예: Latent_Nachal")]
            public LatentAbility ability;
            [Tooltip("발동 확률 (0~1). 구버전 나찰=1(항상)")]
            [Range(0f, 1f)] public float activationChance = 1f;
        }

        [Header("Slots")]
        [Tooltip("보유 잠재능력 슬롯 (구버전 LatentAbilityManager.Slots). 플레이어만 채운다")]
        [SerializeField] private List<Slot> slots = new List<Slot>();

        [Header("Rules")]
        [Tooltip("발동 지속 시간(초) (구버전 LatentAbilityDuration 10)")]
        [SerializeField] private float duration = 10f;
        [Tooltip("발동에 투혼 MAX가 필요한지 (구버전 IsFull 게이트)")]
        [SerializeField] private bool requireTouhonFull = true;
        [Tooltip("해제 시 설정할 투혼 값 (구버전 ResetValue 50)")]
        [SerializeField] private float touhonResetValue = 50f;
        [Tooltip("발동 시 FP 완충 (구버전 CharacterFP.RecoverFull)")]
        [SerializeField] private bool recoverFPOnActivate = true;

        [Header("Trigger (구버전 스틱 입력)")]
        [Tooltip("패드: 좌/우 스틱 버튼 동시 누름으로 발동 (구버전 SelfDestructButton = LS+RS)")]
        [SerializeField] private bool gamepadDualStick = true;
        [Tooltip("키보드 대체 트리거 키")]
        [SerializeField] private Key keyboardTrigger = Key.G;

        [Header("Transformation Visuals (구버전 Activate/DeactivateOnLatentAbility)")]
        [Tooltip("발동 중 켜는 오브젝트 — 변신 모델 · 오라 이펙트 · 포스트프로세싱 Volume 등. 해제 시 다시 끈다")]
        [SerializeField] private List<GameObject> activateOnLatent = new List<GameObject>();
        [Tooltip("발동 중 끄는 오브젝트 — 평상시 모델 등. 해제 시 다시 켠다")]
        [SerializeField] private List<GameObject> deactivateOnLatent = new List<GameObject>();

        [Header("Post-Processing (Optional)")]
        [Tooltip("발동 중 weight를 페이드 인/아웃할 URP Volume (구버전 화면 연출 대응). 비우면 미사용 — 위 리스트에 Volume 오브젝트를 넣어 즉시 토글해도 된다")]
        [SerializeField] private Volume latentVolume;
        [Tooltip("Volume weight 페이드 시간(초). 0이면 즉시")]
        [SerializeField] private float volumeFadeTime = 0.35f;

        [Header("Hair Recolor (Latent)")]
        [Tooltip("발동 중 재질을 교체할 머리 렌더러 (예: Hair_JangHyu_mesh). 메시/애니는 그대로 두고 색만 바꾼다")]
        [SerializeField] private Renderer hairRenderer;
        [Tooltip("발동 중 적용할 빨강 머리 재질 (구버전 JangHyuRedHairToon 등). 비우면 리컬러 안 함")]
        [SerializeField] private Material latentHairMaterial;
        [Tooltip("교체할 재질 슬롯 인덱스. -1 = 모든 슬롯 교체(머리 메시가 여러 서브메시일 때 권장)")]
        [SerializeField] private int hairMaterialSlot = -1;

        [Header("Debug (View Only)")]
        [SerializeField] private bool isActive;
        [SerializeField] private float remaining;

        private Character _character;
        private readonly List<LatentAbility> _activeAbilities = new List<LatentAbility>();
        private float _endTime;
        private bool _prevTrigger;
        private CharacterCinematics _cinematics;
        private CameraSequence _camera;
        private float _volumeTarget;
        private Material[] _cachedHairMaterials;

        /// <summary>지금 잠재능력이 발동 중인가.</summary>
        public bool IsActive => isActive;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _cinematics = GetComponent<CharacterCinematics>();
            ApplyLatentVisuals(false);                    // 초기: 평상시 모델 ON, 변신/이펙트 OFF
            if (latentVolume != null) latentVolume.weight = 0f;
        }

        private void Update()
        {
            if (_character == null) return;

            if (isActive)
            {
                remaining = Mathf.Max(0f, _endTime - Time.time);
                if (Time.time >= _endTime) Deactivate();
            }

            bool trig = ReadTrigger();
            if (trig && !_prevTrigger && !isActive)
                TryActivate();
            _prevTrigger = trig;

            // 포스트프로세싱 Volume weight 페이드 (발동 중 1로, 해제 후 0으로).
            if (latentVolume != null && volumeFadeTime > 0f && latentVolume.weight != _volumeTarget)
                latentVolume.weight = Mathf.MoveTowards(latentVolume.weight, _volumeTarget, Time.deltaTime / volumeFadeTime);
        }

        private bool ReadTrigger()
        {
            bool sticks = false;
            if (gamepadDualStick)
            {
                var gp = Gamepad.current;
                sticks = gp != null && gp.leftStickButton.isPressed && gp.rightStickButton.isPressed;
            }
            var kb = Keyboard.current;
            bool key = kb != null && kb[keyboardTrigger].isPressed;
            return sticks || key;
        }

        private void TryActivate()
        {
            if (_character.IsDead) return;
            if (requireTouhonFull && !(_character.MaxTouhon > 0f && _character.CurrentTouhon >= _character.MaxTouhon))
                return;

            _activeAbilities.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.ability == null) continue;
                if (Random.value <= s.activationChance)
                {
                    s.ability.Activate(_character);
                    _activeAbilities.Add(s.ability);
                }
            }
            if (_activeAbilities.Count == 0) return;

            if (recoverFPOnActivate) _character.ResetFP();
            isActive = true;
            _endTime = Time.time + duration;

            // 발동 카메라 시퀀스 재생 (그랩/처형과 동일 경로 — CharacterCinematics의 좌/우 쌍, 시전자 Facing).
            if (_cinematics != null)
            {
                bool faceRight = _character.Movement == null || _character.Movement.FacingRight;
                _camera = _cinematics.ResolveLatentCamera(faceRight);
                _camera?.Play();
            }

            // 변신 연출 — 모델 교체 · 오라 이펙트 · 포스트프로세싱 ON (구버전 ApplyLatentAbilityTransformation).
            ApplyLatentVisuals(true);
        }

        private void Deactivate()
        {
            for (int i = 0; i < _activeAbilities.Count; i++)
                _activeAbilities[i]?.Deactivate(_character);
            _activeAbilities.Clear();
            isActive = false;
            remaining = 0f;
            _camera?.Stop();
            _camera = null;
            ApplyLatentVisuals(false);   // 변신 연출 원복 (모델/이펙트/포스트프로세싱)
            if (_character != null) _character.SetTouhon(Mathf.Min(touhonResetValue, _character.MaxTouhon));
        }

        /// <summary>변신 연출 토글 — 발동 시 변신 모델/이펙트/포스트프로세싱 ON, 평상시 모델 OFF (해제 시 반대). 구버전 Activate/DeactivateOnLatentAbility.</summary>
        private void ApplyLatentVisuals(bool on)
        {
            for (int i = 0; i < deactivateOnLatent.Count; i++)
                if (deactivateOnLatent[i] != null) deactivateOnLatent[i].SetActive(!on);
            for (int i = 0; i < activateOnLatent.Count; i++)
                if (activateOnLatent[i] != null) activateOnLatent[i].SetActive(on);

            // 머리 리컬러 — 메시/애니는 그대로, 재질만 빨강으로 교체 후 해제 시 원복 (구버전 빨강머리 대체안).
            if (hairRenderer != null && latentHairMaterial != null)
            {
                if (on)
                {
                    if (_cachedHairMaterials == null) _cachedHairMaterials = hairRenderer.sharedMaterials;
                    var mats = hairRenderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (hairMaterialSlot < 0 || hairMaterialSlot == i) mats[i] = latentHairMaterial;
                    hairRenderer.sharedMaterials = mats;
                }
                else if (_cachedHairMaterials != null)
                {
                    hairRenderer.sharedMaterials = _cachedHairMaterials;
                    _cachedHairMaterials = null;
                }
            }

            _volumeTarget = on ? 1f : 0f;
            if (latentVolume != null && volumeFadeTime <= 0f) latentVolume.weight = _volumeTarget;
        }

        private void OnDisable()
        {
            if (isActive) Deactivate();
        }
    }
}
