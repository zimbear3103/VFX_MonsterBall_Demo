using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[ExecuteInEditMode]
[CustomEditor(typeof(FieldGenerator))]
public class fieldGenerator_Editor : Editor
{
	
	FieldGenerator spaceGen;

	private void OnEnable()
	{
		spaceGen = (FieldGenerator)target;
		//EditorUtility.SetDirty(target);
	}

	public override void OnInspectorGUI()
	{
		EditorGUILayout.Space(10f);
		if (GUILayout.Button("Generate Asteroids Field", GUILayout.Height(40f)))
		{
			spaceGen.GenerateFunc();
		}

		EditorGUILayout.Space(8f);

		if (GUILayout.Button("Delete Asteroids Field", GUILayout.Height(20f)))
		{
			spaceGen.DeleteField();
		}

		EditorGUILayout.Space(8f);
		base.DrawDefaultInspector();
		if (GUI.changed) { EditorUtility.SetDirty(target); }
		Repaint();
		
	}
}
