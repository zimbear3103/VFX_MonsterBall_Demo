using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

namespace Aiara
{
    /// <summary>
    /// 플레이어 사망 시 표시되는 "GAME OVER" / 부활 안내 UI.
    /// 사망 모션이 재생된 뒤 GuardableHealth가 이 UI를 Show()로 띄우고,
    /// 패드 A(South) 또는 키보드 A 를 HoldDuration(기본 1초) 동안 누르면 부활을 확정한다.
    ///
    /// 이 컴포넌트는 '씬에 직접 배치'해서 사용한다.
    /// - 루트에 Canvas / CanvasGroup / 이 스크립트를 두고, 하위에 배경/텍스트/홀드 게이지를 구성한다.
    /// - 인스펙터에서 _canvas / _group / HoldFill 등을 드래그로 연결한다(비우면 자동 탐색 폴백).
    /// - 평소엔 비활성(SetVisible(false))으로 두고, 사망 시에만 Show()로 켜진다.
    /// </summary>
    public class ReviveGameOverUI : MonoBehaviour
    {
        [Header("References (씬에서 드래그 연결)")]
        [Tooltip("이 UI의 Canvas. 비우면 자기 자신/자식에서 자동 탐색.")]
        public Canvas Canvas;

        [Tooltip("표시/숨김에 사용할 CanvasGroup. 비우면 자기 자신/자식에서 자동 탐색.")]
        public CanvasGroup Group;

        [Tooltip("홀드 진행률을 표시할 Filled 이미지(가로). A를 누르는 동안 fillAmount가 0→1로 찬다.")]
        public Image HoldFill;

        [Tooltip("(선택) 'GAME OVER' 텍스트. 연결하면 GameOverText 문자열로 갱신.")]
        public TextMeshProUGUI GameOverLabel;

        [Tooltip("(선택) 부활 안내 텍스트. 연결하면 PromptFormat/ButtonLabel로 갱신.")]
        public TextMeshProUGUI PromptLabel;

        [Tooltip("게임오버 등장 애니를 재생할 Animator. 비우면 자기 자신/자식에서 자동 탐색.")]
        public Animator Animator;

        [Header("HUD Portrait Swap")]
        [Tooltip("평상시 표시되는 HUD RawImage. 비우면 PlayerUI_UP/life_UI/Image/RawImage를 자동 탐색.")]
        public RawImage AliveHudRawImage;

        [Tooltip("사망 UI 표시 중 활성화할 HUD RawImage. 비우면 PlayerUI_UP/life_UI/Image/RawImage (1)를 자동 탐색.")]
        public RawImage DeadHudRawImage;

        [Header("Animation")]
        [Tooltip("사망 시 재생할 애니 트리거/상태 이름. 컨트롤러에 같은 이름의 트리거가 있으면 SetTrigger, 없으면 해당 상태를 직접 재생한다.")]
        public string GameOverTrigger = "GameOver";

        [Header("Text")]
        [Tooltip("게임오버 큰 텍스트")]
        public string GameOverText = "GAME OVER";

        [Tooltip("부활 안내 텍스트. {0} = 버튼 라벨")]
        public string PromptFormat = "{0} 를 길게 눌러 부활";

        [Tooltip("버튼 라벨 (패드 A 버튼 안내)")]
        public string ButtonLabel = "A키";

        [Header("Hold To Revive")]
        [Tooltip("부활에 필요한 홀드 시간(초)")]
        public float HoldDuration = 1f;

        [Header("Startup")]
        [Tooltip("Awake에서 자동으로 숨길지 여부. 씬에 켜둔 채 배치해도 시작 시 숨겨진다.")]
        public bool HideOnAwake = true;

        /// <summary>현재 활성 인스턴스(싱글톤).</summary>
        public static ReviveGameOverUI Instance { get; private set; }

        protected bool _waiting;
        protected Action _onConfirm;
        protected float _holdTimer;
        protected bool _hudPortraitSwapApplied;
        protected bool _aliveHudRawImageWasActive;
        protected bool _deadHudRawImageWasActive;

