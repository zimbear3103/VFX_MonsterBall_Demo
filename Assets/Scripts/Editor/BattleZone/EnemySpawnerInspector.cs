using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// EnemySpawner 인스펙터. 이 스포너가 속한 배틀존을 찾아 웨이브 번호를 알려 주고,
    /// 씬 뷰에는 자기 웨이브의 스폰 포인트만 강조해 그린다.
    /// </summary>
    [CustomEditor(typeof(EnemySpawner))]
    [CanEditMultipleObjects]
    public class EnemySpawnerInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length > 1) return;

            EnemySpawner spawner = (EnemySpawner)target;
            BattleZone zone = spawner.GetComponentInParent<BattleZone>();

            EditorGUILayout.Space();

            if (zone == null)
            {
                EditorGUILayout.HelpBox(
                    "부모에 BattleZone이 없습니다. 단독 스포너라면 정상이고, 웨이브로 쓰려면 존 아래로 옮기세요.",
                    MessageType.Info);
                return;
            }

            int index = BattleZoneAuthoring.GetWaves(zone).IndexOf(spawner);

            if (index < 0)
            {
                EditorGUILayout.HelpBox($"'{zone.name}'의 웨이브 목록에 등록되어 있지 않습니다.", MessageType.Warning);
                if (GUILayout.Button("웨이브로 등록"))
                    BattleZoneAuthoring.LinkWave(zone, spawner);
            }
            else
            {
                EditorGUILayout.LabelField($"{zone.name}의 Wave {index + 1}", EditorStyles.boldLabel);
            }

            if (GUILayout.Button("배틀존 툴 열기", GUILayout.Height(24f)))
            {
                Selection.activeGameObject = spawner.gameObject;
                BattleZoneToolWindow.Open();
            }
        }

        private void OnSceneGUI()
        {
            EnemySpawner spawner = (EnemySpawner)target;
            BattleZone zone = spawner.GetComponentInParent<BattleZone>();

            int index = zone != null ? BattleZoneAuthoring.GetWaves(zone).IndexOf(spawner) : 0;
            if (zone != null) BattleZoneGizmos.DrawTriggerBox(zone);

            BattleZoneGizmos.DrawWave(spawner, Mathf.Max(index, 0), false);
        }
    }
}
