using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SimplePlayerMove : MonoBehaviour
{
    public float speed = 5f;
    private Rigidbody rb;
    private Plane groundPlane;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        groundPlane = new Plane(Vector3.up, Vector3.zero); // Plane song song mặt đất (Y)
    }

    void FixedUpdate()
    {
        Move();
        RotateToMouse();
    }

    void Move()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 move = new Vector3(h, 0, v).normalized * speed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);
    }

    void RotateToMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (groundPlane.Raycast(ray, out float distance))
        {
            Vector3 point = ray.GetPoint(distance);
            Vector3 lookDir = point - transform.position;
            lookDir.y = 0;

            if (lookDir != Vector3.zero)
            {
                Quaternion rotation = Quaternion.LookRotation(lookDir);
                rb.MoveRotation(rotation);
            }
        }
    }
}
