using TMPro;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// <c>&lt;link="shake"&gt;</c>로 표시된 구간의 글자를 떨리게 한다.
    /// 자막 TMP 오브젝트에 붙는다 — <see cref="DialogueTextMarkup"/>이 대화 시작 때 알아서 붙여준다.
    ///
    /// <b>왜 link 태그인가</b> — 흔들림은 한 프레임짜리 서식으로 표현할 수 없어서 매 프레임 정점을 밀어야 하는데,
    /// 그러려면 "어느 글자부터 어느 글자까지"를 알아야 한다. TMP의 link 태그는 화면에 아무것도 그리지 않으면서
    /// <c>textInfo.linkInfo</c>에 그 구간의 글자 번호를 그대로 남겨준다. 게다가 태그는 글자수에 안 잡혀서
    /// 타자기(<c>maxVisibleCharacters</c>)와도 충돌하지 않는다.
    ///
    /// <b>왜 매 프레임 ForceMeshUpdate인가</b> — 정점을 직접 밀면 그 값이 그대로 남기 때문에, 원본을 따로
    /// 들고 있지 않으면 흔들림이 프레임마다 누적돼 글자가 날아가 버린다. 원본을 캐시해두는 방법도 있지만
    /// 타자기가 글자를 한 자씩 늘릴 때마다 메시가 다시 만들어져서 캐시를 언제 버릴지가 까다롭다.
    /// 자막은 길어야 100자 남짓이라, 매 프레임 원본으로 되돌리고 다시 미는 쪽이 훨씬 단순하고 안전하다.
    /// 흔들릴 구간이 없으면 아무것도 하지 않는다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Text Shaker")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public class DialogueTextShaker : MonoBehaviour
    {
        [Tooltip("흔들리는 폭(픽셀).")]
        public float Amplitude = 2f;

        [Tooltip("흔들리는 속도.")]
        public float Frequency = 18f;

        [Tooltip("찾을 link ID. DialogueTextMarkup이 넣는 값과 같아야 한다.")]
        public string LinkId = DialogueTextMarkup.ShakeLinkId;

        /// <summary>글자마다 흔들림 위상을 어긋나게 하는 값. 클수록 글자들이 제각각 논다.</summary>
        private const float PerCharacterPhase = 0.7f;

        protected TMP_Text _text;

        protected virtual void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        /// <summary>
        /// 타자기와 자막 갱신이 모두 끝난 뒤에 밀어야 한다. Update에서 하면 같은 프레임에
        /// TMP가 메시를 다시 만들면서 흔들림이 지워질 수 있다.
        /// </summary>
        protected virtual void LateUpdate()
        {
            if (_text == null || Amplitude <= 0f)
            {
                return;
            }

            // 원본 상태로 되돌린다. 안 하면 지난 프레임의 흔들림 위에 또 밀려서 글자가 흩어진다.
            _text.ForceMeshUpdate();

            TMP_TextInfo textInfo = _text.textInfo;
            if (textInfo == null || textInfo.linkCount == 0)
            {
                return;
            }

            bool moved = false;

            for (int l = 0; l < textInfo.linkCount; l++)
            {
                TMP_LinkInfo link = textInfo.linkInfo[l];
                if (!string.Equals(link.GetLinkID(), LinkId))
                {
                    continue;
                }

                int first = link.linkTextfirstCharacterIndex;
                int last = first + link.linkTextLength;

                for (int c = first; c < last && c < textInfo.characterCount; c++)
                {
                    if (ShakeCharacter(textInfo, c))
                    {
                        moved = true;
                    }
                }
            }

            if (moved)
            {
                _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
            }
        }

        /// <summary>글자 하나의 네 정점을 같은 만큼 민다. 아직 안 보이는 글자는 건너뛴다.</summary>
        protected virtual bool ShakeCharacter(TMP_TextInfo textInfo, int characterIndex)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[characterIndex];

            // 공백이나 타자기가 아직 안 찍은 글자는 정점 자체가 없다.
            if (!charInfo.isVisible)
            {
                return false;
            }

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;

            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;
            if (vertices == null || vertexIndex + 3 >= vertices.Length)
            {
                return false;
            }

            Vector3 offset = Offset(characterIndex);

            vertices[vertexIndex] += offset;
            vertices[vertexIndex + 1] += offset;
            vertices[vertexIndex + 2] += offset;
            vertices[vertexIndex + 3] += offset;

            return true;
        }

        /// <summary>
        /// 글자별 흔들림. 대화 중 timeScale이 0으로 내려가도 멈추면 안 되므로 unscaled로 잰다.
        /// PerlinNoise는 sin보다 덜 규칙적이라 "떨림"에 가깝게 보인다.
        /// </summary>
        protected virtual Vector3 Offset(int characterIndex)
        {
            float t = Time.unscaledTime * Frequency;
            float seed = characterIndex * PerCharacterPhase;

            float x = (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f * Amplitude;
            float y = (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f * Amplitude;

            return new Vector3(x, y, 0f);
        }
    }
}
