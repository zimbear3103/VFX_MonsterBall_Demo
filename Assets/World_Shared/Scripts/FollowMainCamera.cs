using UnityEngine;

public class FollowMainCamera : MonoBehaviour
{
    Camera camera;

    private void Start()
    {
        camera = Camera.main;
    }

    void Update()
    {
        transform.position = camera.transform.position;
    }
}
