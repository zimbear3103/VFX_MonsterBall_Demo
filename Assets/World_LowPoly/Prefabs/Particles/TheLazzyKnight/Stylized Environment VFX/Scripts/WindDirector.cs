using System.Collections;
using System.Collections.Generic;
using UnityEngine.VFX;
using UnityEngine;

[ExecuteInEditMode]
public class WindDirector : MonoBehaviour
{
    Vector3 windRotation;
    Vector3 oldRotation;
    public float force = 1.0f;
    float oldForce;
    public GameObject[] windDirected;
    public VisualEffect[] fxes;
    Mesh Gizmo;
 

    void Awake()
    {
        AddVisualEffects();
    }

    public void AddVisualEffects()
    {
        windRotation = transform.localRotation.eulerAngles;

        windDirected = GameObject.FindGameObjectsWithTag("WindDirected");
        int windDirectedSize = windDirected.Length;
        fxes = new VisualEffect[windDirectedSize];
        for (int i = 0; i < windDirectedSize; i++)
        {
            fxes[i] = windDirected[i].GetComponent<VisualEffect>();
        }


        foreach (VisualEffect visualEffect in fxes)
        {
            {
                visualEffect.SetVector3("Wind Rotation", windRotation);
                visualEffect.SetFloat("Wind Force", force);
            }
        }
    }
    void Update()
    {
        windRotation = transform.localRotation.eulerAngles;


        if (windRotation != oldRotation)
        {
 
            oldRotation = windRotation;

            foreach (VisualEffect visualEffect in fxes)
            {
                {
                    visualEffect.SetVector3("Wind Rotation", windRotation);
                }
            }

           
        }

        if(force != oldForce)
        {

            oldForce = force;

            foreach (VisualEffect visualEffect in fxes)
            {
                {
                    visualEffect.SetFloat("Wind Force", force);
                }
            }


        }

    }

    void OnDrawGizmosSelected()
    {
        Gizmo = Resources.Load<Mesh>("WindArrow");
        Gizmos.color = new Vector4(0.3f, 0.86f, 0.99f, 1.0f);

        Gizmos.DrawMesh(Gizmo, transform.position, transform.rotation, new Vector3(2.0f, 2.0f, 2.0f));
    }

    void OnDrawGizmos()
    {

        Vector3 newHeight = new Vector3(0, 0.5f, 0);
        Vector3 oldHeight = transform.position;
        Vector3 iconGizmoPosition = oldHeight + newHeight;
        Gizmos.DrawIcon(iconGizmoPosition, "WindLogo128.png", true);
     
    }
}
