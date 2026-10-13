using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JiggleAnythingShader
{
    public class example_code : MonoBehaviour
    {
        public GameObject shaderTargetGameobject;
        public bool runCode;
        //-----------------------------
        [Header("Settings")]
        public int effectID;
        public bool disableEmissive;
        //-----------------------------
        void Start()
        {
        }
        void Update(){
            if (runCode == true) { runAction(); runCode = false; }
        }
        public void runAction(){
            if (shaderTargetGameobject.GetComponent<jiggle_effectController>() != null){
                shaderTargetGameobject.GetComponent<jiggle_effectController>().effectID = effectID;
                shaderTargetGameobject.GetComponent<jiggle_effectController>().runEffect = true;
            }
            //--------
            if (shaderTargetGameobject.GetComponent<Renderer>().material.GetInt("_Use_Emission") != null){
                if (disableEmissive == true) { shaderTargetGameobject.GetComponent<Renderer>().material.SetInt("_Use_Emission", 0); }
                if (disableEmissive == false) { shaderTargetGameobject.GetComponent<Renderer>().material.SetInt("_Use_Emission", 1); }
            }
        }
    }
}