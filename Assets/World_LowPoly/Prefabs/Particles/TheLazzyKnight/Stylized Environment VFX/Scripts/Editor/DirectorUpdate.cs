using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(WindDirector))]
public class DirectorUpdate : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        WindDirector windDirector = (WindDirector)target;
        if (GUILayout.Button("Update Effects"))
        {
            windDirector.AddVisualEffects();
        }
    }
}
