using System.Collections.Generic;
using UnityEngine;

namespace Aiara
{
    /// <summary>
    /// GameObject가 활성화되는 순간 FeedbackManager 키 기반 VFX를 재생한다.
    /// AttackAction의 TimedVisual target으로 붙여, 애니메이션 이벤트/타이밍에 맞춰
    /// 등록된 MMF_Player를 발동시키는 용도 (구버전 JangHyuAnimationEventReceiver의
    /// PlayFeedback 디스패치 이식). 캐릭터 Facing에 따라 좌우 미러링된다.
    /// </summary>
    public class FeedbackOnEnable : MonoBehaviour
    {
        [Header("Feedback Entries")]
        [Tooltip("재생할 VFX 목록. Key = FeedbackManager 등록 키 (FeedbackKeys.* 권장). 오프셋/회전/스케일은 좌측(FacingLeft) 기준이며 Facing에 따라 자동 미러링.")]
        [SerializeField] private List<AttackVFXEntry> entries = new List<AttackVFXEntry>();

        private Yeolha.BeltScroll.Character _character;

        private void OnEnable()
        {
            if (_character == null)
                _character = GetComponentInParent<Yeolha.BeltScroll.Character>(true);

            bool facingRight = _character == null || _character.Movement == null
                || _character.Movement.FacingRight;
            Transform baseTransform = _character != null ? _character.transform : transform;

            for (int i = 0; i < entries.Count; i++)
                entries[i]?.Play(facingRight, baseTransform);
        }
    }
}
