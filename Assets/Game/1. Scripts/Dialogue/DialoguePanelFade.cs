using PixelCrushers;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화창 패널들이 나타나고 사라지는 <b>페이드 속도</b>를 한자리에서 조절한다. 아예 끌 수도 있다.
    /// 대화창 UI 프리팹의 <b>루트</b>에 붙여두면 그 아래 모든 패널(NPC·PC·아군 자막, 선택지 메뉴 등)에 적용된다.
    ///
    /// 속사정 — Pixel Crushers의 <see cref="UIPanel"/>은 패널마다 Animator를 두고 <c>Show</c>/<c>Hide</c>
    /// 트리거로 CanvasGroup의 알파를 애니메이션한다(스톡 클립 기준 0.5초). 패널이 여럿이고 컨트롤러는 공용이라
    /// 클립을 고치면 프로젝트 전체가 같이 바뀌므로, 여기서는 <b>클립 대신 각 Animator를</b> 만진다.
    ///   - <b>속도</b>: <see cref="Animator.speed"/>를 곱한다. 2면 0.25초, 0.5면 1초가 된다.
    ///   - <b>끄기</b>: Animator를 꺼버린다. 그러면 UIPanel의 대기 코루틴이 애니메이터를 '없는 것'으로 보고
    ///     곧바로 콜백을 부르기 때문에 열고 닫는 것이 즉시 끝난다. 대신 알파를 아무도 안 올려주므로
    ///     여기서 1로 세워둔다 — 숨기는 쪽은 패널의 <c>Deactivate On Hidden</c>이 오브젝트를 꺼서 해결한다.
    ///
    /// 플레이 중에 값을 바꾸면 바로 반영된다(인스펙터에서 눈으로 맞추라고 열어둔 것).
    /// </summary>
    /// <remarks>
    /// 패널들이 열리기 전에 값이 잡혀 있어야 첫 대화부터 적용된다. 그래서 실행 순서를 앞으로 당겨둔다.
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    [AddComponentMenu("Aiara/Dialogue/Dialogue Panel Fade")]
    public class DialoguePanelFade : MonoBehaviour
    {
        [Tooltip("페이드 속도 배수. 1 = 원래 속도(스톡 클립 0.5초), 2 = 두 배 빠르게(0.25초), 0.5 = 두 배 느리게.")]
        [Range(0.25f, 8f)]
        public float FadeSpeed = 1f;

        [Tooltip("페이드를 아예 끈다. 켜면 대화창이 즉시 나타나고 즉시 사라진다(위 속도는 무시된다).")]
        public bool DisableFade = false;

        [Tooltip("어느 패널에 무엇을 걸었는지 콘솔에 남긴다.")]
        public bool DebugLog = false;

        protected virtual void Awake()
        {
            Apply();
        }

        /// <summary>지금 설정을 아래 패널들에 적용한다. 인스펙터에서 값을 바꾸면 플레이 중에도 다시 불린다.</summary>
        public virtual void Apply()
        {
            UIPanel[] panels = GetComponentsInChildren<UIPanel>(true);
            int applied = 0;

            for (int i = 0; i < panels.Length; i++)
            {
                UIPanel panel = panels[i];
                if (panel == null)
                {
                    continue;
                }

                // UIPanel이 자기 애니메이터를 찾는 방식 그대로 찾는다 — 자기 자신에 없으면 자식에서.
                Animator animator = panel.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = panel.GetComponentInChildren<Animator>(true);
                }

                if (animator == null)
                {
                    continue;
                }

                if (DisableFade)
                {
                    animator.enabled = false;

                    // 애니메이터가 꺼지면 알파를 올려줄 사람이 없다. 열렸을 때 안 보이는 사고를 막는다.
                    CanvasGroup group = panel.GetComponent<CanvasGroup>();
                    if (group != null)
                    {
                        group.alpha = 1f;
                    }

                    // 알파로 숨기지 않으므로, 닫힐 때 오브젝트를 끄지 않는 패널은 화면에 남는다.
                    if (!panel.deactivateOnHidden)
                    {
                        Debug.LogWarning($"[DialoguePanelFade] '{panel.name}'은 Deactivate On Hidden이 꺼져 있어 " +
                                         "페이드를 끄면 닫혀도 화면에 남습니다. 그 옵션을 켜세요.", panel);
                    }
                }
                else
                {
                    animator.enabled = true;
                    animator.speed = Mathf.Max(0.01f, FadeSpeed);
                }

                applied++;
            }

            if (DebugLog)
            {
                Debug.Log($"[DialoguePanelFade] 패널 {applied}개에 적용 — " +
                          $"{(DisableFade ? "페이드 끔(즉시)" : $"속도 x{FadeSpeed:0.##}")}", this);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 플레이 중 인스펙터에서 값을 바꾸면 바로 반영한다.
        /// 에디터 모드에서는 손대지 않는다 — 프리팹의 Animator 상태를 건드려 에셋이 더러워진다.
        /// </summary>
        protected virtual void OnValidate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Apply();
        }
#endif
    }
}
