using MoreMountains.Tools;
using UnityEngine;
using UnityEngine.UI;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화창 초상화(Portrait Image)가 **원본 이미지 비율대로** 보이도록 RectTransform 크기를 고쳐준다.
    /// 대화창 UI의 Portrait Image 오브젝트에 붙인다. (Tools/Aiara/대화창 UI 셋업이 자동으로 붙여준다.)
    ///
    /// 왜 필요한가 —
    /// 스톡 대화창의 Portrait Image는 64x64 고정 슬롯이다. Unity UI의 Image는 sprite를 rect에 그냥 늘려
    /// 채우기 때문에, 세로로 긴 프로필을 넣으면 정사각으로 눌려 보인다.
    /// Pixel Crushers에도 <c>Use Portrait Native Size</c>가 있지만 그건 **원본 픽셀 크기 그대로**(512x768 등)
    /// sizeDelta에 박아버려서 대화창 밖으로 삐져나간다. 그래서 "슬롯 크기는 지키되 그 안에서 비율 유지"를
    /// 하는 쪽이 필요하다.
    ///
    /// 동작 —
    /// 처음 켜질 때의 rect 크기를 **기준 상자**로 기억해두고, sprite가 바뀔 때마다 그 상자에 맞춰
    /// 비율대로 크기를 다시 계산한다. sprite가 비면 기준 상자로 되돌린다.
    /// 기준 상자를 한 번 기억한 뒤에는 rect 크기를 우리가 계속 덮어쓰므로, 슬롯 크기를 바꾸고 싶으면
    /// <see cref="ReferenceSize"/>에 직접 값을 넣으면 된다.
    ///
    /// 앵커가 stretch(가로세로로 늘어나는 설정)면 sizeDelta가 크기가 아니라 여백 값이라 이 방식이 통하지 않는다.
    /// 그 경우 경고만 한 번 찍고 아무것도 하지 않는다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Portrait Aspect Fitter")]
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class DialoguePortraitAspectFitter : MonoBehaviour
    {
        /// <summary>비율을 맞추는 방식.</summary>
        public enum FitModes
        {
            /// <summary>기준 상자 안에 통째로 들어가게 맞춘다. 남는 쪽은 여백이 된다. (기본)</summary>
            Contain,

            /// <summary>가로를 기준 상자에 고정하고 세로만 비율대로 늘린다.</summary>
            MatchWidth,

            /// <summary>세로를 기준 상자에 고정하고 가로만 비율대로 늘린다.</summary>
            MatchHeight
        }

        [MMInspectorGroup("비율 맞추기", true, 100)]

        [Tooltip("Contain = 기준 상자 안에 다 들어가게(여백 생김), " +
                 "MatchWidth = 가로 고정하고 세로만 비율대로, " +
                 "MatchHeight = 세로 고정하고 가로만 비율대로.")]
        public FitModes FitMode = FitModes.Contain;

        [Tooltip("원본이 기준 상자보다 작을 때 키워서 상자를 채운다. " +
                 "끄면 작은 이미지는 원본 픽셀 크기 그대로 둔다. (Contain에서만 의미가 있다)")]
        public bool AllowUpscale = true;

        [Tooltip("기준 상자 크기. 0으로 두면 처음 켜질 때의 rect 크기를 그대로 기준으로 쓴다. " +
                 "슬롯 크기를 바꾸고 싶으면 여기에 값을 넣어라 — rect를 직접 고쳐봐야 이 컴포넌트가 덮어쓴다.")]
        public Vector2 ReferenceSize = Vector2.zero;

        [MMInspectorGroup("디버그", true, 101)]

        [MMReadOnly]
        [Tooltip("실제로 쓰고 있는 기준 상자.")]
        public Vector2 DebugReferenceBox;

        protected Image _image;
        protected RectTransform _rect;
        protected Sprite _lastSprite;
        protected Vector2 _box;
        protected bool _boxCaptured;
        protected bool _warnedAboutStretch;

        protected virtual void Awake()
        {
            _image = GetComponent<Image>();
            _rect = transform as RectTransform;
            CaptureReferenceBox();
        }

        protected virtual void OnEnable()
        {
            // 초상화 오브젝트는 sprite가 없을 때 통째로 꺼졌다가 켜지므로, 켜질 때마다 한 번 다시 맞춘다.
            _lastSprite = null;
            Refresh();
        }

        protected virtual void LateUpdate()
        {
            // Pixel Crushers가 sprite를 갈아끼우는 시점을 알려주는 이벤트가 없어서 값 비교로 잡는다.
            // 참조 비교라 비용은 없다시피 하고, 초상화가 떠 있는 동안에만 돈다.
            if (_image != null && _image.sprite != _lastSprite)
            {
                Refresh();
            }
        }

        /// <summary>지금 물려 있는 sprite에 맞춰 크기를 다시 계산한다.</summary>
        public virtual void Refresh()
        {
            if (_image == null || _rect == null)
            {
                return;
            }

            if (!CaptureReferenceBox())
            {
                return;
            }

            _lastSprite = _image.sprite;

            if (_lastSprite == null)
            {
                _rect.sizeDelta = _box;
                return;
            }

            // packed(아틀라스) sprite도 rect가 원본 잘린 영역을 그대로 들고 있어서 비율 계산에 그대로 쓸 수 있다.
            float sourceWidth = _lastSprite.rect.width;
            float sourceHeight = _lastSprite.rect.height;

            if (sourceWidth <= 0f || sourceHeight <= 0f)
            {
                return;
            }

            _rect.sizeDelta = Fit(sourceWidth, sourceHeight);
        }

        protected virtual Vector2 Fit(float sourceWidth, float sourceHeight)
        {
            float aspect = sourceWidth / sourceHeight;

            switch (FitMode)
            {
                case FitModes.MatchWidth:
                    return new Vector2(_box.x, _box.x / aspect);

                case FitModes.MatchHeight:
                    return new Vector2(_box.y * aspect, _box.y);

                default:
                    float scale = Mathf.Min(_box.x / sourceWidth, _box.y / sourceHeight);
                    if (!AllowUpscale)
                    {
                        scale = Mathf.Min(scale, 1f);
                    }

                    return new Vector2(sourceWidth * scale, sourceHeight * scale);
            }
        }

        /// <summary>기준 상자를 정한다. 앵커가 stretch면 이 방식이 성립하지 않으므로 false를 돌려준다.</summary>
        protected virtual bool CaptureReferenceBox()
        {
            if (ReferenceSize.x > 0f && ReferenceSize.y > 0f)
            {
                _box = ReferenceSize;
                _boxCaptured = true;
                DebugReferenceBox = _box;
                return true;
            }

            if (_boxCaptured)
            {
                DebugReferenceBox = _box;
                return true;
            }

            if (!Mathf.Approximately(_rect.anchorMin.x, _rect.anchorMax.x) ||
                !Mathf.Approximately(_rect.anchorMin.y, _rect.anchorMax.y))
            {
                if (!_warnedAboutStretch)
                {
                    _warnedAboutStretch = true;
                    Debug.LogWarning($"[초상화 비율] {name}의 앵커가 stretch라 크기를 건드리지 않습니다. " +
                                     "앵커를 한 점으로 모으거나 Reference Size를 직접 넣어주세요.", this);
                }

                return false;
            }

            _box = _rect.sizeDelta;
            _boxCaptured = _box.x > 0f && _box.y > 0f;
            DebugReferenceBox = _box;

            if (!_boxCaptured)
            {
                Debug.LogWarning($"[초상화 비율] {name}의 rect 크기가 0이라 기준 상자를 잡지 못했습니다. " +
                                 "Reference Size를 직접 넣어주세요.", this);
            }

            return _boxCaptured;
        }
    }
}