        /// <summary>
        /// 씬에 배치된 인스턴스를 반환한다. 활성 인스턴스가 없으면 비활성 오브젝트까지 탐색하고,
        /// 그래도 없으면(씬 미배치) null을 반환한다. 호출 측(GuardableHealth)은 null 폴백을 처리한다.
        /// </summary>
        public static ReviveGameOverUI GetOrCreate()
        {
            if (Instance != null)
            {
                return Instance;
            }

            // 비활성 상태로 씬에 배치돼 있을 수 있으므로 Inactive 포함 탐색.
            ReviveGameOverUI found = FindFirstObjectByType<ReviveGameOverUI>(FindObjectsInactive.Include);
            if (found != null)
            {
                found.gameObject.SetActive(true);
                found.EnsureInitialized();
                Instance = found;
                return found;
            }

            return null;
        }

protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureInitialized();

            if (HideOnAwake)
            {
                SetVisible(false);
                // The controller's default state is an empty idle (Entry -> New State), NOT GameOver,
                // so the Animator can stay ENABLED without auto-playing the GameOver clip.
                // Keeping it enabled is required so SetTrigger(GameOver) fires reliably later:
                // enabling a just-disabled Animator and setting a trigger on the same frame loses it.
                // Rebind() parks it on the empty default state so it doesn't drive the CanvasGroup alpha.
                if (Animator != null)
                {
                    Animator.enabled = true;
                    Animator.Rebind();
                }
            }
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>인스펙터 참조가 비어 있으면 자기 자신/자식에서 자동 탐색해 채운다.</summary>
        protected virtual void EnsureInitialized()
        {
            if (Canvas == null)
            {
                Canvas = GetComponent<Canvas>();
                if (Canvas == null) Canvas = GetComponentInChildren<Canvas>(true);
            }
            if (Group == null)
            {
                Group = GetComponent<CanvasGroup>();
                if (Group == null) Group = GetComponentInChildren<CanvasGroup>(true);
            }
            if (Animator == null)
            {
                Animator = GetComponent<Animator>();
                if (Animator == null) Animator = GetComponentInChildren<Animator>(true);
            }
            ResolveHudPortraitImages();
            // HoldFill / 라벨은 자동탐색하면 엉뚱한 이미지를 잡을 수 있어 인스펙터 연결을 우선한다.

            // 라벨 텍스트 동기화(연결된 경우에만)
            if (GameOverLabel != null && !string.IsNullOrEmpty(GameOverText))
            {
                GameOverLabel.text = GameOverText;
            }
            if (PromptLabel != null && !string.IsNullOrEmpty(PromptFormat))
            {
                PromptLabel.text = string.Format(PromptFormat, ButtonLabel);
            }
        }

        /// <summary>
        /// UI를 표시하고 부활 입력(A 홀드)을 기다린다. 홀드 완료 시 onConfirm을 호출한다.
        /// </summary>
        public virtual void Show(Action onConfirm)
        {
            EnsureInitialized();
           
            _onConfirm = onConfirm;
            _waiting = true;
            _holdTimer = 0f;
            UpdateHoldFill(0f);
            ApplyHudPortraitSwap();
            PlayGameOverAnimation();
        }

