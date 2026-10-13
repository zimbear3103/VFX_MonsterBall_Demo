using UnityEngine;

public class BalloonBuoyancy : MonoBehaviour
{
    public float buoyancyForce = 10f;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        rb.AddForce(Vector3.up * buoyancyForce, ForceMode.Force);
    }
}
