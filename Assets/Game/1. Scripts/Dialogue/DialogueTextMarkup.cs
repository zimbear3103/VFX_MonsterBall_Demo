using System.Collections.Generic;
using System.Text;
using MoreMountains.Tools;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대사 안의 기호로 감싼 구간에 글자 효과를 입힌다. Dialogue Manager 오브젝트에 붙인다.
    ///
    /// 시트에 이미 자리잡은 표기를 그대로 쓴다 — 작가가 쓰던 규칙을 바꾸지 않아도 되게 하기 위해서다:
    ///   <c>(장휴)</c> 고유명사·인명 / <c>[철신포]</c> 기술·스킬명
    ///
    /// <b>동작 지점</b> — Pixel Crushers는 자막을 UI에 넘기기 직전에 <c>OnConversationLine</c>을
    /// Dialogue Manager로 broadcast한다(<c>ConversationView.StartSubtitle</c>). 그 시점에
    /// <c>subtitle.formattedText.text</c>를 고쳐두면 타자기·자막 패널·글자수 계산이 전부 바뀐 글을 기준으로 돈다.
    /// 그래서 <b>DB나 시트는 손대지 않는다</b> — 시트가 원본이라는 규칙을 지킨 채 표현만 입힌다.
    ///
    /// <b>정적인 효과</b>(굵게·색·크기)는 TMP 리치텍스트로 바로 바꾼다.
    /// <b>흔들림</b>은 한 프레임짜리 태그로 표현할 수 없으므로 <c>&lt;link="shake"&gt;</c>로 구간만 표시해두고,
    /// 실제 떨림은 <see cref="DialogueTextShaker"/>가 매 프레임 정점을 밀어서 만든다.
    /// link 태그는 화면에 아무것도 그리지 않고 글자수에도 안 잡혀서 타자기와 충돌하지 않는다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Text Markup")]
    [DisallowMultipleComponent]
    public class DialogueTextMarkup : MonoBehaviour
    {
        /// <summary>흔들릴 구간을 표시하는 link ID. <see cref="DialogueTextShaker"/>가 같은 값을 찾는다.</summary>
        public const string ShakeLinkId = "shake";

        /// <summary>기호 한 쌍에 어떤 효과를 입힐지.</summary>
        [System.Serializable]
        public class MarkupRule
        {
            [Tooltip("인스펙터에서 알아보기 위한 이름. 동작에는 쓰이지 않는다.")]
            public string Name = "새 규칙";

            [Tooltip("구간이 시작되는 기호. 여는 기호와 닫는 기호가 같아도 된다(예: * *).")]
            public string Open = "[";

            [Tooltip("구간이 끝나는 기호.")]
            public string Close = "]";

            [Tooltip("기호 자체를 화면에 남길지 여부. 기본은 끔 — 괄호는 사라지고 효과만 남는다. " +
                     "켜면 괄호도 같이 보인다(꺾쇠는 noparse로 감싸므로 켜도 안 깨진다).")]
            public bool KeepDelimiters = false;

            [Tooltip("이 구간을 흔든다. 세기와 속도는 아래 '흔들림'에서 정한다.")]
            public bool Shake = false;

            [Tooltip("이 구간을 굵게.")]
            public bool Bold = false;

            [Tooltip("이 구간에 색을 입힌다.")]
            public bool UseColor = false;

            public Color Color = new Color(0.71f, 0.20f, 0.17f);

            [Tooltip("글자 크기 배율(%). 100이면 그대로.")]
            [Range(50f, 200f)]
            public float SizePercent = 100f;
        }

        [MMInspectorGroup("표기 규칙", true, 100)]

        [Tooltip("위에서부터 검사한다. 같은 자리에서 겹치면 먼저 있는 규칙이 이긴다.")]
        public List<MarkupRule> Rules = new List<MarkupRule>
        {
            // (고유명사·이름) → 파란색
            new MarkupRule
            {
                Name = "고유명사·이름 ( )", Open = "(", Close = ")",
                UseColor = true, Color = new Color(0.16f, 0.39f, 0.85f),
            },

            // [키워드] → 노란색
            new MarkupRule
            {
                Name = "키워드 [ ]", Open = "[", Close = "]",
                UseColor = true, Color = new Color(0.91f, 0.71f, 0.09f),
            },

            // <강조> → 크게 + 떨림
            new MarkupRule
            {
                Name = "강조 < >", Open = "<", Close = ">",
                Bold = true, Shake = true, SizePercent = 130f,
            },
        };

        [Tooltip("선택지 텍스트에도 같은 규칙을 적용한다.")]
        public bool ApplyToResponses = true;

        [MMInspectorGroup("흔들림", true, 101)]

        [Tooltip("흔들리는 폭(픽셀).")]
        public float ShakeAmplitude = 2f;

        [Tooltip("흔들리는 속도.")]
        public float ShakeFrequency = 18f;

        [MMInspectorGroup("디버그", true, 102)]

        [Tooltip("바뀐 결과 문자열을 콘솔에 남긴다.")]
        public bool DebugLog = false;

        /// <summary>중첩을 파고들 최대 깊이. 잘못된 표기로 무한 재귀에 빠지지 않게 막는 값.</summary>
        private const int MaxNestingDepth = 4;

        /// <summary>Pixel Crushers가 자막을 UI에 넘기기 직전에 부른다.</summary>
        public virtual void OnConversationLine(Subtitle subtitle)
        {
            if (subtitle == null || subtitle.formattedText == null)
            {
                return;
            }

            string before = subtitle.formattedText.text;
            string after = Apply(before);

            if (before == after)
            {
                return;
            }

            subtitle.formattedText.text = after;

            if (DebugLog)
            {
                Debug.Log($"[DialogueTextMarkup] {before}\n  → {after}", this);
            }
        }

        /// <summary>선택지 메뉴가 뜨기 직전에 부른다.</summary>
        public virtual void OnConversationResponseMenu(Response[] responses)
        {
            if (!ApplyToResponses || responses == null)
            {
                return;
            }

            foreach (Response response in responses)
            {
                if (response != null && response.formattedText != null)
                {
                    response.formattedText.text = Apply(response.formattedText.text);
                }
            }
        }

        /// <summary>대화가 시작될 때, 자막 패널의 TMP에 흔들림 컴포넌트가 붙어 있는지 확인한다.</summary>
        protected virtual void OnEnable()
        {
            EnsureShakers();
        }

        protected virtual void OnConversationStart(Transform actor)
        {
            EnsureShakers();
        }

        /// <summary>
        /// 자막 패널마다 <see cref="DialogueTextShaker"/>를 붙여둔다.
        ///
        /// 손으로 세 패널에 하나씩 붙이게 하면 하나 빠뜨렸을 때 "저 창에서만 안 흔들린다"가 되는데,
        /// 원인을 찾기가 매우 어렵다. 그래서 여기서 한 번에 챙긴다.
        /// </summary>
        protected virtual void EnsureShakers()
        {
            var standardUI = DialogueManager.dialogueUI as StandardDialogueUI;
            if (standardUI == null || standardUI.conversationUIElements == null)
            {
                return;
            }

            var panels = standardUI.conversationUIElements.subtitlePanels;
            if (panels == null)
            {
                return;
            }

            foreach (var panel in panels)
            {
                if (panel == null)
                {
                    continue;
                }

                TMP_Text tmp = panel.subtitleText.textMeshProUGUI;
                if (tmp == null)
                {
                    continue;
                }

                var shaker = tmp.GetComponent<DialogueTextShaker>();
                if (shaker == null)
                {
                    shaker = tmp.gameObject.AddComponent<DialogueTextShaker>();
                }

                shaker.Amplitude = ShakeAmplitude;
                shaker.Frequency = ShakeFrequency;
            }
        }

        /// <summary>표기를 TMP 리치텍스트로 바꾼다.</summary>
        public virtual string Apply(string text)
        {
            return string.IsNullOrEmpty(text) ? text : Convert(text, 0);
        }

        /// <summary>
        /// 한 번 훑으면서 규칙에 맞는 구간을 감싼다.
        ///
        /// 이미 들어 있는 리치텍스트(<c>&lt;...&gt;</c>)는 통째로 넘긴다 — 그 안의 괄호까지 효과로 잡으면
        /// 태그가 깨진다. 짝이 안 맞는 기호는 <b>건드리지 않고 그대로 둔다</b>.
        /// 잘못 쓴 표기 하나 때문에 대사가 사라지는 것보다 효과가 안 걸리는 편이 낫다.
        /// </summary>
        private string Convert(string text, int depth)
        {
            var sb = new StringBuilder(text.Length + 32);
            int i = 0;

            while (i < text.Length)
            {
                // 규칙을 먼저 본다. 리치텍스트 통과보다 뒤에 두면 여는 기호가 '<'인 규칙(강조)이
                // 영영 안 걸린다 — '<'를 만나는 족족 태그로 보고 넘겨버리기 때문이다.
                MarkupRule rule = MatchingRule(text, i);
                if (rule != null && depth < MaxNestingDepth)
                {
                    int contentStart = i + rule.Open.Length;
                    int closeAt = text.IndexOf(rule.Close, contentStart, System.StringComparison.Ordinal);

                    if (closeAt >= 0)
                    {
                        string inner = text.Substring(contentStart, closeAt - contentStart);

                        string body = rule.KeepDelimiters
                            ? Literal(rule.Open) + Convert(inner, depth + 1) + Literal(rule.Close)
                            : Convert(inner, depth + 1);

                        sb.Append(Wrap(body, rule));
                        i = closeAt + rule.Close.Length;
                        continue;
                    }
                }

                // 규칙에 안 걸린 <...>는 원래 있던 리치텍스트로 보고 그대로 통과시킨다.
                if (text[i] == '<')
                {
                    int close = text.IndexOf('>', i);
                    if (close >= 0)
                    {
                        sb.Append(text, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                }

                sb.Append(text[i]);
                i++;
            }

            return sb.ToString();
        }

        private MarkupRule MatchingRule(string text, int index)
        {
            if (Rules == null)
            {
                return null;
            }

            foreach (MarkupRule rule in Rules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.Open) || string.IsNullOrEmpty(rule.Close))
                {
                    continue;
                }

                if (string.CompareOrdinal(text, index, rule.Open, 0, rule.Open.Length) == 0)
                {
                    return rule;
                }
            }

            return null;
        }

        /// <summary>
        /// 기호를 화면에 그대로 남길 때 쓰는 이스케이프.
        ///
        /// 꺾쇠(<c>&lt;</c> <c>&gt;</c>)를 날것으로 내보내면 TMP가 그걸 다시 태그로 파싱하려 든다.
        /// <c>&lt;멈춰라!!&gt;</c>가 화면에서 통째로 사라지던 것이 바로 이 현상이다.
        /// <c>&lt;noparse&gt;</c>로 감싸면 그 구간만 파싱을 멈춰서 기호가 글자로 찍힌다.
        /// (안쪽 내용은 따로 감싸므로 우리가 붙인 색·크기 태그는 그대로 살아 있다.)
        /// </summary>
        private static string Literal(string delimiter)
        {
            if (delimiter.IndexOf('<') < 0 && delimiter.IndexOf('>') < 0)
            {
                return delimiter;
            }

            return "<noparse>" + delimiter + "</noparse>";
        }

        private static string Wrap(string body, MarkupRule rule)
        {
            var sb = new StringBuilder(body.Length + 48);
            var closing = new Stack<string>();

            if (rule.Shake)
            {
                sb.Append("<link=\"").Append(ShakeLinkId).Append("\">");
                closing.Push("</link>");
            }

            if (rule.UseColor)
            {
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(rule.Color)).Append('>');
                closing.Push("</color>");
            }

            if (rule.Bold)
            {
                sb.Append("<b>");
                closing.Push("</b>");
            }

            if (!Mathf.Approximately(rule.SizePercent, 100f))
            {
                sb.Append("<size=").Append(Mathf.RoundToInt(rule.SizePercent)).Append("%>");
                closing.Push("</size>");
            }

            sb.Append(body);

            while (closing.Count > 0)
            {
                sb.Append(closing.Pop());
            }

            return sb.ToString();
        }
    }
}