        /// <summary>
        /// CanvasGroup 알파를 직접 켜는 대신 GameOver 애니메이션으로 UI를 등장시킨다.
        /// 알파(0→1)는 애니 클립이 제어하므로 여기서는 알파를 건드리지 않는다.
        /// Canvas / 레이캐스트만 활성화한다.
        /// </summary>
protected virtual void PlayGameOverAnimation()
        {
            // On death, fire the Animator's GameOver trigger to play the entrance animation.
            // Force the CanvasGroup fully visible so the UI is guaranteed on-screen; the GameOver
            // clip animates the inner content (scale / image alpha) for the entrance effect.
      
            if (Canvas != null) Canvas.enabled = true;
            if (Group != null)
            {
                Group.alpha = 1f;
                Group.blocksRaycasts = true;
                Group.interactable = true;
            }

            if (Animator != null)
            {
                if (!Animator.enabled) Animator.enabled = true;
                if (HasAnimatorTrigger(Animator, GameOverTrigger))
                {
                    Animator.ResetTrigger(GameOverTrigger);
                    Animator.SetTrigger(GameOverTrigger);
          
                }
                else if (!string.IsNullOrEmpty(GameOverTrigger))
                {
                    Animator.Play(GameOverTrigger, 0, 0f);
                }
            }
            else
            {
                ForceVisible();
            }
        }

        /// <summary>애니/외부 간섭과 무관하게 UI를 확실히 보이게 만든다(캔버스 on · 루트 스케일 1 · 알파 1).</summary>
        protected void ForceVisible()
        {
            if (Canvas != null) Canvas.enabled = true;
            var rt = transform as RectTransform;
            if (rt != null && rt.localScale.sqrMagnitude < 0.999f) rt.localScale = Vector3.one;
            if (Group != null)
            {
                Group.blocksRaycasts = true;
                Group.interactable = true;
                Group.alpha = 1f;
            }
            // 애니가 비활성으로 두던 자식 요소(GAME OVER 텍스트/프롬프트/버튼 등)까지 강제로 켠다.
            for (int i = 0; i < transform.childCount; i++)
            {
                var go = transform.GetChild(i).gameObject;
                if (!go.activeSelf) go.SetActive(true);
            }
        }

        /// <summary>애니메이터에 지정한 이름의 Trigger 파라미터가 존재하는지 확인.</summary>
        protected virtual bool HasAnimatorTrigger(Animator animator, string triggerName)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(triggerName))
            {
                return false;
            }
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>UI를 숨기고 입력 대기를 중단한다.</summary>
public virtual void Hide()
        {
            _waiting = false;
            _onConfirm = null;
            _holdTimer = 0f;
            UpdateHoldFill(0f);
            // Return the Animator to the empty default state instead of disabling it, so it stops
            // holding GameOver's final (alpha=1) pose AND stays enabled for the next death's SetTrigger.
            if (Animator != null)
            {
                Animator.enabled = true;
                Animator.Rebind();
            }
            RestoreHudPortraitSwap();
            SetVisible(false);
        }

        protected virtual void Update()
        {
            if (!_waiting)
            {
                return;
            }

            // A(패드 South 또는 키보드 A)를 누르고 있는 동안 타이머 증가, 떼면 리셋.
            // HoldDuration(기본 1초) 동안 유지하면 부활 확정.
            if (ConfirmHeld())
            {
                _holdTimer += Time.unscaledDeltaTime;
                float dur = HoldDuration > 0.01f ? HoldDuration : 1f;
                UpdateHoldFill(Mathf.Clamp01(_holdTimer / dur));

                if (_holdTimer >= dur)
                {
                    _waiting = false;
                    _holdTimer = 0f;
                    Action cb = _onConfirm;
                    _onConfirm = null;
                    cb?.Invoke();
                }
            }
            else
            {
                if (_holdTimer > 0f)
                {
                    _holdTimer = 0f;
                    UpdateHoldFill(0f);
                }
            }
        }

        // 안전망: 대기 중에는 매 프레임 강제로 보이게 유지(애니메이터/외부가 알파를 낮추는 것 방지).
        // LateUpdate는 Animator 평가 이후 실행되므로 알파를 확실히 1로 덮어쓴다.
protected virtual void LateUpdate()
        {
            if (!_waiting) return;
            // While waiting, only keep the canvas/raycasts on so the UI stays interactable.
            // Do not disable the Animator or force alpha/scale, so the GameOver clip can play.
            if (Canvas != null) Canvas.enabled = true;
            if (Group != null) Group.blocksRaycasts = true;
        }

