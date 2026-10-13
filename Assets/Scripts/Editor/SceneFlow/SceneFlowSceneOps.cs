using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 씬을 여닫는 조작만 모아둔 곳. 진입점을 배치하려면 결국 그 그룹의 씬이 열려 있어야 하는데,
    /// 씬 열기는 저장되지 않은 작업을 날릴 수 있으므로 저장 확인을 반드시 거치도록 한 군데로 묶었다.
    /// </summary>
    public static class SceneFlowSceneOps
    {
        /// <summary>그룹의 씬들을 현재 열린 씬에 더해서(Additive) 연다. 이미 열린 씬은 건너뛴다.</summary>
        public static bool OpenGroupScenes(SceneGroup group)
        {
            if (group == null) return false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            int opened = 0;
            foreach (string sceneName in group.EnumerateSceneNames())
            {
                Scene already = SceneManager.GetSceneByName(sceneName);
                if (already.IsValid() && already.isLoaded) continue;

                string path = SceneFlowAuthoring.ScenePathOf(sceneName);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning($"[SceneFlow] 씬 '{sceneName}'을 프로젝트에서 찾지 못했습니다.", group);
                    continue;
                }

                EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                opened++;
            }

            SceneFlowAuthoring.ClearScanCache();
            Debug.Log($"[SceneFlow] 그룹 '{group.name}' 씬 {opened}개를 추가로 열었습니다.", group);
            return true;
        }

        /// <summary>씬 하나만 추가로 연다.</summary>
        public static bool OpenScene(string sceneName)
        {
            string path = SceneFlowAuthoring.ScenePathOf(sceneName);
            if (string.IsNullOrEmpty(path)) return false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneFlowAuthoring.ClearScanCache();
            return true;
        }

        /// <summary>변경된 씬을 저장한다. 배치 작업 뒤 사용자가 명시적으로 누를 때만 부른다.</summary>
        public static void SaveOpenScenes()
        {
            EditorSceneManager.SaveOpenScenes();
            SceneFlowAuthoring.ClearScanCache();
        }
    }
}
