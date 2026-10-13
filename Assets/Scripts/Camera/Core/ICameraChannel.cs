namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 카메라 노드에 주입되는 효과 단위.
    ///
    /// 예: Shake, Zoom, FOV Punch, Follow Offset 등은 모두 Channel로 구현해
    /// 담당 노드에 AddChannel로 등록한다. Channel은 자신이 등록된 노드의
    /// 상태(CameraNodeState)에만 기여하며, 다른 노드의 값을 직접 수정하지 않는다.
    /// </summary>
    public interface ICameraChannel
    {
        /// <summary>이번 프레임의 기여를 상태에 더한다.</summary>
        void Sample(float dt, ref CameraNodeState state);

        /// <summary>true를 반환하면 해당 프레임 평가 후 노드에서 제거된다.</summary>
        bool IsFinished { get; }
    }
}
