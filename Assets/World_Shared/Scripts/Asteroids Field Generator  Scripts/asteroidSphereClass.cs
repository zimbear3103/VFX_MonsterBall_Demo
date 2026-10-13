using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(asteroidProperties))]
public class asteroidSphereClass : asteroidProperties
{
	public float maxRotSpeed = 0.25f;

	public float minSize;
	public float maxSize;

	public Transform parent;

	public void asteroidSphereConstructor(float maxRotSpeed, float minSize, float maxSize, Transform parent, GameObject breakVfx)
	{
		this.maxRotSpeed = maxRotSpeed;
		this.minSize = minSize;
		this.maxSize = maxSize;
		this.parent = parent;
		this.breakVfx = breakVfx;
	}

	void Start()
    {
		rb.mass = mass * transform.localScale.x;
		rb.AddTorque(new Vector3(Random.Range(-maxRotSpeed, maxRotSpeed), Random.Range(-maxRotSpeed, maxRotSpeed), Random.Range(-maxRotSpeed, maxRotSpeed)), ForceMode.VelocityChange);
	}

}
