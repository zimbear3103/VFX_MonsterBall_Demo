using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(asteroidProperties))]
public class asteroidTorusClass : asteroidProperties
{
	public float maxRotSpeed = 0.25f;

	public float minSize;
	public float maxSize;

	public Transform parent;

	public float minOrbitalSpeed;
	public float maxOrbitalSpeed;
	public float fixedOrbitalSpeed;
	public bool clockwise;

	public void asteroidTorusConstructor(float maxRotSpeed, float minSize, float maxSize, Transform parent, float minOrbitalSpeed, float maxOrbitalSpeed, bool clockwise, GameObject breakVfx)
	{
		this.maxRotSpeed = maxRotSpeed;
		this.minSize = minSize;
		this.maxSize = maxSize;
		this.parent = parent;
		this.minOrbitalSpeed = minOrbitalSpeed;
		this.maxOrbitalSpeed = maxOrbitalSpeed;
		this.clockwise = clockwise;
		this.breakVfx = breakVfx;
	}

	void Start()
    {
		fixedOrbitalSpeed = Mathf.Abs((Random.Range(minOrbitalSpeed, maxOrbitalSpeed))/1000f);
		rb.mass = mass * transform.localScale.x;
		rb.AddTorque(new Vector3(Random.Range(-maxRotSpeed, maxRotSpeed), Random.Range(-maxRotSpeed, maxRotSpeed), Random.Range(-maxRotSpeed, maxRotSpeed)), ForceMode.VelocityChange);
	}

	void FixedUpdate()
	{
		if (!rb)
			return;

		if(clockwise)
			rotateRigidBodyAroundPointBy(rb, parent.position, parent.up, fixedOrbitalSpeed);
		else
			rotateRigidBodyAroundPointBy(rb, parent.position, parent.up, -fixedOrbitalSpeed);
	}

	public void rotateRigidBodyAroundPointBy(Rigidbody rb, Vector3 origin, Vector3 axis, float angle)
	{
		Quaternion q = Quaternion.AngleAxis(angle, axis);
		rb.MovePosition(q * (rb.transform.position - origin) + origin);
		rb.MoveRotation(rb.transform.rotation * q);
	}
}
