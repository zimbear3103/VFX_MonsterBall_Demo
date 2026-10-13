using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 포탈·진입점 세팅에서 자주 나는 실수를 훑어 경고 목록으로 돌려준다.
    /// 기계적으로 고칠 수 있는 항목은 Fix 델리게이트를 함께 실어 보낸다.
    ///
    /// 이 검사가 노리는 실패는 대부분 "조용히 어긋나는" 종류다 —
    /// 포탈은 발동하고 씬도 갈리는데 진입점이 없어 플레이어만 제자리에 남거나,
    /// 씬 이름이 Build Settings에 없어 로드가 통째로 실패하거나,
    /// 포탈이 자기 그룹을 가리켜 같은 자리를 다시 로드하는 경우다.
    /// </summary>
    public static class SceneFlowValidator
    {
        public struct Issue
        {
            public MessageType Type;
            public string Message;
            public UnityEngine.Object Context;
            public string FixLabel;
            public Action Fix;
        }

        // ─────────── 포탈 ───────────

        public static List<Issue> ValidatePortal(Portal portal)
        {
            List<Issue> issues = new List<Issue>();
            if (portal == null) return issues;

            ValidateTrigger(portal, issues);

            SceneGroup group = portal.TargetGroup;
            if (group == null)
            {
                Add(issues, MessageType.Error,
                    "대상 SceneGroup이 비어 있습니다. 진입해도 아무 일도 일어나지 않고 에러만 남습니다.", portal);
                return issues;
            }

            ValidateSelfTarget(portal, group, issues);
            ValidateGroupScenes(portal, group, issues);
            ValidateEntryKeyExists(portal, group, issues);

            if (!portal.Active)
            {
                Add(issues, MessageType.Info,
                    "Active가 꺼져 있습니다. 외부에서 SetActive(true)를 부르기 전까지 발동하지 않습니다.",
                    portal, "지금 켜기", () => SceneFlowAuthoring.SetPortalActive(portal, true));
            }

            return issues;
        }

        private static void ValidateTrigger(Portal portal, List<Issue> issues)
        {
            Collider col = portal.GetComponent<Collider>();
            if (col == null)
            {
                Add(issues, MessageType.Error,
                    "콜라이더가 없어 진입을 감지하지 못합니다.", portal,
                    "트리거 추가", () => SceneFlowAuthoring.EnsureTrigger(portal));
                return;
            }

            if (!col.isTrigger)
            {
                Add(issues, MessageType.Error,
                    "콜라이더의 Is Trigger가 꺼져 있습니다. 포탈이 발동하지 않고 플레이어를 막습니다.", portal,
                    "Is Trigger 켜기", () => SceneFlowAuthoring.EnsureTrigger(portal));
            }
        }

        /// <summary>포탈이 놓인 씬이 대상 그룹에 속하면, 진입하는 순간 같은 곳을 다시 로드한다.</summary>
        private static void ValidateSelfTarget(Portal portal, SceneGroup group, List<Issue> issues)
        {
            string sceneName = portal.gameObject.scene.name;
            if (string.IsNullOrEmpty(sceneName)) return;

            foreach (string name in group.EnumerateSceneNames())
            {
                if (name != sceneName) continue;

                Add(issues, MessageType.Warning,
                    $"이 포탈은 자기 자신이 속한 그룹('{group.name}')을 가리킵니다. 같은 구간을 다시 로드합니다. " +
                    "프리팹에서 만든 뒤 대상 그룹을 바꾸는 것을 잊었을 때 나는 증상입니다.", portal);
                return;
            }
        }

        private static void ValidateGroupScenes(Portal portal, SceneGroup group, List<Issue> issues)
        {
            SceneFlowAuthoring.GroupScan scan = SceneFlowAuthoring.ScanGroup(group);

            for (int i = 0; i < scan.MissingScenes.Count; i++)
            {
                Add(issues, MessageType.Error,
                    $"그룹 '{group.name}'이 지목한 씬 '{scan.MissingScenes[i]}'을 프로젝트에서 찾지 못했습니다. " +
                    "이름 오타이거나 씬이 이동/삭제된 상태입니다.", group);
            }

            for (int i = 0; i < scan.NotInBuild.Count; i++)
            {
                string sceneName = scan.NotInBuild[i];
                Add(issues, MessageType.Error,
                    $"씬 '{sceneName}'이 Build Settings에 없거나 꺼져 있습니다. 로더는 씬 이름으로 로드하므로 " +
                    "런타임에 이 씬만 빠진 채 전환됩니다.", group,
                    "Build에 추가", () => AddToBuildSettings(SceneFlowAuthoring.ScenePathOf(sceneName)));
            }
        }

        private static void ValidateEntryKeyExists(Portal portal, SceneGroup group, List<Issue> issues)
        {
            string key = SceneFlowAuthoring.ResolvedEntryKey(portal);
            if (string.IsNullOrEmpty(key))
            {
                Add(issues, MessageType.Error,
                    $"EntryKey가 비었고 그룹 '{group.name}'의 DefaultEntryKey도 비어 있습니다. 배치 위치를 결정할 수 없습니다.",
                    group);
                return;
            }

            SceneFlowAuthoring.GroupScan scan = SceneFlowAuthoring.ScanGroup(group);

            if (!scan.HasKey(key))
            {
                string message =
                    $"그룹 '{group.name}'의 어느 씬에도 EntryKey '{key}'인 SceneEntryPoint가 없습니다. " +
                    "씬은 전환되지만 플레이어는 이전 위치에 그대로 남습니다.";

                if (scan.LegacyOnlyScenes.Count > 0)
                {
                    // 위치 데이터는 이미 있는데 타입만 구버전인 흔한 상태 — 승계하면 바로 해결된다.
                    message += $" (씬 '{string.Join(", ", scan.LegacyOnlyScenes)}'에 구버전 LoadPoint가 남아 있습니다. " +
                               "툴 창의 'LoadPoint 승계'로 옮길 수 있습니다.)";
                }

                Add(issues, MessageType.Error, message, group);
                return;
            }

            if (scan.HasDuplicate(key))
            {
                Add(issues, MessageType.Warning,
                    $"그룹 '{group.name}'에 EntryKey '{key}'가 둘 이상 있습니다. 로더는 먼저 찾은 하나만 쓰므로 " +
                    "어디로 스폰될지 확정되지 않습니다.", group);
            }
        }

        // ─────────── 진입점 ───────────

        public static List<Issue> ValidateEntry(SceneEntryPoint entry)
        {
            List<Issue> issues = new List<Issue>();
            if (entry == null) return issues;

            if (string.IsNullOrWhiteSpace(entry.EntryKey))
            {
                Add(issues, MessageType.Error,
                    "EntryKey가 비어 있습니다. 어떤 포탈도 이 진입점을 찾을 수 없습니다.", entry,
                    "Start로 설정",
                    () => SceneFlowAuthoring.SetEntryKey(entry, SceneFlowAuthoring.DefaultEntryKey));
            }

            ValidateEntryScene(entry, issues);
            ValidateEntryGround(entry, issues);
            ValidateLegacyLeftover(entry, issues);

            return issues;
        }

        /// <summary>로더는 그룹이 로드한 씬들만 훑는다. 그룹 밖 씬의 진입점은 존재해도 발견되지 않는다.</summary>
        private static void ValidateEntryScene(SceneEntryPoint entry, List<Issue> issues)
        {
            Scene scene = entry.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.name)) return;

            List<SceneGroup> groups = SceneFlowAuthoring.GroupsContainingScene(scene.name);
            if (groups.Count == 0)
            {
                Add(issues, MessageType.Error,
                    $"이 진입점이 있는 씬 '{scene.name}'은 어떤 SceneGroup에도 속하지 않습니다. " +
                    "로더는 그룹이 로드한 씬만 훑으므로 이 진입점은 발견되지 않습니다.", entry);
                return;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                SceneFlowAuthoring.GroupScan scan = SceneFlowAuthoring.ScanGroup(groups[i]);
                if (!scan.HasDuplicate(entry.EntryKey)) continue;

                Add(issues, MessageType.Warning,
                    $"그룹 '{groups[i].name}' 안에 EntryKey '{entry.EntryKey}'가 둘 이상 있습니다. " +
                    "로더는 먼저 찾은 하나만 씁니다.", entry);
            }
        }

        private static void ValidateEntryGround(SceneEntryPoint entry, List<Issue> issues)
        {
            if (SceneFlowAuthoring.HasFloorUnder(entry.transform)) return;

            Add(issues, MessageType.Warning,
                "진입점 아래에 바닥이 없습니다. 스폰 직후 그대로 떨어집니다.", entry,
                "바닥에 맞추기", () =>
                {
                    if (!SceneFlowAuthoring.SnapToGround(entry.transform))
                        Debug.LogWarning("[SceneFlow] 아래에서 바닥 콜라이더를 찾지 못했습니다.", entry);
                });
        }

        /// <summary>승계 후 남은 구버전 마커. 값이 서로 다르면 어느 쪽이 맞는지 헷갈린다.</summary>
        private static void ValidateLegacyLeftover(SceneEntryPoint entry, List<Issue> issues)
        {
            Aiara.LoadPoint legacy = entry.GetComponent<Aiara.LoadPoint>();
            if (legacy == null) return;

            if (legacy.pointName == entry.EntryKey)
            {
                Add(issues, MessageType.Info,
                    "같은 오브젝트에 구버전 LoadPoint가 남아 있습니다(동작에는 영향 없음).", entry,
                    "LoadPoint 제거", () => Undo.DestroyObjectImmediate(legacy));
                return;
            }

            Add(issues, MessageType.Warning,
                $"구버전 LoadPoint의 pointName('{legacy.pointName}')이 EntryKey('{entry.EntryKey}')와 다릅니다. " +
                "실제로 쓰이는 값은 EntryKey입니다.", entry,
                "LoadPoint 제거", () => Undo.DestroyObjectImmediate(legacy));
        }

        // ─────────── 공통 ───────────

        /// <summary>씬을 Build Settings 목록 끝에 켜진 상태로 추가한다.</summary>
        public static void AddToBuildSettings(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return;

            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path != scenePath) continue;
                scenes[i].enabled = true;
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        public static int CountErrors(List<Issue> issues)
        {
            int errors = 0;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].Type == MessageType.Error) errors++;
            return errors;
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
