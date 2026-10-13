using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FPSCheck : MonoBehaviour
{
    [SerializeField] protected Text text;

    int fps = 0;
    float checkTime = 0f;

    void Update()
    {
        ++fps;
        checkTime += Time.deltaTime;

        if (checkTime >= 1f)
        {
            text.text = $"FPS: {fps}";
            fps = 0;
            checkTime -= 1f;
        }
    }
}
