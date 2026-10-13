using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class asteroidProperties : MonoBehaviour
{
	public float mass = 5f;
	protected GameObject breakVfx;
	public Rigidbody rb;

    void Awake()
    {
		rb = GetComponent<Rigidbody>();
		rb.angularDamping = 0f;
		rb.linearDamping = 0f;
		rb.useGravity = false;
	}

	void BreakAsteroid()
	{
		GameObject breakParticles = Instantiate(breakVfx, transform.position, transform.rotation);
		Destroy(breakParticles, 6f);
		Destroy(this.gameObject);
		//Instantiate here the broken asteroid
	}

	void OnCollisionEnter(Collision other)
	{
		if(other.gameObject.GetComponent<Rigidbody>())
		{
			if(other.gameObject.GetComponent<Rigidbody>().linearVelocity.magnitude > 55f)
			{
				//Future versions: break and particles
				//Colliding with another highspeed rigidboy
			}
			else if (rb.linearVelocity.magnitude > 55f)
			{
				//Coliding in highspeed with another rigidbody
			}
		}
		else if(rb.linearVelocity.magnitude > 55f)
		{
			//Colliding at highspeed with any other object
		}
	}

}
