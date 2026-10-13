using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 씬 뷰에 엘리베이터 경로를 그린다. 런타임 OnDrawGizmosSelected가 시작·끝 점만 찍는 것과 달리,
    /// 저작에 필요한 정보 — 도착 지점에 놓인 발판의 실제 크기, 운행 방향, 탑승 판정 존 — 를 겹쳐 그린다.
    /// </summary>
    public static class MovingPlatformGizmos
    {
        private static readonly Color PathColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        private static readonly Color StartColor = new Color(0.3f, 1f, 0.4f, 0.9f);
        private static readonly Color EndColor = new Color(1f, 0.35f, 0.35f, 0.9f);
        private static readonly Color RiderColor = new Color(0.2f, 0.9f, 1f, 0.9f);

        public static void DrawPlatform(MovingPlatform platform, bool selected)
        {
            if (platform == null) return;

            Vector3 start = MovingPlatformAuthoring.StartWorld(platform);
            Vector3 end = MovingPlatformAuthoring.EndWorld(platform);
            Vector3 delta = end - start;

            Handles.color = PathColor;
            Handles.DrawDottedLine(start, end, 4f);

            bool hasDeck = MovingPlatformAuthoring.TryGetDeckBounds(platform, out Bounds deck);

            // 도착 지점의 발판 실루엣. 통로 폭이나 천장에 걸리는지는 이걸 봐야 알 수 있다.
            if (hasDeck)
            {
                Handles.color = StartColor;
                Handles.DrawWireCube(deck.center, deck.size);

                Handles.color = EndColor;
                Handles.DrawWireCube(deck.center + delta, deck.size);
            }
            else
            {
                Handles.color = StartColor;
                Handles.SphereHandleCap(0, start, Quaternion.identity,
                                        HandleUtility.GetHandleSize(start) * 0.12f, EventType.Repaint);
                Handles.color = EndColor;
                Handles.SphereHandleCap(0, end, Quaternion.identity,
                                        HandleUtility.GetHandleSize(end) * 0.12f, EventType.Repaint);
            }

            DrawDirection(platform, start, end);

            if (!selected) return;

            DrawRiderZone(platform);
            DrawLabel(platform, start, end);
        }

        /// <summary>운행 방향 화살표. 왕복은 양방향, 편도는 도착 방향 하나만.</summary>
        private static void DrawDirection(MovingPlatform platform, Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.0001f) return;

            Vector3 dir = delta.normalized;
            Quaternion forward = Quaternion.LookRotation(dir);
            float size = HandleUtility.GetHandleSize((start + end) * 0.5f) * 0.12f;

            Handles.color = PathColor;
            Handles.ConeHandleCap(0, Vector3.Lerp(start, end, 0.55f), forward, size, EventType.Repaint);

            if (MovingPlatformAuthoring.GetMode(platform) == MovingPlatform.Mode.PingPong)
            {
                Handles.ConeHandleCap(0, Vector3.Lerp(start, end, 0.45f),
                                      Quaternion.LookRotation(-dir), size, EventType.Repaint);
            }
        }

        /// <summary>탑승 판정 존. 발판 윗면을 덮지 못하면 라이더가 등록되지 않는다.</summary>
        private static void DrawRiderZone(MovingPlatform platform)
        {
            MovingPlatformRider rider = platform.GetComponentInChildren<MovingPlatformRider>(true);
            if (rider == null) return;

            BoxCollider box = rider.GetComponent<BoxCollider>();
            if (box == null) return;

            Matrix4x4 prev = Handles.matrix;
            Handles.matrix = box.transform.localToWorldMatrix;
            Handles.color = RiderColor;
            Handles.DrawWireCube(box.center, box.size);
            Handles.matrix = prev;
        }

        private static void DrawLabel(MovingPlatform platform, Vector3 start, Vector3 end)
        {
            float distance = Vector3.Distance(start, end);
            float oneWay = MovingPlatformAuthoring.OneWayTime(platform);
            string mode = MovingPlatformAuthoring.ModeLabel(MovingPlatformAuthoring.GetMode(platform));

            Vector3 mid = (start + end) * 0.5f;
            Handles.color = PathColor;
            Handles.Label(mid + Vector3.up * HandleUtility.GetHandleSize(mid) * 0.2f,
                          $"{mode} · {distance:0.##}m · 편도 {oneWay:0.##}초");
        }
    }
}
