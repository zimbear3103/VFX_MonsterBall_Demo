using UnityEngine;

namespace Aiara
{
    /// <summary>
    /// VFX 미러링 모드.
    /// FullMirror: 회전 켤레 + Z 스케일 반전으로 정밀한 좌우 미러링.
    /// SimpleDirectional: Y축 90°/-90° 회전만 적용하는 단순 방향 전환.
    /// </summary>
    public enum VFXMirrorMode
    {
        FullMirror,
        SimpleDirectional
    }

    /// <summary>
    /// 공격 시 재생할 VFX 하나의 설정을 담는 구조체.
    /// FeedbackKey로 FeedbackManager에 등록된 MMF_Player를 참조하며,
    /// 오프셋/회전/스케일과 미러링 모드를 한 묶음으로 관리합니다.
    /// </summary>
    [System.Serializable]
    public class AttackVFXEntry
    {
        [Tooltip("FeedbackManager에 등록된 키 (FeedbackKeys.* 사용 권장)")]
        public string Key;

        [Tooltip("미러링 모드")]
        public VFXMirrorMode MirrorMode = VFXMirrorMode.FullMirror;

        [Tooltip("VFX 로컬 위치 오프셋 (좌측 기준)")]
        public Vector3 Offset = Vector3.zero;

        [Tooltip("VFX 회전각 (좌측 기준, FullMirror 모드에서만 사용)")]
        public Vector3 Rotation = Vector3.zero;

        [Tooltip("VFX 스케일 (좌측 기준, FullMirror 모드에서만 사용)")]
        public Vector3 Scale = Vector3.one;

        [Tooltip("오프셋 반전 축 (우측일 때 반전할 축)")]
        public FlipAxis OffsetFlipAxis = FlipAxis.Z;

        /// <summary>
        /// 캐릭터 방향에 맞게 local TRS를 산출하고 FeedbackManager로 재생합니다.
        /// baseTransform이 null이면 FeedbackManager는 부모를 변경하지 않고 위치만 적용합니다.
        /// </summary>
        public void Play(bool facingRight, Transform baseTransform)
        {
            if (string.IsNullOrEmpty(Key)) return;

            Vector3 localPosition = GetFlippedOffset(facingRight);
            Vector3 localEulerAngles;
            Vector3 localScale;

            switch (MirrorMode)
            {
                case VFXMirrorMode.FullMirror:
                    if (facingRight)
                    {
                        localEulerAngles = new Vector3(-Rotation.x, -Rotation.y, Rotation.z);
                        localScale = new Vector3(Scale.x, Scale.y, -Scale.z);
                    }
                    else
                    {
                        localEulerAngles = Rotation;
                        localScale = Scale;
                    }
                    break;
                case VFXMirrorMode.SimpleDirectional:
                default:
                    localEulerAngles = new Vector3(0f, facingRight ? 90f : -90f, 0f);
                    localScale = Vector3.one;
                    break;
            }

            FeedbackManager.PlayFeedback(Key, baseTransform, localPosition, localEulerAngles, localScale);
        }

        private Vector3 GetFlippedOffset(bool facingRight)
        {
            Vector3 offset = Offset;
            if (facingRight)
            {
                switch (OffsetFlipAxis)
                {
                    case FlipAxis.Z:
                        offset.z = -offset.z;
                        break;
                    case FlipAxis.X:
                        offset.x = -offset.x;
                        break;
                }
            }
            return offset;
        }
    }

    /// <summary>
    /// 오프셋 반전에 사용할 축.
    /// </summary>
    public enum FlipAxis
    {
        Z,
        X
    }
}