        /// <summary>패드 A(South) 또는 키보드 A 를 '누르고 있는 동안' true.</summary>
        protected virtual bool ConfirmHeld()
        {
            Gamepad gp = Gamepad.current;
            if (gp != null && gp.buttonSouth.isPressed)
            {
                return true;
            }

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.aKey.isPressed)
            {
                return true;
            }

            return false;
        }

        /// <summary>홀드 진행률(0~1)을 게이지 바에 반영.</summary>
        protected virtual void UpdateHoldFill(float t)
        {
            if (HoldFill != null)
            {
                HoldFill.fillAmount = Mathf.Clamp01(t);
            }
        }

        protected virtual void SetVisible(bool visible)
        {
            if (Group != null)
            {
                Group.alpha = visible ? 1f : 0f;
                Group.blocksRaycasts = visible;
                Group.interactable = visible;
            }
            // 캔버스는 끄지 않는다(꺼졌다 켜질 때의 문제/애니 간섭 방지). 표시/숨김은 CanvasGroup 알파로만 제어한다.
            if (Canvas != null)
            {
                Canvas.enabled = true;
            }

            // CanvasGroup/Canvas가 모두 없을 때를 대비해 자식 표시도 토글(폴백).
            if (Group == null && Canvas == null)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    transform.GetChild(i).gameObject.SetActive(visible);
                }
            }
        }

        protected virtual void ApplyHudPortraitSwap()
        {
            ResolveHudPortraitImages();

            if (AliveHudRawImage == null && DeadHudRawImage == null)
            {
                return;
            }

            if (!_hudPortraitSwapApplied)
            {
                _aliveHudRawImageWasActive = AliveHudRawImage != null && AliveHudRawImage.gameObject.activeSelf;
                _deadHudRawImageWasActive = DeadHudRawImage != null && DeadHudRawImage.gameObject.activeSelf;
                _hudPortraitSwapApplied = true;
            }

            if (AliveHudRawImage != null) AliveHudRawImage.gameObject.SetActive(false);
            if (DeadHudRawImage != null) DeadHudRawImage.gameObject.SetActive(true);
        }

        protected virtual void RestoreHudPortraitSwap()
        {
            if (!_hudPortraitSwapApplied)
            {
                return;
            }

            if (AliveHudRawImage != null) AliveHudRawImage.gameObject.SetActive(_aliveHudRawImageWasActive);
            if (DeadHudRawImage != null) DeadHudRawImage.gameObject.SetActive(_deadHudRawImageWasActive);
            _hudPortraitSwapApplied = false;
        }

        protected virtual void ResolveHudPortraitImages()
        {
            if (AliveHudRawImage != null && DeadHudRawImage != null)
            {
                return;
            }

            RawImage[] rawImages = UnityEngine.Object.FindObjectsByType<RawImage>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < rawImages.Length; i++)
            {
                RawImage rawImage = rawImages[i];
                if (rawImage == null || !IsPlayerLifeHudRawImage(rawImage.transform))
                {
                    continue;
                }

                if (AliveHudRawImage == null && rawImage.name == "RawImage")
                {
                    AliveHudRawImage = rawImage;
                }
                else if (DeadHudRawImage == null && rawImage.name == "RawImage (1)")
                {
                    DeadHudRawImage = rawImage;
                }

                if (AliveHudRawImage != null && DeadHudRawImage != null)
                {
                    return;
                }
            }
        }

        protected static bool IsPlayerLifeHudRawImage(Transform imageTransform)
        {
            if (imageTransform == null) return false;

            Transform imageParent = imageTransform.parent;
            if (imageParent == null || imageParent.name != "Image") return false;

            Transform lifeUI = imageParent.parent;
            if (lifeUI == null || lifeUI.name != "life_UI") return false;

            Transform playerUIUp = lifeUI.parent;
            return playerUIUp != null && playerUIUp.name == "PlayerUI_UP";
        }
    }
}
