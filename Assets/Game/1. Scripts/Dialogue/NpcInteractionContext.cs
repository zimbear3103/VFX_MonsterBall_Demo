using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// EastButton(B)을 공유하는 기능들 사이의 우선권 중재자.
    ///
    /// 대화 존이 플레이어를 감지하면 자기 자신을 등록하고, 벗어나거나 비활성화되면 해제한다.
    /// EastButton에 다른 기능을 붙일 때는 <see cref="IsAvailable"/>가 true면 스스로 양보할 것 —
    /// 그래야 NPC 앞에서 B를 눌렀을 때 다른 기능이 같이 터지지 않는다.
    ///
    /// 등록을 HashSet으로 관리하므로 같은 존이 두 번 등록돼도 카운트가 어긋나지 않는다.
    /// (진입/이탈 이벤트가 한쪽만 불리는 상황에서도 안전)
    /// </summary>
    public static class NpcInteractionContext
    {
        private static readonly HashSet<NpcDialogueZone> _activeZones = new HashSet<NpcDialogueZone>();

        /// <summary>파괴된 존을 걸러내기 위한 캐시된 조건자 (Unity의 == null 오버로드를 탄다)</summary>
        private static readonly Predicate<NpcDialogueZone> ZoneIsGone = zone => zone == null;

        /// <summary>
        /// 플레이를 시작할 때 등록을 비운다.
        ///
        /// 이 프로젝트는 도메인 리로드를 꺼두어서 static이 이전 플레이 세션의 값을 그대로 들고 온다.
        /// 파괴된 존은 조회할 때 걸러지지만, 애초에 빈 상태로 시작하는 편이 확실하다.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _activeZones.Clear();
        }

        /// <summary>지금 EastButton이 대화에 쓰여야 하는가 (= 활성 대화 존 안에 있는가)</summary>
        public static bool IsAvailable
        {
            get
            {
                if (_activeZones.Count == 0)
                {
                    return false;
                }

                // 씬 전환 등으로 존이 파괴되면 Unregister가 안 불릴 수 있어 여기서 청소한다.
                _activeZones.RemoveWhere(ZoneIsGone);
                return _activeZones.Count > 0;
            }
        }

        public static void Register(NpcDialogueZone zone)
        {
            if (zone == null)
            {
                return;
            }

            _activeZones.Add(zone);
        }

        public static void Unregister(NpcDialogueZone zone)
        {
            if (zone == null)
            {
                return;
            }

            _activeZones.Remove(zone);
        }

        /// <summary>씬 전환 등에서 잔존 등록을 일괄 정리한다.</summary>
        public static void Clear()
        {
            _activeZones.Clear();
        }

        /// <summary>
        /// 지금 대화 버튼을 가져갈 존이 <paramref name="zone"/>인가.
        ///
        /// <b>왜 필요한가</b> — 존들은 각자 <c>Update</c>에서 버튼을 직접 읽는다. NPC들이 가까이 서 있어
        /// 트리거가 겹치면 한 번의 B 입력에 <b>여러 존이 동시에 반응</b>하고, 누가 이길지는 Unity가 컴포넌트를
        /// 갱신하는 순서에 달린다. 그래서 "방금 대화한 NPC의 대화가 다시 열리는" 일이 생긴다.
        /// 거리로 딱 하나만 고르면 순서와 무관하게 항상 같은 결과가 나온다.
        ///
        /// <b>말을 걸 수 없는 존은 후보에서 뺀다</b> — 조건이 안 맞거나 횟수를 다 쓴 NPC가 더 가깝다는 이유로
        /// 뒤에 있는 NPC까지 막아버리면, 겹친 자리에서 아무와도 대화가 안 되는 상태가 된다.
        /// </summary>
        public static bool IsClosest(NpcDialogueZone zone, Vector3 playerPosition)
        {
            if (zone == null)
            {
                return false;
            }

            _activeZones.RemoveWhere(ZoneIsGone);

            NpcDialogueZone closest = null;
            float closestDistance = float.MaxValue;

            foreach (NpcDialogueZone candidate in _activeZones)
            {
                if (candidate == null || !candidate.CanTalk)
                {
                    continue;
                }

                float distance = (candidate.InteractionPoint - playerPosition).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = candidate;
                }
            }

            // 후보가 하나도 없으면(전부 대화 불가) 막지 않는다 — 강제 시작처럼 CanTalk를
            // 거치지 않는 경로가 자기 판단으로 열 수 있어야 한다.
            return closest == null || closest == zone;
        }
    }
}
