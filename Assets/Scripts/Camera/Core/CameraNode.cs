using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 카메라 계층의 단계별 노드 공통 기반.
    ///
    /// 각 노드는 CameraRig가 LateUpdate에서 Root → Leaf 순서로 Evaluate를 호출한다.
    /// 노드는 자신에게 등록된 Channel만 처리하며, 처리 순서는:
    ///   Base State 생성 → Priority 순 Channel.Sample → 완료 Channel 제거 → Transform 적용.
    /// </summary>
    public abstract class CameraNode : MonoBehaviour
    {
        private struct ChannelEntry
        {
            public ICameraChannel Channel;
            public int Priority;
        }

        private readonly List<ChannelEntry> _channels = new List<ChannelEntry>();

        /// <summary>소속 CameraRig. Rig가 Awake에서 주입한다.</summary>
        public CameraRig Rig { get; internal set; }

        /// <summary>효과 Channel 등록. priority가 낮을수록 먼저 샘플된다.</summary>
        public void AddChannel(ICameraChannel channel, int priority = 0)
        {
            if (channel == null)
                return;

            RemoveChannel(channel); // 중복 등록 방지
            _channels.Add(new ChannelEntry { Channel = channel, Priority = priority });
            _channels.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public void RemoveChannel(ICameraChannel channel)
        {
            for (int i = _channels.Count - 1; i >= 0; i--)
            {
                if (_channels[i].Channel == channel)
                    _channels.RemoveAt(i);
            }
        }

        public void ClearChannels() => _channels.Clear();

        /// <summary>CameraRig가 매 LateUpdate에 호출하는 평가 진입점.</summary>
        public abstract void Evaluate(float dt);

        /// <summary>등록된 Channel을 Priority 순으로 샘플하고, 완료된 Channel은 제거한다.</summary>
        protected void ApplyChannels(float dt, ref CameraNodeState state)
        {
            for (int i = 0; i < _channels.Count; i++)
                _channels[i].Channel.Sample(dt, ref state);

            for (int i = _channels.Count - 1; i >= 0; i--)
            {
                if (_channels[i].Channel.IsFinished)
                    _channels.RemoveAt(i);
            }
        }
    }
}
