using System.Collections.Generic;
using System.Text;
using MoreMountains.Tools;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 지금 진행 중인 대화에서 <b>지나간 대사</b>를 말한 사람과 함께 모아둔다.
    /// Dialogue Manager 오브젝트에 붙인다. 화면에 보여주는 것은 <see cref="DialogueLogPanel"/>이 맡는다.
    ///
    /// <b>대화 하나짜리 기록이다.</b> 새 대화가 시작되면 비운다 — "지금 이 대화에서 무슨 말이 오갔나"를
    /// 되짚는 용도라, 지난 대화까지 쌓이면 오히려 찾기 어려워진다.
    ///
    /// <b>어디서 줍는가</b> — Pixel Crushers는 자막을 UI에 넘기기 직전에 <c>OnConversationLine</c>을
    /// Dialogue Manager로 broadcast한다(<c>ConversationView.StartSubtitle</c>). 그래서 지금 화면에 뜨는
    /// 대사까지 포함된다. 대사가 없는 노드(Action·퀘스트)는 텍스트가 비어 있어 저절로 걸러진다.
    ///
    /// <b>글은 그 프레임 끝(<see cref="LateUpdate"/>)에 읽는다.</b> 같은 오브젝트에 붙은
    /// <see cref="DialogueTextMarkup"/>도 같은 broadcast를 받아 자막 글을 리치텍스트로 바꾸는데,
    /// 둘 중 누가 먼저 받을지는 컴포넌트 순서에 달려 있다. broadcast를 받는 그 자리에서 읽으면
    /// 아직 안 바뀐 원문(괄호 표기가 그대로인 글)을 담을 수 있다.
    ///
    /// 그렇다고 우리가 <c>Apply</c>를 한 번 더 걸어서는 안 된다 — 마크업 규칙 중 하나가 여는 기호로
    /// <c>&lt;</c>를 쓰기 때문에, 이미 변환된 글에 다시 걸면 <c>&lt;color=...&gt;</c> 같은 태그 자체를
    /// 강조 구간으로 잡아 noparse로 감싸버린다(화면에 태그가 글자로 보이는 증상). 그래서 변환은
    /// 마크업에게만 맡기고, 우리는 <b>다 바뀐 뒤의 글</b>을 그대로 가져온다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Log")]
    [DisallowMultipleComponent]
    public class DialogueLog : MonoBehaviour
    {
        /// <summary>기록 한 줄 — 말한 사람과 대사.</summary>
        public struct Line
        {
            /// <summary>말한 사람의 표시 이름(액터의 Display Name).</summary>
            public string Speaker;

            /// <summary>화면에 뜬 그대로의 대사(글자 효과 태그 포함).</summary>
            public string Text;
        }

        [MMInspectorGroup("기록", true, 100)]

        [Tooltip("보관할 최대 줄 수. 넘으면 오래된 것부터 버린다. 대화 하나가 이보다 길 일은 거의 없지만, " +
                 "반복 대사로 무한히 도는 이벤트가 메모리를 먹지 않게 한다.")]
        public int MaxLines = 300;

        [Tooltip("이름이 비어 있는 대사(내레이션 등)도 남긴다. 끄면 이름 없는 대사는 기록하지 않는다.")]
        public bool KeepUnnamedLines = true;

        [MMInspectorGroup("디버그", true, 101)]

        [Tooltip("어떤 대사가 기록됐는지 콘솔에 남긴다.")]
        public bool DebugLog = false;

        /// <summary>씬에 하나 있으면 되므로 정적으로 잡아둔다. 패널이 이걸 찾아 읽는다.</summary>
        public static DialogueLog Instance { get; private set; }

        /// <summary>기록이 바뀔 때마다 알린다. 로그창이 열려 있는 동안 실시간으로 따라가라고 있는 통로다.</summary>
        public event System.Action Changed;

        protected readonly List<Line> _lines = new List<Line>();

        /// <summary>
        /// 이번 프레임에 들어온 자막들. 프레임 끝에 읽어서 기록으로 옮긴다.
        /// 한 프레임에 여러 줄이 지나갈 수 있어(자동으로 넘어가는 노드) 하나만 들고 있으면 새어 나간다.
        /// </summary>
        protected readonly List<Subtitle> _pending = new List<Subtitle>();

        /// <summary>지금까지 모인 대사들. 오래된 것이 앞이다.</summary>
        public IReadOnlyList<Line> Lines => _lines;

        /// <summary>기록이 하나도 없는가.</summary>
        public bool IsEmpty => _lines.Count == 0;

        /// <summary>
        /// 도메인 리로드를 꺼둔 프로젝트라 정적 참조가 지난 플레이에서 살아남는다 —
        /// 파괴된 컴포넌트를 붙들고 있으면 로그창이 영영 빈 채로 뜬다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        protected virtual void OnEnable()
        {
            Instance = this;
        }

        protected virtual void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>새 대화가 열렸다 — 지난 대화의 기록은 버린다.</summary>
        public virtual void OnConversationStart(Transform actor)
        {
            _pending.Clear();
            Clear();
        }

        /// <summary>
        /// 자막이 화면에 뜨기 직전에 Pixel Crushers가 불러준다.
        /// 글은 아직 읽지 않는다 — 마크업이 지나갔는지 알 수 없어서, 프레임 끝까지 미룬다.
        /// </summary>
        public virtual void OnConversationLine(Subtitle subtitle)
        {
            if (subtitle == null || subtitle.formattedText == null)
            {
                return;
            }

            _pending.Add(subtitle);
        }

        /// <summary>미뤄둔 자막을 기록으로 옮긴다. 이 시점의 글이 화면에 뜬 글과 같다.</summary>
        protected virtual void LateUpdate()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _pending.Count; i++)
            {
                Subtitle subtitle = _pending[i];
                if (subtitle == null || subtitle.formattedText == null)
                {
                    continue;
                }

                string text = subtitle.formattedText.text;
                if (string.IsNullOrEmpty(text))
                {
                    // 대사가 없는 노드(Action·퀘스트)다. 기록할 것이 없다.
                    continue;
                }

                string speaker = subtitle.speakerInfo != null ? subtitle.speakerInfo.Name : string.Empty;
                if (string.IsNullOrEmpty(speaker) && !KeepUnnamedLines)
                {
                    continue;
                }

                Add(speaker, text);
            }

            _pending.Clear();
        }

        /// <summary>기록을 한 줄 넣는다. 대화 밖에서 직접 넣고 싶을 때도 쓸 수 있다.</summary>
        public virtual void Add(string speaker, string text)
        {
            _lines.Add(new Line { Speaker = speaker, Text = text });

            int max = Mathf.Max(1, MaxLines);
            if (_lines.Count > max)
            {
                _lines.RemoveRange(0, _lines.Count - max);
            }

            if (DebugLog)
            {
                Debug.Log($"[DialogueLog] {(string.IsNullOrEmpty(speaker) ? "(이름 없음)" : speaker)}: {text}", this);
            }

            Changed?.Invoke();
        }

        /// <summary>기록을 비운다.</summary>
        public virtual void Clear()
        {
            if (_lines.Count == 0)
            {
                return;
            }

            _lines.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// 기록 전체를 한 덩어리 글로 만든다. 이름과 대사를 어떻게 꾸밀지는 부르는 쪽이 정한다.
        /// </summary>
        /// <param name="nameFormat">이름 줄 서식. {0}에 이름이 들어간다. 비우면 이름 줄을 넣지 않는다.</param>
        /// <param name="separator">대사 사이에 넣을 글. 보통 빈 줄 하나.</param>
        public virtual string BuildText(string nameFormat, string separator)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < _lines.Count; i++)
            {
                Line line = _lines[i];

                if (i > 0 && !string.IsNullOrEmpty(separator))
                {
                    builder.Append(separator);
                }

                if (!string.IsNullOrEmpty(nameFormat) && !string.IsNullOrEmpty(line.Speaker))
                {
                    builder.AppendFormat(nameFormat, line.Speaker);
                    builder.Append('\n');
                }

                builder.Append(line.Text);
            }

            return builder.ToString();
        }
    }
}
