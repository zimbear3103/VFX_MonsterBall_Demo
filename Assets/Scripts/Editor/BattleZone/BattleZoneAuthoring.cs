using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 배틀존 저작(Authoring) 헬퍼. BattleZoneToolWindow와 커스텀 인스펙터가 공유하는
    /// 계층 생성·웨이브 편집·벽 자동 맞춤 로직을 모아둔 에디터 전용 유틸리티.
    ///
    /// BattleZone / EnemySpawner의 직렬화 필드는 private이므로 런타임 코드를 건드리지 않고
    /// SerializedObject 경유로 읽고 쓴다(= 아래 프로퍼티 경로 상수가 이 파일의 계약).
    /// </summary>
    public static class BattleZoneAuthoring
    {
        // ─────────── 직렬화 프로퍼티 경로 (런타임 필드명과 1:1) ───────────

        public const string P_Waves = "waves";
        public const string P_BattleWalls = "battleWalls";
        public const string P_TriggerOnce = "triggerOnce";

        public const string P_SpawnEntries = "spawnEntries";
        public const string P_Preplaced = "preplacedEnemies";
        public const string P_WakePreplaced = "wakePreplacedOnSpawn";
        public const string P_SpawnOnce = "spawnOnce";

        public const string P_EntryPrefab = "prefab";
        public const string P_EntryPoint = "spawnPoint";
        public const string P_EntryDelay = "delay";
        public const string P_EntryRelative = "spawnRelativeToPlayer";
        public const string P_EntryDistance = "spawnDistanceOverride";
        public const string P_EntrySide = "spawnSide";

        // ─────────── 계층 규약 ───────────

        public const string WallsRootName = "Walls";
        public const string SpawnPointsRootName = "SpawnPoints";
        public const string PreplacedRootName = "Preplaced";
        public const string WavePrefix = "Wave_";

        /// <summary>Wall_barrier_Left.prefab (차단 축 음(-)쪽 벽).</summary>
        public const string WallLeftGuid = "80f568f05652e8b48a71f8f6b28f0551";

        /// <summary>Wall_barrier_Right.prefab (차단 축 양(+)쪽 벽).</summary>
        public const string WallRightGuid = "a5fdc88c3eeead14b99118c9520b481d";

        /// <summary>벽으로 막을 통로 축(존 로컬 기준).</summary>
        public enum BlockAxis { X, Z }

        /// <summary>존을 새로 만들 때 쓰는 기본 트리거 크기.</summary>
        public static readonly Vector3 DefaultZoneSize = new Vector3(16f, 8f, 24f);


        // ─────────── 생성 ───────────

        /// <summary>
        /// 배틀존 계층 일습을 생성한다.
        /// BattleZone_N (BoxCollider trigger + BattleZone) / Walls / SpawnPoints / Wave_1(EnemySpawner)
        /// </summary>
        public static BattleZone CreateZone(Vector3 worldPosition, Transform parent = null)
        {
            GameObject root = new GameObject(NextZoneName());
            Undo.RegisterCreatedObjectUndo(root, "Create Battle Zone");

            if (parent != null) Undo.SetTransformParent(root.transform, parent, "Create Battle Zone");
            root.transform.position = worldPosition;

            BoxCollider box = Undo.AddComponent<BoxCollider>(root);
            box.isTrigger = true;
            box.size = DefaultZoneSize;
            box.center = new Vector3(0f, DefaultZoneSize.y * 0.5f, 0f);

            BattleZone zone = Undo.AddComponent<BattleZone>(root);

            Transform walls = EnsureChild(root.transform, WallsRootName);
            EnsureChild(root.transform, SpawnPointsRootName);

            SerializedObject so = new SerializedObject(zone);
            so.FindProperty(P_BattleWalls).objectReferenceValue = walls.gameObject;
            so.ApplyModifiedProperties();

            AddWave(zone);
            Selection.activeGameObject = root;
            return zone;
        }

        private static string NextZoneName()
        {
            int max = 0;
            BattleZone[] zones = Object.FindObjectsByType<BattleZone>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < zones.Length; i++)
            {
                string n = zones[i].gameObject.name;
                int us = n.LastIndexOf('_');
                if (us >= 0 && int.TryParse(n.Substring(us + 1), out int v) && v > max) max = v;
            }
            return "BattleZone_" + (max + 1);
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


        // ─────────── 웨이브 ───────────

        public static List<EnemySpawner> GetWaves(BattleZone zone)
        {
            List<EnemySpawner> list = new List<EnemySpawner>();
            if (zone == null) return list;

            SerializedObject so = new SerializedObject(zone);
            SerializedProperty waves = so.FindProperty(P_Waves);
            for (int i = 0; i < waves.arraySize; i++)
                list.Add(waves.GetArrayElementAtIndex(i).objectReferenceValue as EnemySpawner);
            return list;
        }

        /// <summary>새 웨이브 오브젝트(Wave_N + EnemySpawner)를 만들어 waves 끝에 붙인다.</summary>
        public static EnemySpawner AddWave(BattleZone zone)
        {
            if (zone == null) return null;

            SerializedObject so = new SerializedObject(zone);
            SerializedProperty waves = so.FindProperty(P_Waves);

            GameObject go = new GameObject(WavePrefix + (waves.arraySize + 1));
            Undo.RegisterCreatedObjectUndo(go, "Add Wave");
            Undo.SetTransformParent(go.transform, zone.transform, "Add Wave");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            EnemySpawner spawner = Undo.AddComponent<EnemySpawner>(go);

            waves.InsertArrayElementAtIndex(waves.arraySize);
            waves.GetArrayElementAtIndex(waves.arraySize - 1).objectReferenceValue = spawner;
            so.ApplyModifiedProperties();
            return spawner;
        }

        /// <summary>이미 씬에 있는 EnemySpawner를 웨이브 목록에 편입한다(오브젝트 생성 없음).</summary>
        public static void LinkWave(BattleZone zone, EnemySpawner spawner)
        {
            if (zone == null || spawner == null) return;

            SerializedObject so = new SerializedObject(zone);
            SerializedProperty waves = so.FindProperty(P_Waves);
            for (int i = 0; i < waves.arraySize; i++)
                if (waves.GetArrayElementAtIndex(i).objectReferenceValue == spawner) return;

            waves.InsertArrayElementAtIndex(waves.arraySize);
            waves.GetArrayElementAtIndex(waves.arraySize - 1).objectReferenceValue = spawner;
            so.ApplyModifiedProperties();
        }

        /// <summary>waves에서 index를 제거한다. deleteObject면 웨이브 GameObject도 함께 삭제.</summary>
        public static void RemoveWave(BattleZone zone, int index, bool deleteObject)
        {
            if (zone == null) return;

            SerializedObject so = new SerializedObject(zone);
            SerializedProperty waves = so.FindProperty(P_Waves);
            if (index < 0 || index >= waves.arraySize) return;

            Object target = waves.GetArrayElementAtIndex(index).objectReferenceValue;
            waves.DeleteArrayElementAtIndex(index);
            so.ApplyModifiedProperties();

            EnemySpawner spawner = target as EnemySpawner;
            if (deleteObject && spawner != null)
                Undo.DestroyObjectImmediate(spawner.gameObject);
        }

        public static void MoveWave(BattleZone zone, int index, int delta)
        {
            if (zone == null) return;

            SerializedObject so = new SerializedObject(zone);
            SerializedProperty waves = so.FindProperty(P_Waves);
            int to = index + delta;
            if (index < 0 || index >= waves.arraySize || to < 0 || to >= waves.arraySize) return;

            waves.MoveArrayElement(index, to);
            so.ApplyModifiedProperties();
        }


        // ─────────── 스폰 엔트리 ───────────

        public static int GetEntryCount(EnemySpawner spawner)
        {
            if (spawner == null) return 0;
            return new SerializedObject(spawner).FindProperty(P_SpawnEntries).arraySize;
        }

        /// <summary>스폰 엔트리를 추가한다. point가 있으면 고정 위치, 없으면 플레이어 기준 스폰.</summary>
        public static void AddEntry(EnemySpawner spawner, GameObject prefab, Transform point,
                                    float delay, SpawnSide side)
        {
            if (spawner == null) return;

            SerializedObject so = new SerializedObject(spawner);
            SerializedProperty list = so.FindProperty(P_SpawnEntries);
            list.InsertArrayElementAtIndex(list.arraySize);

            // InsertArrayElementAtIndex는 직전 원소를 복제하므로 전 필드를 명시적으로 덮어쓴다.
            SerializedProperty e = list.GetArrayElementAtIndex(list.arraySize - 1);
            e.FindPropertyRelative(P_EntryPrefab).objectReferenceValue = prefab;
            e.FindPropertyRelative(P_EntryPoint).objectReferenceValue = point;
            e.FindPropertyRelative(P_EntryDelay).floatValue = delay;
            e.FindPropertyRelative(P_EntryRelative).boolValue = point == null;
            e.FindPropertyRelative(P_EntryDistance).floatValue = 0f;
            e.FindPropertyRelative(P_EntrySide).enumValueIndex = (int)side;

            so.ApplyModifiedProperties();
        }

        /// <summary>엔트리를 제거한다. deletePoint면 이 엔트리 전용 스폰 포인트도 함께 삭제.</summary>
        public static void RemoveEntry(EnemySpawner spawner, int index, bool deletePoint)
        {
            if (spawner == null) return;

            SerializedObject so = new SerializedObject(spawner);
            SerializedProperty list = so.FindProperty(P_SpawnEntries);
            if (index < 0 || index >= list.arraySize) return;

            Object point = list.GetArrayElementAtIndex(index)
                               .FindPropertyRelative(P_EntryPoint).objectReferenceValue;

            // 같은 포인트를 다른 엔트리가 쓰고 있으면 지우지 않는다.
            bool shared = false;
            for (int i = 0; i < list.arraySize && !shared; i++)
                if (i != index &&
                    list.GetArrayElementAtIndex(i).FindPropertyRelative(P_EntryPoint).objectReferenceValue == point)
                    shared = true;

            list.DeleteArrayElementAtIndex(index);
            so.ApplyModifiedProperties();

            Transform t = point as Transform;
            if (deletePoint && !shared && t != null)
                Undo.DestroyObjectImmediate(t.gameObject);
        }

        /// <summary>존의 SpawnPoints 아래에 스폰 포인트를 만든다.</summary>
        public static Transform CreateSpawnPoint(BattleZone zone, Vector3 worldPosition, string label)
        {
            Transform root = EnsureChild(zone.transform, SpawnPointsRootName);

            GameObject go = new GameObject("SP_" + label + "_" + (root.childCount + 1));
            Undo.RegisterCreatedObjectUndo(go, "Create Spawn Point");
            Undo.SetTransformParent(go.transform, root, "Create Spawn Point");
            go.transform.position = worldPosition;
            go.transform.rotation = zone.transform.rotation;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }


        // ─────────── 선발대(직접 배치) ───────────

        public static int GetPreplacedCount(EnemySpawner spawner)
        {
            if (spawner == null) return 0;
            return new SerializedObject(spawner).FindProperty(P_Preplaced).arraySize;
        }

        /// <summary>
        /// 적 프리팹을 씬에 직접 배치하고 preplacedEnemies에 등록한다.
        /// startInactive면 비활성으로 두고 wakePreplacedOnSpawn을 켜서 웨이브 시작 시 등장시킨다.
        /// </summary>
        public static Character PlacePreplaced(BattleZone zone, EnemySpawner spawner, GameObject prefab,
                                               Vector3 worldPosition, bool startInactive)
        {
            if (zone == null || spawner == null || prefab == null) return null;

            Transform root = EnsureChild(spawner.transform, PreplacedRootName);

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, root) as GameObject;
            if (instance == null) return null;
            Undo.RegisterCreatedObjectUndo(instance, "Place Enemy");

            instance.transform.position = worldPosition;
            instance.transform.rotation = zone.transform.rotation;

            Character character = instance.GetComponent<Character>();
            if (character == null)
            {
                Debug.LogWarning($"[BattleZoneTool] '{prefab.name}'에 Character 컴포넌트가 없어 선발대로 등록하지 못했습니다.", instance);
                return null;
            }

            if (startInactive) instance.SetActive(false);

            SerializedObject so = new SerializedObject(spawner);
            SerializedProperty list = so.FindProperty(P_Preplaced);
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = character;
            if (startInactive) so.FindProperty(P_WakePreplaced).boolValue = true;
            so.ApplyModifiedProperties();

            return character;
        }

        public static void RemovePreplaced(EnemySpawner spawner, int index, bool deleteObject)
        {
            if (spawner == null) return;

            SerializedObject so = new SerializedObject(spawner);
            SerializedProperty list = so.FindProperty(P_Preplaced);
            if (index < 0 || index >= list.arraySize) return;

            Object target = list.GetArrayElementAtIndex(index).objectReferenceValue;
            list.DeleteArrayElementAtIndex(index);
            so.ApplyModifiedProperties();

            Character c = target as Character;
            if (deleteObject && c != null)
                Undo.DestroyObjectImmediate(c.gameObject);
        }


        // ─────────── 벽 자동 맞춤 ───────────

        public static GameObject LoadWallPrefab(bool left)
        {
            string path = AssetDatabase.GUIDToAssetPath(left ? WallLeftGuid : WallRightGuid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>
        /// 트리거 BoxCollider의 양 끝(차단 축 ±)에 벽 프리팹을 배치·리사이즈한다.
        /// 이미 같은 프리팹 인스턴스가 Walls 아래에 있으면 새로 만들지 않고 위치만 갱신한다.
        /// </summary>
        public static void FitWalls(BattleZone zone, BlockAxis axis, float widthPadding, float heightPadding)
        {
            if (zone == null) return;

            BoxCollider box = zone.GetComponent<BoxCollider>();
            if (box == null)
            {
                Debug.LogWarning("[BattleZoneTool] BoxCollider가 없어 벽을 맞출 수 없습니다.", zone);
                return;
            }

            Transform wallsRoot = EnsureChild(zone.transform, WallsRootName);

            // battleWalls가 비어 있으면 이 시점에 연결해 준다.
            SerializedObject zoneSo = new SerializedObject(zone);
            SerializedProperty wallsProp = zoneSo.FindProperty(P_BattleWalls);
            if (wallsProp.objectReferenceValue == null)
            {
                wallsProp.objectReferenceValue = wallsRoot.gameObject;
                zoneSo.ApplyModifiedProperties();
            }

            int a = axis == BlockAxis.X ? 0 : 2;   // 차단 축
            int w = axis == BlockAxis.X ? 2 : 0;   // 벽 폭이 되는 축

            float half = box.size[a] * 0.5f;
            float width = box.size[w] + widthPadding;
            float height = box.size.y + heightPadding;

            // 벽의 두께 축은 프리팹 로컬 Z. 차단 축이 X면 Y로 90도 돌린다.
            Quaternion rot = axis == BlockAxis.X ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                GameObject prefab = LoadWallPrefab(left);
                if (prefab == null)
                {
                    Debug.LogWarning("[BattleZoneTool] 벽 프리팹을 찾지 못했습니다 (BattleZoneAuthoring의 GUID 확인).", zone);
                    continue;
                }

                Transform wall = FindWallInstance(wallsRoot, prefab);
                if (wall == null)
                {
                    GameObject go = PrefabUtility.InstantiatePrefab(prefab, wallsRoot) as GameObject;
                    if (go == null) continue;
                    Undo.RegisterCreatedObjectUndo(go, "Fit Walls");
                    wall = go.transform;
                }
                else
                {
                    Undo.RecordObject(wall, "Fit Walls");
                }

                Vector3 local = box.center;
                local[a] += left ? -half : half;

                // 존 로컬 → Walls 로컬 (Walls 루트가 오프셋을 갖고 있어도 위치가 어긋나지 않게)
                wall.localPosition = wallsRoot.InverseTransformPoint(zone.transform.TransformPoint(local));
                wall.localRotation = rot;

                Vector3 prefabScale = prefab.transform.localScale;
                wall.localScale = new Vector3(width, height, prefabScale.z <= 0f ? 1f : prefabScale.z);

                EditorUtility.SetDirty(wall);
            }
        }

        private static Transform FindWallInstance(Transform wallsRoot, GameObject prefab)
        {
            for (int i = 0; i < wallsRoot.childCount; i++)
            {
                Transform child = wallsRoot.GetChild(i);
                Object source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (source == prefab) return child;
            }
            return null;
        }
    }
}
