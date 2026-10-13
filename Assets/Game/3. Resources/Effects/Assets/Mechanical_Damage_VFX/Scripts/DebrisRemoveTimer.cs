using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MechDamage
{

    public class DebrisRemoveTimer : MonoBehaviour
    {
        [SerializeField] private float cogSizeMin = 0.2f;
        [SerializeField] private float cogSizeMax = 1f;
        [SerializeField] private float gravityScale = 1f;

        private float cogSizeRandomiser;

        private float dustDurationTimer = 0f;

        private bool removingDebris = false;

        private Rigidbody rb;

        private float prevTimeScale = 1;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            rb.linearDamping = 0;
            rb.mass = Random.Range(0.01f, 1f);
            rb.useGravity = false;
            rb.detectCollisions = true;
            cogSizeRandomiser = Random.Range(cogSizeMin, cogSizeMax);
            transform.localScale = new Vector3(cogSizeRandomiser, cogSizeRandomiser, cogSizeRandomiser);
            transform.Rotate(Random.Range(0, 360), Random.Range(0, 360), Random.Range(0, 360));
            rb.AddForce(Random.Range(-1f, 1f), Random.Range(1f, 3f), Random.Range(-1f, 1f), ForceMode.Impulse);
            rb.AddTorque(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f), ForceMode.VelocityChange);

        }

        private void FixedUpdate()
        {
            rb.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);
        }

        void Update()
        {
            dustDurationTimer += Time.deltaTime;

            if (dustDurationTimer > 2.5f)
            {

                if (removingDebris == false)
                {

                    StartCoroutine("ScaleToZero");

                }

            }

        }

        private IEnumerator ScaleToZero()
        {
            removingDebris = true;

            float ratio = 0;
            float duration = 1;
            float start_time = Time.time; 
            Vector3 initial_scale_value = transform.localScale;
            do
            {
                yield return new WaitForEndOfFrame();
                ratio = (Time.time - start_time) / duration;
                transform.localScale = Vector3.Lerp(initial_scale_value, Vector3.zero, ratio); 
            } 
            
            while (ratio < 1);  

            Destroy(gameObject);

        }

    }

}
