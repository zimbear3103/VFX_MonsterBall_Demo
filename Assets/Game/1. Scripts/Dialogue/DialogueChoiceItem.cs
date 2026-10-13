using TMPro;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 선택지 하나. 미리 UI에 깔아두고 <see cref="DialogueChoiceMenuPanel"/>의 Choices 리스트에 순서대로 물려둔다.
    ///
    /// 런타임에 새로 만들어지지 않는다 — 패널이 선택지 개수만큼 앞에서부터 켜고 글자만 갈아끼운다.
    /// 그래서 오브젝트마다 배치/장식을 손으로 잡아둘 수 있다.
    ///
    /// 포커스가 오면 스케일이 살짝 커진다. 커지는 기준은 <b>Awake 시점의 스케일</b>이라
    /// 인스펙터에서 크기를 다르게 잡아둔 선택지도 각자 자기 크기 기준으로 커진다.
    ///
    /// 시간은 전부 unscaled로 잰다 — 대화 중 timeScale이 0으로 내려가도 연출이 멈추면 안 되기 때문.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Choice Item")]
    public class DialogueChoiceItem : MonoBehaviour
    {
        [Tooltip("선택지 글자. 비워두면 하위에서 처음 찾은 TextMeshPro를 쓴다.")]
        public TMP_Text Label;

        [Tooltip("포커스가 왔을 때 원래 크기에 곱할 배율.")]
        public float FocusedScale = 1.1f;

        [Tooltip("스케일이 변하는 속도. 0 이하면 즉시 바뀐다.")]
        public float ScaleSpeed = 14f;

        [Tooltip("포커스일 때만 켜둘 **부속물**(커서, 테두리 등). 필요 없으면 비워둔다. " +
                 "선택지 오브젝트 자신을 넣으면 안 된다 — 포커스가 없을 때 스스로 꺼져 사라진다(무시하고 경고를 남긴다).")]
        public GameObject[] FocusObjects;

        [Tooltip("고를 수 없는 선택지(조건 미달)일 때 글자 색. Use Disabled Color가 켜져 있을 때만 쓴다.")]
        public Color DisabledColor = new Color(1f, 1f, 1f, 0.4f);

        [Tooltip("고를 수 없는 선택지의 글자 색을 바꾼다. 끄면 색은 그대로 둔다.")]
        public bool UseDisabledColor = true;

        /// <summary>지금 포커스를 가지고 있는가.</summary>
        public bool IsFocused { get; protected set; }

        /// <summary>고를 수 있는 선택지인가. 조건이 안 맞는 응답이면 false — 포커스가 건너뛴다.</summary>
        public bool IsInteractable { get; protected set; }

        protected Vector3 _baseScale = Vector3.one;
        protected Color _baseColor = Color.white;
        protected bool _initialized;
        protected bool _warnedSelfFocusObject;

        protected virtual void Awake()
        {
            Initialize();
        }

        protected virtual void Reset()
        {
            Label = GetComponentInChildren<TMP_Text>(true);
        }

        /// <summary>
        /// 원래 스케일/색을 기억해 둔다. Awake 전에 패널이 먼저 부를 수 있어(실행 순서는 보장되지 않는다)
        /// 어느 쪽이 먼저 들어와도 한 번만 잡히도록 막아둔다.
        /// </summary>
        protected virtual void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            if (Label == null)
            {
                Label = GetComponentInChildren<TMP_Text>(true);
            }

            _baseScale = transform.localScale;

            if (Label != null)
            {
                _baseColor = Label.color;
            }
        }

        /// <summary>선택지를 켜고 글자를 세팅한다.</summary>
        public virtual void Show(string text, bool interactable)
        {
            Initialize();

            IsInteractable = interactable;

            if (Label != null)
            {
                Label.text = text;

                if (UseDisabledColor)
                {
                    Label.color = interactable ? _baseColor : DisabledColor;
                }
            }

            // 포커스는 패널이 따로 정해준다. 켜질 때는 항상 포커스 없는 상태에서 시작한다.
            SetFocused(false, true);
            gameObject.SetActive(true);
        }

        /// <summary>선택지를 끈다.</summary>
        public virtual void Hide()
        {
            Initialize();

            IsFocused = false;
            IsInteractable = false;

            SetFocusObjectsActive(false);
            transform.localScale = _baseScale;

            if (Label != null)
            {
                Label.text = string.Empty;

                if (UseDisabledColor)
                {
                    Label.color = _baseColor;
                }
            }

            gameObject.SetActive(false);
        }

        /// <param name="immediate">스케일을 보간 없이 곧바로 맞춘다. 켜지는 첫 프레임에 쓴다.</param>
        public virtual void SetFocused(bool focused, bool immediate = false)
        {
            Initialize();

            IsFocused = focused;
            SetFocusObjectsActive(focused);

            if (immediate || ScaleSpeed <= 0f)
            {
                transform.localScale = TargetScale();
            }
        }

        protected virtual void Update()
        {
            if (ScaleSpeed <= 0f)
            {
                return;
            }

            Vector3 target = TargetScale();

            if (transform.localScale == target)
            {
                return;
            }

            transform.localScale = Vector3.Lerp(transform.localScale, target,
                1f - Mathf.Exp(-ScaleSpeed * Time.unscaledDeltaTime));
        }

        protected virtual Vector3 TargetScale()
        {
            return IsFocused ? _baseScale * FocusedScale : _baseScale;
        }

        protected virtual void SetFocusObjectsActive(bool value)
        {
            if (FocusObjects == null)
            {
                return;
            }

            for (int i = 0; i < FocusObjects.Length; i++)
            {
                if (FocusObjects[i] == null)
                {
                    continue;
                }

                // 자기 자신은 건너뛴다. 그대로 두면 포커스가 없는 선택지가 스스로 꺼져 화면에서 사라진다.
                if (FocusObjects[i] == gameObject)
                {
                    if (!_warnedSelfFocusObject)
                    {
                        _warnedSelfFocusObject = true;
                        Debug.LogWarning($"[선택지] '{name}'의 Focus Objects에 자기 자신이 들어 있어 무시합니다. " +
                                         "커서/테두리 같은 부속물만 넣으세요. 포커스 표시는 스케일이 대신 합니다.", this);
                    }

                    continue;
                }

                FocusObjects[i].SetActive(value);
            }
        }
    }
}
