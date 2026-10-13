using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UNI_ResetTransformOnStart : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        transform.parent = null;
        transform.position = new Vector3(0,0,0);
        transform.eulerAngles = new Vector3(0, 0, 0);
        transform.localScale = new Vector3(1, 1, 1);
    }

}
