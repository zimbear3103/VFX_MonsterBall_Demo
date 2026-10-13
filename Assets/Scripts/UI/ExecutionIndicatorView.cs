using UnityEngine;
using UnityEngine.UI;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 절명기 인디케이터(R3Button) 뷰 — 구버전 Aiara.ExecutionIndicatorView 이관.
    /// 플레이어가 적 기준 화면 왼쪽/오른쪽 어느 쪽에 있는지에 따라 스프라이트를 전환한다.
    /// 스프라이트는 프리팹(R3Button)에서 한 번만 지정하면 모든 몬스터에 공통 적용된다.
    /// ExecutionIndicator.Update가 매 프레임 SetSide()를 호출한다.
    /// </summary>
    public class ExecutionIndicatorView : MonoBehaviour
    {
        [Tooltip("플레이어가 적의 '왼쪽'(화면 기준)에 있을 때 표시할 스프라이트")]
        public Sprite LeftSideSprite;

        [Tooltip("플레이어가 적의 '오른쪽'(화면 기준)에 있을 때 표시할 스프라이트")]
        public Sprite RightSideSprite;

        [Tooltip("스프라이트를 적용할 Image. 비워두면 자식에서 자동 탐색.")]
        public Image TargetImage;

        protected virtual void Awake()
        {
            if (TargetImage == null)
                TargetImage = GetComponentInChildren<Image>(true);
        }

        /// <summary>좌/우 상태 갱신. 해당 방향 스프라이트가 비어 있으면 기존 스프라이트를 유지한다.</summary>
        public virtual void SetSide(bool playerOnRight)
        {
            if (TargetImage == null) return;
            Sprite desired = playerOnRight ? RightSideSprite : LeftSideSprite;
            if (desired != null && TargetImage.sprite != desired)
                TargetImage.sprite = desired;
        }
    }
}
