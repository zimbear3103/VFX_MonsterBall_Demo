//	Unluck Software	
// 	www.chemicalbliss.com

using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(BakedMeshAnimator))]
[CanEditMultipleObjects]
[System.Serializable]
public class BakedMeshAnimatorEditor : Editor
{
	public override void OnInspectorGUI()
	{
		BakedMeshAnimator m_target = (BakedMeshAnimator)target;
		DrawDefaultInspector();
		if (GUI.changed)
		{
			EditorUtility.SetDirty(m_target);
		}
		GUILayout.Space(15);
		if (m_target.crossfade)
		{
			EditorGUILayout.HelpBox("All meshes must have the same vert count for crossfade to function.", MessageType.Warning);
			if (m_target.crossfadeNormalFix)
			{
				EditorGUILayout.HelpBox("NormalFix enabled: uses a high amount of cpu", MessageType.Warning);
			}
		}
	}
}