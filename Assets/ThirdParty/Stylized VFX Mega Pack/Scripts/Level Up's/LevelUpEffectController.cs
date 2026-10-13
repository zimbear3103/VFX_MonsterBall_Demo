using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;

public class LevelUpEffectController : MonoBehaviour
{
    public Material material; // Attach an instance of the material
    public VisualEffect vfx; // VisualEffect component for VFX effects
    public Button triggerButton; // UI button

    private string scalarPropertyName = "_Power"; // Name of the exposed property for scalar value
    private string alphaPropertyName = "_Alpha"; // Name of the exposed property for alpha
    private string boolPropertyName = "_Active"; // Name of the exposed boolean in Shader Graph

    public float startValue = 3.0f; // Initial value
    public float finishValue = 0.5f; // Target value
    public float duration = 0.3f; // Animation duration

    public float alphaSlideDuration = 0.2f; // Duration of alpha animation

    private bool isEffectActive = false;

    private void Start()
    {
        if (triggerButton != null)
        {
            // Attach the TriggerLevelUp function to the button
            triggerButton.onClick.AddListener(TriggerLevelUp);
        }
        else
        {
            Debug.LogWarning("Button was not assigned in the inspector!");
        }
    }

    // Method to trigger the effect via Timeline or another script
    public void PressButton()
    {
        if (triggerButton != null)
        {
            triggerButton.onClick.Invoke(); // Simulate button click
        }
    }

    public void TriggerLevelUp()
    {
        if (!isEffectActive)
        {
            isEffectActive = true;

            // Enable the effect in VFX Graph
            if (vfx != null)
            {
                vfx.SendEvent("OnLevelUp"); // Send event to VFX Graph
            }

            // Start the full animation sequence
            StartCoroutine(FullAnimationSequence());
        }
    }

    private System.Collections.IEnumerator FullAnimationSequence()
    {
        // Enable effect in Shader Graph
        material.SetInt(boolPropertyName, 1);

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

        // Disable effect in VFX
        if (vfx != null)
        {
            vfx.SendEvent("OnLevelUpEnd"); // Send event to end the effect in VFX
        }

        // Reset flag
        isEffectActive = false;
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
