using UnityEngine;
using UnityEngine.VFX;

public class LevelUpEffectLoop : MonoBehaviour
{
    public Material material; // Attach an instance of the material
    public VisualEffect vfx; // VisualEffect component for VFX effects

    private string scalarPropertyName = "_Power"; // Name of the exposed property for scalar value
    private string alphaPropertyName = "_Alpha"; // Name of the exposed property for alpha
    private string boolPropertyName = "_Active"; // Name of the exposed boolean in Shader Graph

    public float startValue = 3.0f; // Initial value
    public float finishValue = 0.5f; // Target value
    public float duration = 0.3f; // Animation duration
    public float alphaSlideDuration = 0.2f; // Duration of alpha animation
    public float loopDelay = 1.0f; // Delay between repetitions

    private void Start()
    {
        StartCoroutine(LevelUpLoop());
    }

    private System.Collections.IEnumerator LevelUpLoop()
    {
        while (true)
        {
            // Enable effect in Shader Graph
            material.SetInt(boolPropertyName, 1);

            // Enable effect in VFX Graph
            if (vfx != null)
            {
                vfx.SendEvent("OnLevelUp");
            }

            // Alpha animation: from 0 to 1
            yield return StartCoroutine(ChangeAlphaOverTime(0f, 1f, alphaSlideDuration));

            // Regular animation from start value to finish value
            yield return StartCoroutine(ChangeMaterialValueOverTime(startValue, finishValue, duration));

            // Return from finish value to start value
            yield return StartCoroutine(ChangeMaterialValueOverTime(finishValue, startValue, duration));

            // Alpha animation: from 1 to 0
            yield return StartCoroutine(ChangeAlphaOverTime(1f, 0f, alphaSlideDuration));

            // Disable effect in Shader Graph
            material.SetInt(boolPropertyName, 0);

            // Disable effect in VFX Graph
            if (vfx != null)
            {
                vfx.SendEvent("OnLevelUpEnd");
            }

            // Delay before next cycle
            yield return new WaitForSeconds(loopDelay);
        }
    }

    private System.Collections.IEnumerator ChangeMaterialValueOverTime(float fromValue, float toValue, float time)
    {
        float elapsedTime = 0f;

        while (elapsedTime < time)
        {
            elapsedTime += Time.deltaTime;
            float newValue = Mathf.Lerp(fromValue, toValue, elapsedTime / time);
            material.SetFloat(scalarPropertyName, newValue);
            yield return null;
        }

        material.SetFloat(scalarPropertyName, toValue);
    }

    private System.Collections.IEnumerator ChangeAlphaOverTime(float fromAlpha, float toAlpha, float time)
    {
        float elapsedTime = 0f;

        while (elapsedTime < time)
        {
            elapsedTime += Time.deltaTime;
            float newAlpha = Mathf.Lerp(fromAlpha, toAlpha, elapsedTime / time);
            material.SetFloat(alphaPropertyName, newAlpha);
            yield return null;
        }

        material.SetFloat(alphaPropertyName, toAlpha);
    }
}