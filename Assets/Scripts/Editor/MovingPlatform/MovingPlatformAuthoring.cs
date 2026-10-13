using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 엘리베이터(MovingPlatform) 저작(Authoring) 헬퍼. MovingPlatformToolWindow와 커스텀 인스펙터가
    /// 공유하는 계층 생성·참조 복원·경로 계산 로직을 모아둔 에디터 전용 유틸리티.
    ///
    /// MovingPlatform / MovingPlatformRider의 직렬화 필드는 private이므로 런타임 코드를 건드리지 않고
    /// SerializedObject 경유로 읽고 쓴다(= 아래 프로퍼티 경로 상수가 이 파일의 계약).
    /// </summary>
    public static class MovingPlatformAuthoring
    {
        // ─────────── 직렬화 프로퍼티 경로 (런타임 필드명과 1:1) ───────────

        public const string P_Mode = "mode";
        public const string P_EndPoint = "endPoint";
        public const string P_Speed = "speed";
        public const string P_WaitTime = "waitTime";
        public const string P_StartDelay = "startDelay";
        public const string P_Ease = "ease";

        /// <summary>MovingPlatformRider.platform — 이 존이 태워 보낼 플랫폼.</summary>
        public const string P_RiderPlatform = "platform";

        // ─────────── 계층 규약 (MovingPlatform.Awake가 찾는 이름) ───────────

        /// <summary>Awake는 endPoint 참조가 비면 이 이름의 자식을 찾아 대체한다.</summary>
        public const string EndPointName = "EndPoint";
        public const string RiderZoneName = "RiderZone";
        public const string DeckName = "PlatformMesh";

        /// <summary>MovingPlatform.prefab.</summary>
        public const string PlatformPrefabGuid = "1cc8edd0c298a4a4ca0f2a4a70ecbd11";

        /// <summary>새로 만든 도착 지점의 기본 높이(m). 0이면 제자리라 움직이지 않으므로 띄워 둔다.</summary>
        public const float DefaultTravelHeight = 4f;

        /// <summary>탑승 판정 존의 두께(m). 발판 윗면을 조금 파고들어야 서 있는 캐릭터가 안에 들어온다.</summary>
        public const float RiderZoneHeight = 0.6f;

        /// <summary>존이 발판 윗면 아래로 파고드는 깊이(m).</summary>
        public const float RiderZoneSink = 0.1f;

        /// <summary>자주 쓰는 운행 세팅 묶음. 모드·속도·대기·경로 방향을 한 번에 맞춘다.</summary>
        public enum Preset
        {
            /// <summary>수직 왕복 엘리베이터. 부드럽게 오르내린다.</summary>
            Elevator,
            /// <summary>밟으면 한 번만 올라가는 편도 승강기.</summary>
            OneWayLift,
            /// <summary>수평 왕복 이동 발판.</summary>
            ShuttlePlatform,
            /// <summary>수평 편도 후 시작점으로 순간복귀하는 컨베이어형.</summary>
            ConveyorLoop,
        }

        // ─────────── 생성 ───────────

        /// <summary>MovingPlatform.prefab을 씬에 배치한다. 프리팹을 못 찾으면 계층을 직접 조립한다.</summary>
        public static MovingPlatform CreatePlatform(Vector3 worldPosition, Transform parent = null)
        {
            string path = AssetDatabase.GUIDToAssetPath(PlatformPrefabGuid);
            GameObject prefab = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(path);

            GameObject root;
            if (prefab != null)
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                Undo.RegisterCreatedObjectUndo(root, "Create Moving Platform");
            }
            else
            {
                root = new GameObject(NextPlatformName());
                Undo.RegisterCreatedObjectUndo(root, "Create Moving Platform");
                if (parent != null) Undo.SetTransformParent(root.transform, parent, "Create Moving Platform");
                Undo.AddComponent<MovingPlatform>(root);
            }

            root.transform.position = worldPosition;

            MovingPlatform platform = root.GetComponent<MovingPlatform>();
            if (platform != null) EnsureHierarchy(platform);

            Selection.activeGameObject = root;
            return platform;
        }

        private static string NextPlatformName()
        {
            int max = 0;
            MovingPlatform[] all = Object.FindObjectsByType<MovingPlatform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].gameObject.name;
                int us = n.LastIndexOf('_');
                if (us >= 0 && int.TryParse(n.Substring(us + 1), out int v) && v > max) max = v;
            }
            return "MovingPlatform_" + (max + 1);
        }

        /// <summary>
        /// 빠진 자식(EndPoint/RiderZone)을 만들고, 트리거 콜라이더·역참조를 규약대로 맞춘 뒤
        /// MovingPlatform의 endPoint 참조까지 연결한다. 이미 맞는 것은 건드리지 않는다.
        /// </summary>
        public static void EnsureHierarchy(MovingPlatform platform)
        {
            if (platform == null) return;

            Transform end = EnsureEndPoint(platform);
            EnsureRiderZone(platform, out bool zoneCreated);

            SerializedObject so = new SerializedObject(platform);
            so.FindProperty(P_EndPoint).objectReferenceValue = end;
            so.ApplyModifiedProperties();

            // 방금 만든 존만 발판에 맞춘다. 손으로 조정해 둔 존을 덮어쓰지 않기 위해서다.
            if (zoneCreated) FitRiderZone(platform);
        }

        /// <summary>도착 지점을 찾고, 없으면 기본 높이만큼 위에 만들어 반환한다.</summary>
        public static Transform EnsureEndPoint(MovingPlatform platform)
        {
            Transform existing = EndPointOf(platform);
            if (existing != null) return existing;

            GameObject go = new GameObject(EndPointName);
            Undo.RegisterCreatedObjectUndo(go, "Create End Point");
            Undo.SetTransformParent(go.transform, platform.transform, "Create End Point");
            go.transform.localPosition = Vector3.up * DefaultTravelHeight;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        /// <summary>탑승 판정 존(트리거 + MovingPlatformRider)을 보장한다.</summary>
        public static MovingPlatformRider EnsureRiderZone(MovingPlatform platform)
        {
            return EnsureRiderZone(platform, out _);
        }

        /// <summary>
        /// 탑승 판정 존을 보장한다. created는 존이나 그 콜라이더를 이번에 새로 만들었는지 —
        /// 즉 크기를 발판에 맞춰 줘야 하는지를 알려준다.
        /// </summary>
        public static MovingPlatformRider EnsureRiderZone(MovingPlatform platform, out bool created)
        {
            created = false;
            MovingPlatformRider rider = platform.GetComponentInChildren<MovingPlatformRider>(true);

            if (rider == null)
            {
                created = true;
                Transform zone = platform.transform.Find(RiderZoneName);
                if (zone == null)
                {
                    GameObject go = new GameObject(RiderZoneName);
                    Undo.RegisterCreatedObjectUndo(go, "Create Rider Zone");
                    Undo.SetTransformParent(go.transform, platform.transform, "Create Rider Zone");
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    zone = go.transform;
                }

                rider = zone.GetComponent<MovingPlatformRider>();
                if (rider == null) rider = Undo.AddComponent<MovingPlatformRider>(zone.gameObject);
            }

            if (rider.GetComponent<BoxCollider>() == null) created = true;
            EnsureBoxTrigger(rider.gameObject);
            LinkRider(rider, platform);
            return rider;
        }

        /// <summary>존의 platform 참조를 이 플랫폼으로 맞춘다.</summary>
        public static void LinkRider(MovingPlatformRider rider, MovingPlatform platform)
        {
            if (rider == null) return;
            SerializedObject so = new SerializedObject(rider);
            so.FindProperty(P_RiderPlatform).objectReferenceValue = platform;
            so.ApplyModifiedProperties();
        }

        private static BoxCollider EnsureBoxTrigger(GameObject go)
        {
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(go);
            if (!box.isTrigger)
            {
                Undo.RecordObject(box, "Set Is Trigger");
                box.isTrigger = true;
                EditorUtility.SetDirty(box);
            }
            return box;
        }

        // ─────────── 조회 ───────────

        /// <summary>런타임 Awake와 같은 규칙: 참조가 비었으면 EndPoint 이름의 자식으로 대체한다.</summary>
        public static Transform EndPointOf(MovingPlatform platform)
        {
            if (platform == null) return null;

            Transform assigned = GetRef(platform, P_EndPoint) as Transform;
            if (assigned != null) return assigned;

            return platform.transform.Find(EndPointName);
        }

        /// <summary>endPoint 필드에 실제로 꽂혀 있는 참조(이름 대체 없이).</summary>
        public static Transform AssignedEndPoint(MovingPlatform platform)
        {
            return GetRef(platform, P_EndPoint) as Transform;
        }

        /// <summary>출발 지점. 재생 중에는 내부 캐시를 볼 수 없어 현재 위치를 돌려준다.</summary>
        public static Vector3 StartWorld(MovingPlatform platform)
        {
            return platform != null ? platform.transform.position : Vector3.zero;
        }

        public static Vector3 EndWorld(MovingPlatform platform)
        {
            Transform end = EndPointOf(platform);
            return end != null ? end.position : StartWorld(platform);
        }

        public static float TravelDistance(MovingPlatform platform)
        {
            return Vector3.Distance(StartWorld(platform), EndWorld(platform));
        }

        public static MovingPlatform.Mode GetMode(MovingPlatform platform)
        {
            if (platform == null) return MovingPlatform.Mode.PingPong;
            return (MovingPlatform.Mode)new SerializedObject(platform).FindProperty(P_Mode).enumValueIndex;
        }

        public static string ModeLabel(MovingPlatform.Mode mode)
        {
            switch (mode)
            {
                case MovingPlatform.Mode.PingPong: return "왕복";
                case MovingPlatform.Mode.LoopTeleport: return "편도 후 순간복귀";
                default: return "밟으면 편도";
            }
        }

        /// <summary>편도 이동 시간(s). 감속(ease)을 켜면 평균 속도가 낮아지므로 근사치다.</summary>
        public static float OneWayTime(MovingPlatform platform)
        {
            float speed = GetFloat(platform, P_Speed);
            if (speed <= 0.0001f) return float.PositiveInfinity;
            return TravelDistance(platform) / speed;
        }

        /// <summary>한 주기(같은 상태로 돌아오기까지) 시간(s). 편도 타입은 완주 시간.</summary>
        public static float CycleTime(MovingPlatform platform)
        {
            float one = OneWayTime(platform);
            float wait = GetFloat(platform, P_WaitTime);

            switch (GetMode(platform))
            {
                case MovingPlatform.Mode.PingPong: return (one + wait) * 2f;
                case MovingPlatform.Mode.LoopTeleport: return one + wait;
                default: return one;
            }
        }

        public static float GetFloat(Object target, string prop)
        {
            if (target == null) return 0f;
            return new SerializedObject(target).FindProperty(prop).floatValue;
        }

        public static bool GetBool(Object target, string prop)
        {
            if (target == null) return false;
            return new SerializedObject(target).FindProperty(prop).boolValue;
        }

        public static Object GetRef(Object target, string prop)
        {
            if (target == null) return null;
            return new SerializedObject(target).FindProperty(prop).objectReferenceValue;
        }

        public static void SetFloat(Object target, string prop, float value, string undoName)
        {
            if (target == null) return;
            Undo.RecordObject(target, undoName);
            SerializedObject so = new SerializedObject(target);
            so.FindProperty(prop).floatValue = value;
            so.ApplyModifiedProperties();
        }

        public static void SetBool(Object target, string prop, bool value, string undoName)
        {
            if (target == null) return;
            Undo.RecordObject(target, undoName);
            SerializedObject so = new SerializedObject(target);
            so.FindProperty(prop).boolValue = value;
            so.ApplyModifiedProperties();
        }

        public static void SetMode(MovingPlatform platform, MovingPlatform.Mode mode)
        {
            if (platform == null) return;
            Undo.RecordObject(platform, "Change Platform Mode");
            SerializedObject so = new SerializedObject(platform);
            so.FindProperty(P_Mode).enumValueIndex = (int)mode;
            so.ApplyModifiedProperties();
        }

        // ─────────── 경로 편집 ───────────

        public static void MoveEndPoint(MovingPlatform platform, Vector3 worldPosition, string undoName)
        {
            Transform end = EndPointOf(platform);
            if (end == null) return;

            Undo.RecordObject(end, undoName);
            end.position = worldPosition;
        }

        /// <summary>도착 지점의 X/Z를 출발점에 맞춰 순수 수직 이동(엘리베이터)으로 만든다.</summary>
        public static void AlignVertical(MovingPlatform platform)
        {
            Vector3 start = StartWorld(platform);
            Vector3 end = EndWorld(platform);

            float height = Mathf.Abs(end.y - start.y) < 0.01f
                ? DefaultTravelHeight
                : end.y - start.y;

            MoveEndPoint(platform, start + Vector3.up * height, "Align Platform Vertical");
        }

        /// <summary>도착 지점의 Y를 출발점에 맞춰 수평 이동 발판으로 만든다.</summary>
        public static void AlignHorizontal(MovingPlatform platform)
        {
            Vector3 start = StartWorld(platform);
            Vector3 end = EndWorld(platform);

            Vector3 flat = new Vector3(end.x, start.y, end.z);
            if ((flat - start).sqrMagnitude < 0.0001f)
                flat = start + platform.transform.right * DefaultTravelHeight;

            MoveEndPoint(platform, flat, "Align Platform Horizontal");
        }

        /// <summary>수직 이동 높이를 직접 지정한다(부호로 상승/하강).</summary>
        public static void SetTravelHeight(MovingPlatform platform, float height)
        {
            Vector3 start = StartWorld(platform);
            Vector3 end = EndWorld(platform);
            MoveEndPoint(platform, new Vector3(end.x, start.y + height, end.z), "Set Platform Travel Height");
        }

        /// <summary>출발점과 도착점을 맞바꾼다. 위층에서 시작하는 엘리베이터로 뒤집을 때 쓴다.</summary>
        public static void SwapEnds(MovingPlatform platform)
        {
            Transform end = EndPointOf(platform);
            if (end == null) return;

            Vector3 start = StartWorld(platform);
            Vector3 goal = end.position;
            if ((goal - start).sqrMagnitude < 0.0001f) return;

            Undo.RecordObject(platform.transform, "Swap Platform Ends");
            Undo.RecordObject(end, "Swap Platform Ends");

            platform.transform.position = goal;
            end.position = start; // 도착점이 자식이면 부모를 옮긴 뒤라 월드 좌표로 다시 지정해야 한다.
        }

        /// <summary>운행 세팅 묶음을 적용한다. 경로 방향도 프리셋에 맞춰 정리한다.</summary>
        public static void ApplyPreset(MovingPlatform platform, Preset preset)
        {
            if (platform == null) return;
            EnsureHierarchy(platform);

            Undo.RecordObject(platform, "Apply Platform Preset");
            SerializedObject so = new SerializedObject(platform);

            switch (preset)
            {
                case Preset.Elevator:
                    so.FindProperty(P_Mode).enumValueIndex = (int)MovingPlatform.Mode.PingPong;
                    so.FindProperty(P_Speed).floatValue = 2f;
                    so.FindProperty(P_WaitTime).floatValue = 1.5f;
                    so.FindProperty(P_StartDelay).floatValue = 0f;
                    so.FindProperty(P_Ease).boolValue = true;
                    break;

                case Preset.OneWayLift:
                    so.FindProperty(P_Mode).enumValueIndex = (int)MovingPlatform.Mode.OneWayOnTouch;
                    so.FindProperty(P_Speed).floatValue = 2f;
                    so.FindProperty(P_WaitTime).floatValue = 0f;
                    so.FindProperty(P_StartDelay).floatValue = 0.4f;
                    so.FindProperty(P_Ease).boolValue = true;
                    break;

                case Preset.ShuttlePlatform:
                    so.FindProperty(P_Mode).enumValueIndex = (int)MovingPlatform.Mode.PingPong;
                    so.FindProperty(P_Speed).floatValue = 2.5f;
                    so.FindProperty(P_WaitTime).floatValue = 0.5f;
                    so.FindProperty(P_StartDelay).floatValue = 0f;
                    so.FindProperty(P_Ease).boolValue = false;
                    break;

                case Preset.ConveyorLoop:
                    so.FindProperty(P_Mode).enumValueIndex = (int)MovingPlatform.Mode.LoopTeleport;
                    so.FindProperty(P_Speed).floatValue = 2f;
                    so.FindProperty(P_WaitTime).floatValue = 0.3f;
                    so.FindProperty(P_StartDelay).floatValue = 0f;
                    so.FindProperty(P_Ease).boolValue = false;
                    break;
            }

            so.ApplyModifiedProperties();

            if (preset == Preset.Elevator || preset == Preset.OneWayLift) AlignVertical(platform);
            else AlignHorizontal(platform);
        }

        public static string PresetLabel(Preset preset)
        {
            switch (preset)
            {
                case Preset.Elevator: return "엘리베이터 (수직 왕복)";
                case Preset.OneWayLift: return "편도 승강기 (밟으면 출발)";
                case Preset.ShuttlePlatform: return "이동 발판 (수평 왕복)";
                default: return "컨베이어 (수평 순간복귀)";
            }
        }

        // ─────────── 배치 보조 ───────────

        /// <summary>발판(비트리거 콜라이더) 전체를 감싸는 월드 AABB. 없으면 렌더러로 대체.</summary>
        public static bool TryGetDeckBounds(MovingPlatform platform, out Bounds bounds)
        {
            bounds = new Bounds();
            if (platform == null) return false;

            bool has = false;
            Collider[] colliders = platform.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].isTrigger) continue;
                if (!has) { bounds = colliders[i].bounds; has = true; }
                else bounds.Encapsulate(colliders[i].bounds);
            }
            if (has) return true;

            Renderer[] renderers = platform.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!has) { bounds = renderers[i].bounds; has = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            return has;
        }

        /// <summary>탑승 판정 존을 발판 윗면에 맞춰 덮는다. 발판이나 존을 못 찾으면 false.</summary>
        public static bool FitRiderZone(MovingPlatform platform)
        {
            MovingPlatformRider rider = platform != null
                ? platform.GetComponentInChildren<MovingPlatformRider>(true)
                : null;
            if (rider == null) return false;

            BoxCollider box = rider.GetComponent<BoxCollider>();
            if (box == null) return false;
            if (!TryGetDeckBounds(platform, out Bounds deck)) return false;

            Transform t = box.transform;
            Vector3 scale = t.lossyScale;
            if (Mathf.Abs(scale.x) < 0.0001f || Mathf.Abs(scale.y) < 0.0001f || Mathf.Abs(scale.z) < 0.0001f)
                return false;

            // 발판 윗면을 살짝 파고들어 덮는다. 위로는 서 있는 캐릭터의 발이 들어올 만큼만.
            float top = deck.max.y;
            Vector3 centerWorld = new Vector3(
                deck.center.x,
                top - RiderZoneSink + RiderZoneHeight * 0.5f,
                deck.center.z);

            Undo.RecordObject(box, "Fit Rider Zone");
            box.center = t.InverseTransformPoint(centerWorld);
            box.size = new Vector3(
                deck.size.x / Mathf.Abs(scale.x),
                RiderZoneHeight / Mathf.Abs(scale.y),
                deck.size.z / Mathf.Abs(scale.z));
            EditorUtility.SetDirty(box);
            return true;
        }

        /// <summary>발판 밑면을 바로 아래 바닥에 붙인다. 아래에 콜라이더가 없으면 false.</summary>
        public static bool SnapToGround(MovingPlatform platform, float probe = 20f)
        {
            if (platform == null) return false;

            Vector3 origin = platform.transform.position + Vector3.up * 0.5f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probe + 0.5f,
                                 ~0, QueryTriggerInteraction.Ignore))
                return false;

            // 피벗이 발판 한가운데면 그대로 내리면 바닥에 박힌다. 밑면까지의 거리만큼 들어 올린다.
            float lift = 0f;
            if (TryGetDeckBounds(platform, out Bounds deck))
                lift = platform.transform.position.y - deck.min.y;

            Undo.RecordObject(platform.transform, "Snap Platform To Ground");
            platform.transform.position = hit.point + Vector3.up * lift;
            return true;
        }
    }
}
