using System.Collections.Generic;
using MoreMountains.Tools;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화가 진행되는 동안 게임 UI(HUD)를 감췄다가 끝나면 되돌린다. Dialogue Manager 오브젝트에 붙인다.
    ///
    /// 끄는 방법은 <b>Canvas 컴포넌트를 비활성화</b>하는 것이다 — GameObject를 끄지 않는다.
    /// GameObject를 끄면 HUD 스크립트들의 OnDisable/OnEnable이 돌면서 바인딩이나 코루틴이 끊길 수 있는데,
    /// Canvas만 끄면 그리기만 멈추고 나머지는 그대로 살아 있다. ver2의 HUD는 전부 런타임에 캔버스를
    /// 만들어 쓰므로(<c>HUDBuilder.CreateOverlayCanvas</c>) 씬에서 미리 지정해둘 대상이 없다 —
    /// 그래서 기본 동작이 "찾아서 끄기"다.
    ///
    /// <b>되돌릴 때는 우리가 끈 것만 되돌린다.</b> 대화 전에 이미 꺼져 있던 캔버스는 건드리지 않는다 —
    /// 그러지 않으면 다른 이유로 숨겨둔 UI가 대화 끝에 되살아난다.
    ///
    /// 대화창 자신은 당연히 제외한다. 화면 페이드처럼 대화보다 위에 있어야 하는 것들도
    /// <see cref="KeepSortingOrderAtOrAbove"/>로 남겨둔다 (ScreenFader의 FadeCanvas가 9999다).
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Game UI Hider")]
    [DisallowMultipleComponent]
    public class DialogueGameUiHider : MonoBehaviour
    {
        [MMInspectorGroup("숨길 대상", true, 100)]

        [Tooltip("씬의 Canvas를 찾아서 끈다. ver2 HUD는 런타임에 캔버스를 만들기 때문에 이 방식이 기본이다. " +
                 "끄면 아래 Also Hide에 직접 넣은 것만 숨긴다.")]
        public bool AutoHideCanvases = true;

        [Tooltip("Sorting Order가 이 값 이상인 캔버스는 숨기지 않는다. " +
                 "화면 페이드(ScreenFader의 FadeCanvas = 9999)처럼 대화 위에 계속 떠 있어야 하는 것들을 위한 안전선이다.")]
        public int KeepSortingOrderAtOrAbove = 1000;

        [Tooltip("자동 탐색과 별개로 반드시 숨길 오브젝트들. 이쪽은 GameObject를 통째로 끈다. " +
                 "월드 공간 표시물처럼 캔버스가 아닌 것을 숨길 때 쓴다.")]
        public GameObject[] AlsoHide;

        [Tooltip("자동 탐색에서 제외할 오브젝트들. 여기 넣은 것과 그 자식에 있는 캔버스는 건드리지 않는다.")]
        public GameObject[] NeverHide;

        [MMInspectorGroup("디버그", true, 101)]

        [Tooltip("무엇을 숨기고 되돌렸는지 콘솔에 남긴다.")]
        public bool DebugLog = false;

        /// <summary>지금 게임 UI를 감춰둔 상태인가.</summary>
        public bool IsHidden => _hidden;

        protected bool _hidden;
        protected bool _subscribed;

        /// <summary>우리가 끈 캔버스들. 되돌릴 때 이 목록만 다시 켠다.</summary>
        protected readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

        /// <summary>우리가 끈 오브젝트들(Also Hide). 되돌릴 때 이 목록만 다시 켠다.</summary>
        protected readonly List<GameObject> _hiddenObjects = new List<GameObject>();

        protected virtual void OnEnable()
        {
            Subscribe();
        }

        protected virtual void OnDisable()
        {
            Unsubscribe();

            // 대화 도중 이 컴포넌트가 꺼지면 HUD가 영영 사라진다 — 여기서 반드시 되돌린다.
            Show();
        }

        protected virtual void Subscribe()
        {
            if (_subscribed || DialogueManager.instance == null)
            {
                return;
            }

            DialogueManager.instance.conversationStarted += OnConversationStarted;
            DialogueManager.instance.conversationEnded += OnConversationEnded;
            _subscribed = true;
        }

        protected virtual void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            if (DialogueManager.instance != null)
            {
                DialogueManager.instance.conversationStarted -= OnConversationStarted;
                DialogueManager.instance.conversationEnded -= OnConversationEnded;
            }

            _subscribed = false;
        }

        /// <summary>
        /// Dialogue Manager보다 이 컴포넌트가 먼저 켜졌을 수 있으므로, 구독이 아직이면 계속 시도한다.
        /// (Dialogue Manager는 자기 Awake에서 인스턴스를 세운다)
        /// </summary>
        protected virtual void Update()
        {
            if (!_subscribed)
            {
                Subscribe();
            }
        }

        protected virtual void OnConversationStarted(Transform actor) => Hide();

        protected virtual void OnConversationEnded(Transform actor) => Show();

        /// <summary>게임 UI를 감춘다. 이미 감춘 상태면 아무것도 하지 않는다.</summary>
        public virtual void Hide()
        {
            if (_hidden)
            {
                return;
            }

            _hidden = true;
            _hiddenCanvases.Clear();
            _hiddenObjects.Clear();

            if (AutoHideCanvases)
            {
                HideCanvases();
            }

            if (AlsoHide != null)
            {
                for (int i = 0; i < AlsoHide.Length; i++)
                {
                    GameObject target = AlsoHide[i];
                    if (target == null || !target.activeSelf)
                    {
                        continue;
                    }

                    target.SetActive(false);
                    _hiddenObjects.Add(target);
                }
            }

            if (DebugLog)
            {
                Debug.Log($"[DialogueGameUiHider] 캔버스 {_hiddenCanvases.Count}개, " +
                          $"오브젝트 {_hiddenObjects.Count}개를 감췄습니다.", this);
            }
        }

        /// <summary>감춰둔 것을 되돌린다. 우리가 끈 것만 되돌린다.</summary>
        public virtual void Show()
        {
            if (!_hidden)
            {
                return;
            }

            _hidden = false;

            for (int i = 0; i < _hiddenCanvases.Count; i++)
            {
                if (_hiddenCanvases[i] != null)
                {
                    _hiddenCanvases[i].enabled = true;
                }
            }

            for (int i = 0; i < _hiddenObjects.Count; i++)
            {
                if (_hiddenObjects[i] != null)
                {
                    _hiddenObjects[i].SetActive(true);
                }
            }

            if (DebugLog)
            {
                Debug.Log($"[DialogueGameUiHider] 캔버스 {_hiddenCanvases.Count}개, " +
                          $"오브젝트 {_hiddenObjects.Count}개를 되돌렸습니다.", this);
            }

            _hiddenCanvases.Clear();
            _hiddenObjects.Clear();
        }

        protected virtual void HideCanvases()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            Transform dialogueUiTransform = ResolveDialogueUiTransform();

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];

                // 이미 꺼져 있던 것은 우리 몫이 아니다 — 되돌릴 때 켜버리면 안 된다.
                if (canvas == null || !canvas.enabled)
                {
                    continue;
                }

                if (ShouldKeep(canvas, dialogueUiTransform))
                {
                    continue;
                }

                canvas.enabled = false;
                _hiddenCanvases.Add(canvas);
            }
        }

        /// <summary>이 캔버스를 남겨둬야 하는가.</summary>
        protected virtual bool ShouldKeep(Canvas canvas, Transform dialogueUiTransform)
        {
            // 대화보다 위에 있어야 하는 것들(화면 페이드 등).
            if (canvas.sortingOrder >= KeepSortingOrderAtOrAbove)
            {
                return true;
            }

            // 대화창 자신. 대화 UI가 이 캔버스 안에 들어 있으면 끄면 안 된다.
            if (dialogueUiTransform != null && dialogueUiTransform.IsChildOf(canvas.transform))
            {
                return true;
            }

            // Dialogue Manager가 들고 있는 캔버스(경고창 등)도 대화용이다.
            if (canvas.transform.IsChildOf(transform))
            {
                return true;
            }

            if (NeverHide != null)
            {
                for (int i = 0; i < NeverHide.Length; i++)
                {
                    GameObject keep = NeverHide[i];
                    if (keep != null && canvas.transform.IsChildOf(keep.transform))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>지금 쓰이는 대화창의 트랜스폼. 아직 없으면 null.</summary>
        protected virtual Transform ResolveDialogueUiTransform()
        {
            var ui = DialogueManager.dialogueUI as MonoBehaviour;
            return ui != null ? ui.transform : null;
        }
    }
}
