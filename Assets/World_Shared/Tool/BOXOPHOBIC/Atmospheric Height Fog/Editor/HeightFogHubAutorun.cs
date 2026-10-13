using UnityEditor;

namespace AtmosphericHeightFog
{

    [InitializeOnLoad]
    public class HeightFogHubAutorun
    {
        static HeightFogHubAutorun()
        {
            //EditorApplication.update += OnInit;
        }

        static void OnInit()
        {
            //EditorApplication.update -= OnInit;
            //HeightFogHub window = EditorWindow.GetWindow<HeightFogHub>(false, "Atmoshperic Height Fog", true);
            //window.Show();
        }
    }
}


