using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JiggleAnythingShader
{
    [ExecuteInEditMode]
    public class jiggle_interractorsHolder : MonoBehaviour
    {
        jiggle_InteractorPos[] interactors;
        Vector4[] pos = new Vector4[100];
        float[] radiuses = new float[100];
        //----------------------------------------------
        void Start() { FindInteractors(); }
        void Update()
        {
            FindInteractors();
            for (int i = 0; i < interactors.Length; i++){
                pos[i] = interactors[i].transform.position;
                radiuses[i] = interactors[i].radius;
            }
            Shader.SetGlobalVectorArray("_ShaderInteractorsPositions", pos);
            Shader.SetGlobalFloatArray("_ShaderInteractorsRadiuses", radiuses);
        }
        void FindInteractors() { interactors = FindObjectsOfType<jiggle_InteractorPos>(); }
    }
}