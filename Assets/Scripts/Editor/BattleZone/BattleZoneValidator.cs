using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 배틀존 세팅에서 자주 나는 실수를 훑어 경고 목록으로 돌려준다.
    /// 기계적으로 고칠 수 있는 항목은 Fix 델리게이트를 함께 실어 보낸다.
    /// </summary>
    public static class BattleZoneValidator
    {
        public struct Issue
        {
            public MessageType Type;
            public string Message;
            public UnityEngine.Object Context;
            public string FixLabel;
            public Action Fix;
        }

        public static List<Issue> Validate(BattleZone zone)
        {
            List<Issue> issues = new List<Issue>();
            if (zone == null) return issues;

            ValidateZone(zone, issues);

            List<EnemySpawner> waves = BattleZoneAuthoring.GetWaves(zone);
            if (waves.Count == 0)
            {
                Add(issues, MessageType.Warning, "웨이브가 하나도 없습니다. 진입 즉시 전투가 종료됩니다.", zone);
            }

            bool hasNullWave = waves.Contains(null);
            if (hasNullWave)
            {
                Add(issues, MessageType.Warning, "웨이브 목록에 빈(None) 항목이 있습니다.", zone,
                    "빈 항목 제거", () => RemoveNullWaves(zone));
            }

            for (int i = 0; i < waves.Count; i++)
            {
                if (waves[i] != null) ValidateWave(zone, waves[i], i, issues);
            }

            return issues;
        }

        private static void ValidateZone(BattleZone zone, List<Issue> issues)
        {
            BoxCollider box = zone.GetComponent<BoxCollider>();
            if (box == null)
            {
                Add(issues, MessageType.Error, "BoxCollider가 없어 진입 트리거가 동작하지 않습니다.", zone,
                    "BoxCollider 추가", () =>
                    {
                        BoxCollider added = Undo.AddComponent<BoxCollider>(zone.gameObject);
                        added.isTrigger = true;
                        added.size = BattleZoneAuthoring.DefaultZoneSize;
                    });
            }
            else if (!box.isTrigger)
            {
                // 런타임 Awake가 강제로 켜주지만, 에디터에서 꺼져 있으면 벽/지형과 물리 충돌이 나 보인다.
                Add(issues, MessageType.Warning, "BoxCollider의 Is Trigger가 꺼져 있습니다.", zone,
                    "Is Trigger 켜기", () =>
                    {
                        Undo.RecordObject(box, "Set Is Trigger");
                        box.isTrigger = true;
                        EditorUtility.SetDirty(box);
                    });
            }

            SerializedObject so = new SerializedObject(zone);
            if (so.FindProperty(BattleZoneAuthoring.P_BattleWalls).objectReferenceValue == null)
            {
                Transform walls = zone.transform.Find(BattleZoneAuthoring.WallsRootName);
                if (walls != null)
                {
                    Add(issues, MessageType.Warning, "Battle Walls가 비어 있습니다. (Walls 자식은 존재)", zone,
                        "Walls 연결", () =>
                        {
                            SerializedObject s = new SerializedObject(zone);
                            s.FindProperty(BattleZoneAuthoring.P_BattleWalls).objectReferenceValue = walls.gameObject;
                            s.ApplyModifiedProperties();
                        });
                }
                else
                {
                    Add(issues, MessageType.Warning, "Battle Walls가 비어 있어 전투 중 구역을 벗어날 수 있습니다.", zone);
                }
            }
        }

        private static void ValidateWave(BattleZone zone, EnemySpawner wave, int index, List<Issue> issues)
        {
            string tag = $"Wave {index + 1} ({wave.gameObject.name})";

            if (!wave.gameObject.activeInHierarchy)
            {
                Add(issues, MessageType.Error, $"{tag}: 오브젝트가 비활성이라 스폰 코루틴이 돌지 않습니다.", wave,
                    "활성화", () =>
                    {
                        Undo.RecordObject(wave.gameObject, "Activate Wave");
                        wave.gameObject.SetActive(true);
                    });
            }

            SerializedObject so = new SerializedObject(wave);
            SerializedProperty entries = so.FindProperty(BattleZoneAuthoring.P_SpawnEntries);
            SerializedProperty preplaced = so.FindProperty(BattleZoneAuthoring.P_Preplaced);

            if (entries.arraySize == 0 && preplaced.arraySize == 0)
            {
                Add(issues, MessageType.Warning,
                    $"{tag}: 스폰 엔트리도 선발대도 없습니다. 이 웨이브는 즉시 넘어가지 않고 멈춥니다.", wave);
            }

            BoxCollider box = zone.GetComponent<BoxCollider>();

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty e = entries.GetArrayElementAtIndex(i);
                GameObject prefab = e.FindPropertyRelative(BattleZoneAuthoring.P_EntryPrefab).objectReferenceValue as GameObject;

                if (prefab == null)
                {
                    int captured = i;
                    Add(issues, MessageType.Error, $"{tag} / 엔트리 {i + 1}: 프리팹이 비어 있습니다.", wave,
                        "엔트리 제거", () => BattleZoneAuthoring.RemoveEntry(wave, captured, false));
                    continue;
                }

                if (prefab.GetComponent<Character>() == null)
                {
                    Add(issues, MessageType.Error,
                        $"{tag} / 엔트리 {i + 1}: '{prefab.name}'에 Character가 없어 전멸 판정에 잡히지 않습니다.", wave);
                }

                Transform point = e.FindPropertyRelative(BattleZoneAuthoring.P_EntryPoint).objectReferenceValue as Transform;
                if (point != null && box != null && !ContainsPoint(zone, box, point.position))
                {
                    Add(issues, MessageType.Info,
                        $"{tag} / 엔트리 {i + 1}: 스폰 포인트가 존 트리거 밖입니다.", point);
                }
            }

            bool wake = so.FindProperty(BattleZoneAuthoring.P_WakePreplaced).boolValue;
            for (int i = 0; i < preplaced.arraySize; i++)
            {
                Character c = preplaced.GetArrayElementAtIndex(i).objectReferenceValue as Character;
                if (c == null)
                {
                    int captured = i;
                    Add(issues, MessageType.Warning, $"{tag} / 선발대 {i + 1}: 빈(None) 항목입니다.", wave,
                        "제거", () => BattleZoneAuthoring.RemovePreplaced(wave, captured, false));
                    continue;
                }

                if (!c.gameObject.activeSelf && !wake)
                {
                    Add(issues, MessageType.Warning,
                        $"{tag} / 선발대 '{c.name}': 비활성인데 Wake Preplaced On Spawn이 꺼져 있어 영영 등장하지 않습니다.", c,
                        "Wake 켜기", () =>
                        {
                            SerializedObject s = new SerializedObject(wave);
                            s.FindProperty(BattleZoneAuthoring.P_WakePreplaced).boolValue = true;
                            s.ApplyModifiedProperties();
                        });
                }
            }
        }

        /// <summary>월드 좌표가 존 트리거 박스 안인지.</summary>
        public static bool ContainsPoint(BattleZone zone, BoxCollider box, Vector3 worldPoint)
        {
            Vector3 local = zone.transform.InverseTransformPoint(worldPoint) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private static void RemoveNullWaves(BattleZone zone)
        {
            SerializedObject so = new SerializedObject(zone);
            SerializedProperty waves = so.FindProperty(BattleZoneAuthoring.P_Waves);
            for (int i = waves.arraySize - 1; i >= 0; i--)
                if (waves.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    waves.DeleteArrayElementAtIndex(i);
            so.ApplyModifiedProperties();
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
