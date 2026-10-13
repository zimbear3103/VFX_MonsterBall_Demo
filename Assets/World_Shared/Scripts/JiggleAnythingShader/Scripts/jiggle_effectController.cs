using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static JiggleAnythingShader.jiggle_effectController;

namespace JiggleAnythingShader
{
    public class jiggle_effectController : MonoBehaviour
    {
        public bool runEffect;
        [Tooltip("select ID from commands below,then tick runEffect")] public int effectID = -1;
        public effectTypes idleAction;
        public runEffectOnCommand[] commands;
        [Tooltip("disabled interractors when effect is not being run")] public GameObject[] interractors;
        public GameObject[] connectedEffectControllers;
        public bool useAnimatedJiggle;
        public vectorDir animatedJiggleDirection;
        [Range(0.001f, 0.1f)]public float animatedJiggleDistance;
        [Tooltip("less is more")][Range(0.25f, 2f)] public float animatedJiggleSpeed;
        //------------
        Vector4 tempFloats, savedTempFloats;
        Vector4 tempStartPoint;
        Vector4 tempTarget = new Vector4(0, 0, 0, 0);
        //------------
        float timeElapsed = 0;
        float lerpDuration = 1f;
        //------------
        void Start()
        {
            GetComponent<Renderer>().material = this.GetComponent<Renderer>().material;
            //-------------------------------------------------------------------------------------------------------------------------------------
            if (idleAction == effectTypes.DEFAULTS)
            {
                savedTempFloats = new Vector4(
                    GetComponent<Renderer>().material.GetFloat("_Distance_UpDown"), GetComponent<Renderer>().material.GetFloat("_Distance_InOut"),
                    GetComponent<Renderer>().material.GetFloat("_AnimationSpeed"), GetComponent<Renderer>().material.GetFloat("_squishiness")
                    );
            }
            idleEffect();
            enableDisableEffectors(false);
        }
        void Update()
        {
            if (runEffect == true)
            {
                runEffectCommand(effectID);
                if (connectedEffectControllers.Length > 0) { sendToConnectedEffectorConnectors(); }
                runEffect = false;
            }
        }
        //-----------------------------------------------------------------------------------------------------------------------------------------
        public void enableDisableEffectors(bool trueOrFalse)
        {
            for (int i = 0; i < interractors.Length; i++)
            {
                interractors[i].SetActive(trueOrFalse);
            }
        }
        public void sendToConnectedEffectorConnectors()
        {
            foreach (GameObject controller in connectedEffectControllers)
            {
                controller.GetComponent<jiggle_effectController>().effectID = effectID;
                controller.GetComponent<jiggle_effectController>().runEffect = runEffect;
            }
        }
        public void idleEffect()
        {
            switch (idleAction)
            {
                case effectTypes.DEFAULTS: tempFloats = new Vector4(savedTempFloats.x, savedTempFloats.y, savedTempFloats.z, savedTempFloats.w); break;
                case effectTypes.breath: tempFloats = new Vector4(0, 5, 1.5f, 0); break;
                case effectTypes.breathHeavy: tempFloats = new Vector4(0, 10, 2f, 0); break;
                case effectTypes.jiggle: tempFloats = new Vector4(10, 10, 20, 3); break;
                case effectTypes.squeeze: tempFloats = new Vector4(10, 10, 10, 15); break;
                case effectTypes.flattenTop: tempFloats = new Vector4(-10, 10, 10, 20); break;
                case effectTypes.pulsating: tempFloats = new Vector4(3, 3, 30, 0); break;
                case effectTypes.inflateSTATIC: tempFloats = new Vector4(0, 0, 0.01f, 0); setBools(1); setOthers(5, new Vector3(0, 0, 0), new Vector3(0, 0, 0)); break;
                case effectTypes.serpent: tempFloats = new Vector4(0, 0, 0.01f, 0); setBools(2); setOthers(0, new Vector3(0.2f, 5, 5), new Vector3(0, 0, 0)); break;
                case effectTypes.fluid: tempFloats = new Vector4(0, 0, 1f, 0); setBools(3); setOthers(0, new Vector3(0, 0, 0), new Vector3(10, 0.5f, 0.5f)); break;
                case effectTypes.melt: tempFloats = new Vector4(0, 0, 1f, 0); setBools(4); setOthers(0, new Vector3(0, 0, 0), new Vector3(0, 0, 0)); StartCoroutine(melting(GetComponent<Renderer>().material.GetFloat("_Melt_Speed"))); break;
                case effectTypes.STOP: tempFloats = new Vector4(0, 0, 0, 0); break;
            }
            if (idleAction != effectTypes.DEFAULTS)
            {
                set();
            }
        }
        public void set()
        {
            GetComponent<Renderer>().material.SetFloat("_Distance_UpDown", tempFloats.x);
            GetComponent<Renderer>().material.SetFloat("_Distance_InOut", tempFloats.y);
            GetComponent<Renderer>().material.SetFloat("_AnimationSpeed", tempFloats.z);
            GetComponent<Renderer>().material.SetFloat("_squishiness", tempFloats.w);
        }
        public void setBools(int id)
        {
            switch (id)
            {
                case 1: GetComponent<Renderer>().material.SetInt("_Inflate", 1); GetComponent<Renderer>().material.SetInt("_Serpent", 0); GetComponent<Renderer>().material.SetInt("_Fluid", 0); GetComponent<Renderer>().material.SetInt("_Melt", 0); break;
                case 2: GetComponent<Renderer>().material.SetInt("_Inflate", 0); GetComponent<Renderer>().material.SetInt("_Serpent", 1); GetComponent<Renderer>().material.SetInt("_Fluid", 0); GetComponent<Renderer>().material.SetInt("_Melt", 0); break;
                case 3: GetComponent<Renderer>().material.SetInt("_Inflate", 0); GetComponent<Renderer>().material.SetInt("_Serpent", 0); GetComponent<Renderer>().material.SetInt("_Fluid", 1); GetComponent<Renderer>().material.SetInt("_Melt", 0); break;
                case 4: GetComponent<Renderer>().material.SetInt("_Inflate", 0); GetComponent<Renderer>().material.SetInt("_Serpent", 0); GetComponent<Renderer>().material.SetInt("_Fluid", 0); GetComponent<Renderer>().material.SetInt("_Melt", 1); break;
            }
        }
        public void setOthers(int inflt, Vector3 serpent, Vector3 fluid)
        {
            GetComponent<Renderer>().material.SetFloat("_Inflate Amount", inflt);
            GetComponent<Renderer>().material.SetVector("_Serpent_Distance_Amount_Speed", serpent);
            GetComponent<Renderer>().material.SetFloat("_Fluid_Sharpness", fluid.x);
            GetComponent<Renderer>().material.SetFloat("_Fluid_Tiling", fluid.y);
            GetComponent<Renderer>().material.SetFloat("_Fluid_Power", fluid.z);
        }
        public void runEffectCommand(int id)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                if (id == commands[i].ID)
                {
                    switch (commands[i].runEffectOnCommandAction)
                    {
                        case effectTypes.DEFAULTS: break;
                        case effectTypes.breath: tempFloats = new Vector4(0, 5, 1.5f, 0); break;
                        case effectTypes.breathHeavy: tempFloats = new Vector4(0, 10, 2f, 0); break;
                        case effectTypes.jiggle: tempFloats = new Vector4(10, 10, 20, 3); if (useAnimatedJiggle == true) { enableDisableEffectors(true); StartCoroutine(animatedJiggle(animatedJiggleDirection)); } break;
                        case effectTypes.squeeze: tempFloats = new Vector4(10, 10, 10, 15); break;
                        case effectTypes.flattenTop: tempFloats = new Vector4(-10, 10, 10, 20); break;
                        case effectTypes.pulsating: tempFloats = new Vector4(3, 3, 30, 0); break;
                        case effectTypes.inflateSTATIC: tempFloats = new Vector4(0, 10, 0.01f, 0); setBools(1); setOthers(5, new Vector3(0, 0, 0), new Vector3(0, 0, 0)); break;
                        case effectTypes.serpent: tempFloats = new Vector4(0, 0, 0.01f, 0); setBools(2); setOthers(0, new Vector3(0.2f, 5, 5), new Vector3(0, 0, 0)); break;
                        case effectTypes.fluid: tempFloats = new Vector4(0, 0, 1f, 0); setBools(3); setOthers(0, new Vector3(0, 0, 0), new Vector3(10, 0.5f, 0.5f)); break;
                        case effectTypes.melt: tempFloats = new Vector4(0, 0, 1f, 0); setBools(4); setOthers(0, new Vector3(0, 0, 0), new Vector3(0, 0, 0)); StartCoroutine(melting(GetComponent<Renderer>().material.GetFloat("_Melt_Speed"))); break;
                        case effectTypes.STOP: tempFloats = new Vector4(0, 0, 0, 0); break;
                    }
                    if (useAnimatedJiggle == false && commands[i].runEffectOnCommandAction != effectTypes.jiggle)
                    {
                        set();
                        StartCoroutine(setLerped(effectID, 2, i));
                    }
                }
            }
        }
        public void lerping()
        {
            GetComponent<Renderer>().material.SetFloat("_Distance_UpDown", Mathf.Lerp(tempStartPoint.x, tempTarget.x, timeElapsed / lerpDuration));
            GetComponent<Renderer>().material.SetFloat("_Distance_InOut", Mathf.Lerp(tempStartPoint.y, tempTarget.y, timeElapsed / lerpDuration));
            //GetComponent<Renderer>().material.SetFloat("_AnimationSpeed", Mathf.Lerp(tempStartPoint.z, tempTarget.z, timeElapsed / lerpDuration));
            GetComponent<Renderer>().material.SetFloat("_squishiness", Mathf.Lerp(tempStartPoint.w, tempTarget.w, timeElapsed / lerpDuration));
        }
        #region coroutines & enums
        IEnumerator setLerped(int id, int dir, int loopID)
        {
            enableDisableEffectors(true);
            timeElapsed = 0;
            lerpDuration = 2;
            yield return new WaitForSeconds(commands[loopID].doOnceDelay);
            tempStartPoint = new Vector4(GetComponent<Renderer>().material.GetFloat("_Distance_UpDown"),
                GetComponent<Renderer>().material.GetFloat("_Distance_InOut"),
                GetComponent<Renderer>().material.GetFloat("_AnimationSpeed"),
                GetComponent<Renderer>().material.GetFloat("_squishiness"));
            if (dir == 1)
            {
                switch (id)
                {
                    case 0: tempTarget = new Vector4(0, 5, 1.5f, 0); break;
                    case 1: tempTarget = new Vector4(0, 10, 2f, 0); break;
                    case 2: tempTarget = new Vector4(10, 10, 20, 3); break;
                    case 3: tempTarget = new Vector4(10, 10, 10, 15); break;
                    case 4: tempTarget = new Vector4(-10, 10, 10, 20); break;
                    case 5: tempTarget = new Vector4(3, 3, 30, 0); break;
                }
            }
            else if (dir == 2)
            {
                switch (idleAction)
                {
                    case effectTypes.breath: tempTarget = new Vector4(0, 5, 1.5f, 0); break;
                    case effectTypes.breathHeavy: tempTarget = new Vector4(0, 10, 2f, 0); break;
                    case effectTypes.jiggle: tempTarget = new Vector4(10, 10, 20, 3); break;
                    case effectTypes.squeeze: tempTarget = new Vector4(10, 10, 10, 15); break;
                    case effectTypes.flattenTop: tempTarget = new Vector4(-10, 10, 10, 20); break;
                    case effectTypes.pulsating: tempTarget = new Vector4(3, 3, 30, 0); break;
                }
            }
            while (timeElapsed < lerpDuration)
            {
                timeElapsed += 0.25f * Time.maximumDeltaTime;
                lerping();
                yield return null;
            }
            GetComponent<Renderer>().material.SetFloat("_AnimationSpeed", Mathf.Lerp(tempStartPoint.z, tempTarget.z, timeElapsed / lerpDuration));
            if (commands[loopID].doOnce == true) { StartCoroutine(delayReset(commands[loopID].doOnceDelay)); }
            enableDisableEffectors(false);
        }
        IEnumerator animatedJiggle(vectorDir dir)
        {
            Vector3 tempDir = Vector3.zero;
            switch (dir)
            {
                case vectorDir.X: tempDir = new Vector3(animatedJiggleDistance, 0, 0); break;
                case vectorDir.Y: tempDir = new Vector3(0, animatedJiggleDistance, 0); break;
                case vectorDir.Z: tempDir = new Vector3(0, 0, animatedJiggleDistance); break;
            }
            List<Vector3> tempStartPos = new List<Vector3>();
            List<Vector3> tempEndPoint = new List<Vector3>();
            for (int i = 0; i < interractors.Length; i++) { tempStartPos.Add(interractors[i].transform.localPosition); tempEndPoint.Add(tempStartPos[i] + (tempDir * -1)); }
            yield return new WaitForSeconds(0.1f);
            //-------
            timeElapsed = 0;
            while (timeElapsed < animatedJiggleSpeed)
            {
                timeElapsed += 0.25f * Time.maximumDeltaTime;
                for (int i = 0; i < interractors.Length; i++)
                {
                    interractors[i].transform.localPosition = Vector3.Lerp(tempStartPos[i], tempEndPoint[i], timeElapsed / lerpDuration);
                }
                yield return null;
            }
            timeElapsed = 0;
            while (timeElapsed < animatedJiggleSpeed)
            {
                timeElapsed += 0.25f * Time.maximumDeltaTime;
                for (int i = 0; i < interractors.Length; i++)
                {
                    interractors[i].transform.localPosition = Vector3.Lerp(tempEndPoint[i], tempStartPos[i], timeElapsed / lerpDuration);
                }
                yield return null;
            }
            //-------
            timeElapsed = 0;
            while (timeElapsed < animatedJiggleSpeed)
            {
                timeElapsed += 0.25f * Time.maximumDeltaTime;
                for (int i = 0; i < interractors.Length; i++)
                {
                    interractors[i].transform.localPosition = Vector3.Lerp(tempStartPos[i], tempEndPoint[i] + (tempDir / 3), timeElapsed / lerpDuration);
                }
                yield return null;
            }
            timeElapsed = 0;
            while (timeElapsed < animatedJiggleSpeed)
            {
                timeElapsed += 0.25f * Time.maximumDeltaTime;
                for (int i = 0; i < interractors.Length; i++)
                {
                    interractors[i].transform.localPosition = Vector3.Lerp(tempEndPoint[i] + (tempDir / 3), tempStartPos[i], timeElapsed / lerpDuration);
                }
                yield return null;
            }
            //-------
            for (int i = 0; i < interractors.Length; i++)
            {
                interractors[i].transform.localPosition = tempStartPos[i];
            }
        }
        IEnumerator melting(float speed)
        {
            if (GetComponent<Renderer>().material.GetInt("_Dont_Animate_Melting") == 0)
            {
                for (int i = 0; i < 100; i++)
                {
                    GetComponent<Renderer>().material.SetFloat("_Melt_Amount", GetComponent<Renderer>().material.GetFloat("_Melt_Amount") + 0.01f);
                    yield return new WaitForSeconds(0.1f - speed / 6);
                }
            }
        }
        IEnumerator delayReset(float delay)
        {
            yield return new WaitForSeconds(delay);
            idleEffect();
            set();
        }
        public enum effectTypes { DEFAULTS, breath, breathHeavy, jiggle, squeeze, flattenTop, pulsating, inflateSTATIC, serpent, fluid, melt, STOP }
        public enum vectorDir { X, Y, Z }
        #endregion
    }
    [System.Serializable]
    public class runEffectOnCommand
    {
        public int ID;
        public effectTypes runEffectOnCommandAction;
        [Range(0, 5)] public float doOnceDelay;
        public bool doOnce;
    }
}