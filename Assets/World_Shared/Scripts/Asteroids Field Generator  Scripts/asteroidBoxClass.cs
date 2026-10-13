using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(asteroidProperties))]
public class asteroidBoxClass : asteroidProperties
{
	public float maxRotSpeed = 0.25f;

	public float maxMoveSpeed = 1.5f;

	public float minSize;
	public float maxSize;

	public Transform parent;

	public void asteroidBoxConstructor(float maxRotSpeed, float maxMoveSpeed, float minSize, float maxSize, Transform parent, GameObject breakVfx)
	{
		this.maxRotSpeed = maxRotSpeed;
		this.maxMoveSpeed = maxMoveSpeed;
		this.minSize = minSize;
		this.maxSize = maxSize;
		this.parent = parent;
		this.breakVfx = breakVfx;
	}

	void Start()
    {
		rb.mass = mass * transform.localScale.x;
		rb.AddTorque(new Vector3(Random.Range(-maxRotSpeed, maxRotSpeed), Random.Range(-maxRotSpeed, maxRotSpeed), Random.Range(-maxRotSpeed, maxRotSpeed)), ForceMode.VelocityChange);
		rb.linearVelocity = new Vector3(Random.Range(-maxMoveSpeed, maxMoveSpeed), Random.Range(-maxMoveSpeed, maxMoveSpeed), Random.Range(-maxMoveSpeed, maxMoveSpeed));
	}
}
