using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 사다리 저작(Authoring) 헬퍼. LadderToolWindow와 커스텀 인스펙터가 공유하는
    /// 계층 생성·참조 복원·바닥 맞춤 로직을 모아둔 에디터 전용 유틸리티.
    ///
    /// LadderTraversable / LadderZone의 직렬화 필드는 private이므로 런타임 코드를 건드리지 않고
    /// SerializedObject 경유로 읽고 쓴다(= 아래 프로퍼티 경로 상수가 이 파일의 계약).
    /// </summary>
    public static class LadderAuthoring
    {
        // ─────────── 직렬화 프로퍼티 경로 (런타임 필드명과 1:1) ───────────

        public const string P_Height = "height";
        public const string P_ClimbSpeed = "climbSpeed";
        public const string P_StepDistance = "climbStepDistance";
        public const string P_StepSpeed = "climbStepSpeed";
        public const string P_FaceRight = "faceRight";
        public const string P_TopExitOffset = "topExitOffset";
        public const string P_BottomExitOffset = "bottomExitOffset";
        public const string P_ClimbOffset = "climbOffset";
        public const string P_Width = "width";
        public const string P_Depth = "depth";
        public const string P_ZoneHeight = "zoneHeight";

        public const string P_ClimbVolume = "climbVolume";
        public const string P_BottomZone = "bottomZone";
        public const string P_TopZone = "topZone";
        public const string P_TopExitPoint = "topExitPoint";
        public const string P_BottomExitPoint = "bottomExitPoint";

        /// <summary>LadderZone.kind — 0=Bottom, 1=Top.</summary>
        public const string P_ZoneKind = "kind";
        public const string P_ZoneLadder = "ladder";

        // ─────────── 계층 규약 (LadderTraversable.ResolveRefs가 찾는 이름) ───────────

        public const string ClimbVolumeName = "ClimbVolume";
        public const string BottomZoneName = "BottomZone";
        public const string TopZoneName = "TopZone";
        public const string TopExitPointName = "TopExitPoint";
        public const string BottomExitPointName = "BottomExitPoint";
        public const string VisualName = "Visual";

        /// <summary>Ladder.prefab.</summary>
        public const string LadderPrefabGuid = "adb8d1c0b0dd55f4a911c9cfb836cb09";

        /// <summary>등반 클립이 한 칸 이동을 요청할 때 쓰는 애니메이션 이벤트 이름.</summary>
        public const string ClimbUpEvent = "climbingUp";
        public const string ClimbDownEvent = "climbingDown";

        /// <summary>사다리 등반이 성립하려면 컨트롤러에 있어야 하는 파라미터(AnimParams와 1:1).</summary>
        public static readonly (string Name, AnimatorControllerParameterType Type)[] RequiredAnimParams =
        {
            (AnimParams.OnLadder,         AnimatorControllerParameterType.Bool),
            (AnimParams.LadderClimbSpeed, AnimatorControllerParameterType.Float),
            (AnimParams.ClimbEnterBottom, AnimatorControllerParameterType.Trigger),
            (AnimParams.ClimbEnterTop,    AnimatorControllerParameterType.Trigger),
            (AnimParams.ClimbGoUp,        AnimatorControllerParameterType.Trigger),
            (AnimParams.ClimbGoDown,      AnimatorControllerParameterType.Trigger),
            (AnimParams.ClimbExitTop,     AnimatorControllerParameterType.Trigger),
            (AnimParams.ClimbExitBottom,  AnimatorControllerParameterType.Trigger),
        };

        // ─────────── 생성 ───────────

        /// <summary>Ladder.prefab을 씬에 배치한다. 프리팹을 못 찾으면 계층을 직접 조립한다.</summary>
        public static LadderTraversable CreateLadder(Vector3 worldPosition, Transform parent = null)
        {
            string path = AssetDatabase.GUIDToAssetPath(LadderPrefabGuid);
            GameObject prefab = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(path);

            GameObject root;
            if (prefab != null)
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                Undo.RegisterCreatedObjectUndo(root, "Create Ladder");
            }
            else
            {
                root = new GameObject(NextLadderName());
                Undo.RegisterCreatedObjectUndo(root, "Create Ladder");
                if (parent != null) Undo.SetTransformParent(root.transform, parent, "Create Ladder");
                Undo.AddComponent<LadderTraversable>(root);
            }

            root.transform.position = worldPosition;

            LadderTraversable ladder = root.GetComponent<LadderTraversable>();
            if (ladder != null) EnsureHierarchy(ladder);

            Selection.activeGameObject = root;
            return ladder;
        }

        private static string NextLadderName()
        {
            int max = 0;
            LadderTraversable[] all = Object.FindObjectsByType<LadderTraversable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].gameObject.name;
                int us = n.LastIndexOf('_');
                if (us >= 0 && int.TryParse(n.Substring(us + 1), out int v) && v > max) max = v;
            }
            return "Ladder_" + (max + 1);
        }

        /// <summary>
        /// 빠진 자식(ClimbVolume/BottomZone/TopZone/Exit 포인트)을 만들고, 콜라이더·kind·역참조를
        /// 규약대로 맞춘 뒤 LadderTraversable의 참조 필드까지 연결한다. 이미 맞는 것은 건드리지 않는다.
        /// </summary>
        public static void EnsureHierarchy(LadderTraversable ladder)
        {
            if (ladder == null) return;
            Transform t = ladder.transform;

            Transform climb = EnsureChild(t, ClimbVolumeName);
            BoxCollider climbBox = EnsureBoxTrigger(climb.gameObject);

            Transform bottom = EnsureChild(t, BottomZoneName);
            EnsureBoxTrigger(bottom.gameObject);
            LadderZone bottomZone = EnsureZone(bottom.gameObject, ladder, atTop: false);

            Transform top = EnsureChild(t, TopZoneName);
            EnsureBoxTrigger(top.gameObject);
            LadderZone topZone = EnsureZone(top.gameObject, ladder, atTop: true);

            Transform topExit = EnsureChild(t, TopExitPointName);
            Transform bottomExit = EnsureChild(t, BottomExitPointName);

            SerializedObject so = new SerializedObject(ladder);
            so.FindProperty(P_ClimbVolume).objectReferenceValue = climbBox;
            so.FindProperty(P_BottomZone).objectReferenceValue = bottomZone;
            so.FindProperty(P_TopZone).objectReferenceValue = topZone;
            so.FindProperty(P_TopExitPoint).objectReferenceValue = topExit;
            so.FindProperty(P_BottomExitPoint).objectReferenceValue = bottomExit;
            so.ApplyModifiedProperties();
        }

        /// <summary>이름이 같은 자식을 찾고, 없으면 만들어 반환한다.</summary>
        public static Transform EnsureChild(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null) return found;

            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            Undo.SetTransformParent(go.transform, parent, "Create " + name);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
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

        private static LadderZone EnsureZone(GameObject go, LadderTraversable ladder, bool atTop)
        {
            LadderZone zone = go.GetComponent<LadderZone>();
            if (zone == null) zone = Undo.AddComponent<LadderZone>(go);

            SerializedObject so = new SerializedObject(zone);
            so.FindProperty(P_ZoneKind).enumValueIndex = atTop ? 1 : 0;
            so.FindProperty(P_ZoneLadder).objectReferenceValue = ladder;
            so.ApplyModifiedProperties();
            return zone;
        }

        // ─────────── 조회 / 수정 ───────────

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

        public static Vector3 GetVector3(Object target, string prop)
        {
            if (target == null) return Vector3.zero;
            return new SerializedObject(target).FindProperty(prop).vector3Value;
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

        /// <summary>한 칸(StepDistance) 오르는 데 걸리는 시간(s). 등반 애니 사이클 길이와 맞춰야 한다.</summary>
        public static float StepDuration(LadderTraversable ladder)
        {
            float speed = GetFloat(ladder, P_StepSpeed);
            if (speed <= 0.0001f) return float.PositiveInfinity;
            return GetFloat(ladder, P_StepDistance) / speed;
        }

        // ─────────── 배치 보조 ───────────

        /// <summary>사다리 밑동을 바로 아래 바닥에 붙인다. 아래에 콜라이더가 없으면 false.</summary>
        public static bool SnapToGround(LadderTraversable ladder, float probe = 20f)
        {
            if (ladder == null) return false;

            Vector3 origin = ladder.transform.position + Vector3.up * 0.5f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probe + 0.5f,
                                 ~0, QueryTriggerInteraction.Ignore))
                return false;

            Undo.RecordObject(ladder.transform, "Snap Ladder To Ground");
            ladder.transform.position = hit.point;
            return true;
        }

        /// <summary>Visual 자식의 렌더러 높이에 맞춰 Height를 채운다. Visual이 없으면 false.</summary>
        public static bool FitHeightToVisual(LadderTraversable ladder)
        {
            if (ladder == null) return false;

            Transform visual = ladder.transform.Find(VisualName);
            if (visual == null) return false;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            float scaleY = Mathf.Abs(ladder.transform.lossyScale.y);
            if (scaleY < 0.0001f) return false;

            SetFloat(ladder, P_Height, b.size.y / scaleY, "Fit Ladder Height");
            return true;
        }

        /// <summary>상단 탈출 지점 아래에 설 바닥이 있는지. 없으면 올라서자마자 떨어진다.</summary>
        public static bool HasFloorUnderTopExit(LadderTraversable ladder, float probe = 3f)
        {
            if (ladder == null) return false;
            Vector3 origin = ladder.TopExitWorld + Vector3.up * 0.1f;
            return Physics.Raycast(origin, Vector3.down, probe, ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
