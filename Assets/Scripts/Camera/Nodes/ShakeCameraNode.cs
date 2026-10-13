using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 셰이크 노드. 로컬 위치/회전 노이즈, Punch, Spring 기반 셰이크를 담당할 자리.
    ///
    /// 이번 단계에서는 실제 셰이크 효과를 구현하지 않는다.
    /// Channel 수용 구조만 갖추고, 채널이 없으면 Offset 0으로 정상 통과한다.
    /// 향후 전투 셰이크는 ICameraChannel 구현체를 이 노드에 AddChannel하는 방식으로 붙인다.
    /// </summary>
    public class ShakeCameraNode : CameraNode
    {
        public override void Evaluate(float dt)
        {
            CameraNodeState state = CameraNodeState.Default;

            ApplyChannels(dt, ref state);

            // 채널이 없으면 PositionOffset/RotationOffset은 0 → 계층에 아무 영향 없음
            transform.localPosition = state.PositionOffset;
            transform.localRotation = Quaternion.Euler(state.RotationOffset);
        }
    }
}
