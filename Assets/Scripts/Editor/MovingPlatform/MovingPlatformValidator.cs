using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 엘리베이터 세팅에서 자주 나는 실수를 훑어 경고 목록으로 돌려준다.
    /// 기계적으로 고칠 수 있는 항목은 Fix 델리게이트를 함께 실어 보낸다.
    ///
    /// 엘리베이터는 경로(EndPoint) · 발판(설 수 있는 콜라이더) · 탑승 존(RiderZone) 세 축이 모두
    /// 맞아야 동작한다. 하나만 빠져도 "움직이는데 안 태우는" 식으로 조용히 어긋나므로 축별로 검사한다.
    /// </summary>
    public static class MovingPlatformValidator
    {
        /// <summary>이보다 짧으면 사실상 제자리다.</summary>
        private const float MinTravelDistance = 0.05f;

        /// <summary>편도가 이보다 오래 걸리면 기다리다 지친다.</summary>
        private const float MaxSaneOneWayTime = 30f;

        public struct Issue
        {
            public MessageType Type;
            public string Message;
            public UnityEngine.Object Context;
            public string FixLabel;
            public Action Fix;
        }

        public static List<Issue> Validate(MovingPlatform platform)
        {
            List<Issue> issues = new List<Issue>();
            if (platform == null) return issues;

            ValidatePath(platform, issues);
            ValidateDeck(platform, issues);
            ValidateRiderZone(platform, issues);
            ValidateNumbers(platform, issues);
            ValidateStaticFlags(platform, issues);

            return issues;
        }

        // ─────────── 경로 ───────────

        private static void ValidatePath(MovingPlatform platform, List<Issue> issues)
        {
            Transform end = MovingPlatformAuthoring.EndPointOf(platform);

            if (end == null)
            {
                Add(issues, MessageType.Error,
                    "도착 지점이 없습니다. EndPoint 참조도 비었고 EndPoint 이름의 자식도 없어 발판이 움직이지 않습니다.",
                    platform, "계층 자동 구성", () => MovingPlatformAuthoring.EnsureHierarchy(platform));
                return;
            }

            if (MovingPlatformAuthoring.AssignedEndPoint(platform) == null)
            {
                // Awake가 이름으로 찾아 주긴 하지만, 자식 이름을 바꾸는 순간 조용히 멈춘다.
                Add(issues, MessageType.Info,
                    "End Point 참조가 비어 있어 런타임에 'EndPoint' 자식 이름으로 찾아 씁니다. 직접 꽂아 두는 편이 안전합니다.",
                    platform, "참조 연결", () =>
                    {
                        SerializedObject so = new SerializedObject(platform);
                        so.FindProperty(MovingPlatformAuthoring.P_EndPoint).objectReferenceValue = end;
                        so.ApplyModifiedProperties();
                    });
            }

            if (!end.IsChildOf(platform.transform))
            {
                Add(issues, MessageType.Warning,
                    "도착 지점이 발판의 자식이 아닙니다. 발판을 복제하면 두 발판이 같은 도착 지점을 공유합니다.",
                    end, "자식으로 옮기기",
                    () => Undo.SetTransformParent(end, platform.transform, "Reparent End Point"));
            }

            float distance = MovingPlatformAuthoring.TravelDistance(platform);
            if (distance < MinTravelDistance)
            {
                Add(issues, MessageType.Error,
                    "출발점과 도착점이 같은 위치입니다. 발판이 제자리에서 움직이지 않습니다.",
                    platform, "4m 위로 올리기",
                    () => MovingPlatformAuthoring.SetTravelHeight(
                        platform, MovingPlatformAuthoring.DefaultTravelHeight));
                return;
            }

            Vector3 delta = MovingPlatformAuthoring.EndWorld(platform) - MovingPlatformAuthoring.StartWorld(platform);
            bool vertical = Mathf.Abs(delta.y) > new Vector2(delta.x, delta.z).magnitude;

            if (vertical && MovingPlatformAuthoring.GetMode(platform) == MovingPlatform.Mode.LoopTeleport)
            {
                // 순간복귀는 라이더에게 전달되지 않는다(의도된 동작). 수직이면 탑승자만 공중에 남는다.
                Add(issues, MessageType.Warning,
                    "수직 이동인데 운행 방식이 순간복귀(LoopTeleport)입니다. 발판만 시작 지점으로 돌아가고 " +
                    "탑승자는 따라가지 않아 공중에 남습니다. 엘리베이터라면 왕복(PingPong)을 쓰세요.",
                    platform, "왕복으로 변경",
                    () => MovingPlatformAuthoring.SetMode(platform, MovingPlatform.Mode.PingPong));
            }

            float oneWay = MovingPlatformAuthoring.OneWayTime(platform);
            if (oneWay > MaxSaneOneWayTime)
            {
                Add(issues, MessageType.Info,
                    $"편도에 {oneWay:0.#}초 걸립니다(거리 {distance:0.##}m ÷ 속도 " +
                    $"{MovingPlatformAuthoring.GetFloat(platform, MovingPlatformAuthoring.P_Speed):0.##}m/s). " +
                    "대기 시간이 지나치게 길어질 수 있습니다.", platform);
            }
        }

        // ─────────── 발판 ───────────

        private static void ValidateDeck(MovingPlatform platform, List<Issue> issues)
        {
            bool standable = false;
            Collider[] colliders = platform.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].isTrigger) continue;
                standable = true;
                break;
            }

            if (!standable)
            {
                Add(issues, MessageType.Error,
                    "설 수 있는 콜라이더가 없습니다(전부 트리거이거나 아예 없음). 플레이어가 발판을 통과합니다.",
                    platform);
            }
        }

        // ─────────── 탑승 판정 존 ───────────

        private static void ValidateRiderZone(MovingPlatform platform, List<Issue> issues)
        {
            MovingPlatformRider rider = platform.GetComponentInChildren<MovingPlatformRider>(true);

            if (rider == null)
            {
                // 발판은 움직이지만 아무도 태우지 않는다 — 발판 위에서 캐릭터가 그대로 미끄러진다.
                Add(issues, MessageType.Error,
                    "탑승 판정 존(MovingPlatformRider)이 없습니다. 발판만 움직이고 그 위의 캐릭터는 따라가지 않습니다.",
                    platform, "계층 자동 구성", () => MovingPlatformAuthoring.EnsureHierarchy(platform));
                return;
            }

            UnityEngine.Object owner = MovingPlatformAuthoring.GetRef(
                rider, MovingPlatformAuthoring.P_RiderPlatform);

            if (owner != null && owner != platform)
            {
                Add(issues, MessageType.Error,
                    "탑승 판정 존의 Platform 참조가 다른 발판을 가리킵니다. 엉뚱한 발판이 탑승자를 가져갑니다.",
                    rider, "참조 연결", () => MovingPlatformAuthoring.LinkRider(rider, platform));
            }

            Collider zoneCollider = rider.GetComponent<Collider>();
            if (zoneCollider == null)
            {
                Add(issues, MessageType.Error,
                    "탑승 판정 존에 콜라이더가 없어 탑승을 감지하지 못합니다.", rider,
                    "BoxCollider 추가", () =>
                    {
                        BoxCollider added = Undo.AddComponent<BoxCollider>(rider.gameObject);
                        added.isTrigger = true;
                        MovingPlatformAuthoring.FitRiderZone(platform);
                    });
                return;
            }

            if (!zoneCollider.isTrigger)
            {
                Add(issues, MessageType.Error,
                    "탑승 판정 존의 Is Trigger가 꺼져 있습니다. 탑승을 감지하지 못하고 플레이어를 밀어냅니다.",
                    rider, "Is Trigger 켜기", () =>
                    {
                        Undo.RecordObject(zoneCollider, "Set Is Trigger");
                        zoneCollider.isTrigger = true;
                        EditorUtility.SetDirty(zoneCollider);
                    });
            }

            ValidateZoneCoverage(platform, rider, zoneCollider, issues);
        }

        /// <summary>존이 발판 윗면을 덮는지. 위로 뜨거나 안쪽으로 작으면 서 있어도 라이더로 잡히지 않는다.</summary>
        private static void ValidateZoneCoverage(MovingPlatform platform, MovingPlatformRider rider,
                                                 Collider zoneCollider, List<Issue> issues)
        {
            if (!MovingPlatformAuthoring.TryGetDeckBounds(platform, out Bounds deck)) return;

            Bounds zone = zoneCollider.bounds;
            float top = deck.max.y;

            bool coversTop = zone.min.y <= top + 0.02f && zone.max.y > top;
            bool coversFootprint =
                zone.size.x >= deck.size.x * 0.6f &&
                zone.size.z >= deck.size.z * 0.6f &&
                Mathf.Abs(zone.center.x - deck.center.x) < deck.size.x * 0.5f &&
                Mathf.Abs(zone.center.z - deck.center.z) < deck.size.z * 0.5f;

            if (coversTop && coversFootprint) return;

            string reason = !coversTop
                ? "발판 윗면을 감싸지 못합니다(존이 발판 속에 묻혔거나 위로 떠 있습니다)"
                : "발판 넓이를 충분히 덮지 못합니다";

            Add(issues, MessageType.Warning,
                $"탑승 판정 존이 {reason}. 발판 위에 서 있어도 탑승자로 등록되지 않을 수 있습니다.",
                rider, "존 크기 맞추기", () => MovingPlatformAuthoring.FitRiderZone(platform));
        }

        // ─────────── 수치 ───────────

        private static void ValidateNumbers(MovingPlatform platform, List<Issue> issues)
        {
            float speed = MovingPlatformAuthoring.GetFloat(platform, MovingPlatformAuthoring.P_Speed);
            if (speed <= 0.0001f)
            {
                Add(issues, MessageType.Error, "속도가 0입니다. 발판이 움직이지 않습니다.", platform,
                    "속도 2로", () => MovingPlatformAuthoring.SetFloat(
                        platform, MovingPlatformAuthoring.P_Speed, 2f, "Set Platform Speed"));
            }

            MovingPlatform.Mode mode = MovingPlatformAuthoring.GetMode(platform);
            float wait = MovingPlatformAuthoring.GetFloat(platform, MovingPlatformAuthoring.P_WaitTime);

            if (mode == MovingPlatform.Mode.OneWayOnTouch && wait > 0.0001f)
            {
                Add(issues, MessageType.Info,
                    $"편도 타입에는 대기 시간({wait:0.##}초)이 적용되지 않습니다. 출발 지연은 Start Delay를 쓰세요.",
                    platform, "대기 0으로", () => MovingPlatformAuthoring.SetFloat(
                        platform, MovingPlatformAuthoring.P_WaitTime, 0f, "Clear Platform Wait"));
            }
        }

        // ─────────── 정적 플래그 ───────────

        private static void ValidateStaticFlags(MovingPlatform platform, List<Issue> issues)
        {
            // Batching Static은 메시를 월드에 구워 버려 Transform을 움직여도 화면이 그대로다.
            Transform[] all = platform.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i].gameObject;
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
                if ((flags & StaticEditorFlags.BatchingStatic) == 0) continue;

                GameObject captured = go;
                StaticEditorFlags capturedFlags = flags;
                Add(issues, MessageType.Error,
                    $"'{go.name}'에 Batching Static이 켜져 있습니다. 재생 중 발판이 제자리에 굳어 보입니다.",
                    go, "Static 끄기", () =>
                    {
                        Undo.RecordObject(captured, "Clear Batching Static");
                        GameObjectUtility.SetStaticEditorFlags(
                            captured, capturedFlags & ~StaticEditorFlags.BatchingStatic);
                        EditorUtility.SetDirty(captured);
                    });
                return; // 한 번만 보고하면 충분하다.
            }
        }

        private static void Add(List<Issue> list, MessageType type, string message, UnityEngine.Object context,
                                string fixLabel = null, Action fix = null)
        {
            list.Add(new Issue
            {
                Type = type,
                Message = message,
                Context = context,
                FixLabel = fixLabel,
                Fix = fix
            });
        }
    }
}
