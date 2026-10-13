//	Unluck Software	
// 	www.chemicalbliss.com

using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(BakedMeshAnimation))]
[CanEditMultipleObjects]
[System.Serializable]
public class BakedMeshAnimatonEditor : Editor
{
	public override void OnInspectorGUI()
	{
		BakedMeshAnimation target_cs = (BakedMeshAnimation)target;
		GUILayout.Space(15);
		if (GUILayout.Button("Reverse Mesh Array"))
		{
			System.Array.Reverse(target_cs.meshes);
		}

		EditorGUILayout.HelpBox("Lock inspector window to drag multiple meshes from project window", MessageType.Info);

		GUILayout.Space(15);
		DrawDefaultInspector();
		if (GUI.changed)
		{
			EditorUtility.SetDirty(target_cs);
		}
	}
}