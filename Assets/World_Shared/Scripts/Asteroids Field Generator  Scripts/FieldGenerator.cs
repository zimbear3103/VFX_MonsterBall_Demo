using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[ExecuteInEditMode]
public class FieldGenerator : MonoBehaviour
{
	public enum type { Box, Torus, Sphere };
	#region Variables

	[Space(10f)]
	[Header("Main Properties")]
	public GameObject[] asteroidPrefabs;
	public int k = 100;
	[Space]
	public float asteroidsFieldDensity;
	[Tooltip("Minimal distance between Asteroids")]
	public float minDistance = 20f;
	public float minAsteroidSize = 0.5f;
	public float maxAsteroidSize = 3f;
	float maxRotSpeed = 0.25f;
	[Tooltip("LayerMask to define which objects the asteroids won't overlap on spawn.")]
	public LayerMask asteroidCollisionLayers;
	public GameObject breakVfx;

	[Space(10f)]
	[Header("Field Properties")]
	public type AsteroidFieldType = new type();
	[SerializeField, HideInInspector]
	public bool asteroidsFieldCreated = false;
	private int childCount;

	public BoxProp boxProperties;

	public TorusProp torusProperties;

	public SphereProp sphereProperties;

	[Space(10f)]
	[Header("Debug Properties")]
	public bool highlightAsteroids = false;
	[Tooltip("Array to show how many asteroids were created by it index")]
	public int[] asteroidsCreated;

	[Space(10f)]
	[SerializeField, HideInInspector]
	public List<GameObject> asteroidsGenerated = new List<GameObject>();

	private Collider[] colliders;

	#endregion
	void Update()
	{
		if (transform.childCount < 1 && asteroidsFieldCreated)
		{
			asteroidsGenerated.Clear();
			for (int i = 0; i < asteroidsCreated.Length; i++)
			{
				asteroidsCreated = new int[0];
			}
			asteroidsFieldCreated = false;
		}
	}

	GameObject RandomAsteroid()
	{
		int index;
		GameObject go = asteroidPrefabs[index = Random.Range(0, asteroidPrefabs.Length)];

		for (int i = 0; i < asteroidPrefabs.Length; i++)
		{
			if (index == i)
			{
				asteroidsCreated[i] += 1;
			}
		}
		return go;
	}

	//Call this method to generate an asteroid field during runtime
	public void CustomGenerateFunc(type type)
	{
		if (!asteroidsFieldCreated)
		{
			Generate(type);
		}
		else
		{
			Debug.Log("This object already has an Asteroid Field created. Delete the field using the delete button, or manually destroy the AsteroidCointainer gameobject.");
		}
	}

	[ContextMenu("Generate Field")]
	public void GenerateFunc()
	{
		if (!asteroidsFieldCreated)
		{
			Generate(AsteroidFieldType);
		}
		else
		{
			Debug.Log("This object already has an Asteroid Field created. Delete the field using the delete button, or manually destroy the AsteroidCointainer gameobject.");
		}
	}

	public void Generate(type type)
	{
		asteroidsCreated = new int[asteroidPrefabs.Length];
		Vector3 positionToSpawn = Vector3.zero;
		Vector3 finalPos = Vector3.zero;
		float minorDistance = Mathf.Infinity;
		bool aloneAsteroid = false;
		GameObject asteroidsContainer = new GameObject("AsteroidContainer");
		asteroidsContainer.transform.position = this.transform.position;
		asteroidsContainer.transform.rotation = this.transform.rotation;
		asteroidsContainer.transform.SetParent(this.transform);
		asteroidsFieldCreated = true;
		int nBeforeRejection = 0;
		int rejectedCandidates = 0;

		for (int i = 0; i < asteroidsFieldDensity; i++)//Repeat the iteration process based on asteroidsChainDensity, informed by user
		{
			bool canGen = false;
			nBeforeRejection = 0;
			if (i != 0)//If this is not first asteroid
			{
				do
				{
					minorDistance = Mathf.Infinity;
					Random.InitState((int)System.DateTime.Now.Ticks);
					switch (type)
					{
						case type.Box:
							positionToSpawn = BoxSpawner();
							finalPos = transform.position + positionToSpawn;

							break;
						case type.Torus:
							positionToSpawn = TorusSpawner();
							finalPos = transform.position + positionToSpawn;
							break;
						case type.Sphere:
							positionToSpawn = SphereSpawner();
							finalPos = transform.position + positionToSpawn;
							break;
					}

					foreach (GameObject go in asteroidsGenerated)
					{
						if (Vector3.Distance(go.transform.position, finalPos) < minorDistance)
						{
							minorDistance = Vector3.Distance(go.transform.position, finalPos);
						}
					}

					colliders = Physics.OverlapSphere(finalPos, minDistance, asteroidCollisionLayers);

					if ((minorDistance > minDistance && colliders.Length < 1) || (aloneAsteroid && colliders.Length < 1))
					{
						canGen = true;
					}

					nBeforeRejection++;
					if (nBeforeRejection >= k)
					{
						rejectedCandidates++;
						break;
					}
				} while (!canGen);
				if (!canGen)
					continue;
			}
			else//IF ITS FIRST
			{
				do
				{
					Random.InitState((int)System.DateTime.Now.Ticks);
					switch (type)
					{
						case type.Box:
							positionToSpawn = BoxSpawner();
							finalPos = transform.position + positionToSpawn;

							break;
						case type.Torus:
							positionToSpawn = TorusSpawner();
							finalPos = transform.position + positionToSpawn;
							break;
						case type.Sphere:
							positionToSpawn = SphereSpawner();
							finalPos = transform.position + positionToSpawn;
							break;
					}
					colliders = Physics.OverlapSphere(finalPos, minDistance, asteroidCollisionLayers);

				} while (colliders.Length > 1);
				canGen = true;
			}
			GameObject asteroid = Instantiate(RandomAsteroid(), transform.position + positionToSpawn, Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)));
			float scale = Random.Range(minAsteroidSize, maxAsteroidSize);
			asteroid.transform.SetParent(asteroidsContainer.transform);
			asteroid.transform.localScale = new Vector3(scale, scale, scale);
			switch (type)
			{
				case type.Box:
					asteroid.AddComponent<asteroidBoxClass>().asteroidBoxConstructor(maxRotSpeed, boxProperties.maxMoveSpeed, minAsteroidSize, maxAsteroidSize, asteroidsContainer.transform, breakVfx);
					break;
				case type.Torus:
					asteroid.AddComponent<asteroidTorusClass>().asteroidTorusConstructor(maxRotSpeed, minAsteroidSize, maxAsteroidSize, asteroidsContainer.transform,
						torusProperties.minOrbitalSpeed, torusProperties.maxOrbitalSpeed, torusProperties.clockwise, breakVfx);
					break;
				case type.Sphere:
					asteroid.AddComponent<asteroidSphereClass>().asteroidSphereConstructor(maxRotSpeed, minAsteroidSize, maxAsteroidSize, asteroidsContainer.transform, breakVfx);
					break;
			}
			asteroidsGenerated.Add(asteroid);
			minorDistance = Mathf.Infinity;
			aloneAsteroid = false;
		}
		print(rejectedCandidates + " asteroids were rejected by K factor.");
	}

	Vector3 BoxSpawner()
	{
		Vector3 worldOffset = new Vector3(Random.Range(-boxProperties.asteroidsChainSize.x, boxProperties.asteroidsChainSize.x), Random.Range(-boxProperties.asteroidsChainSize.y, boxProperties.asteroidsChainSize.y),
												Random.Range(-boxProperties.asteroidsChainSize.z, boxProperties.asteroidsChainSize.z));
		worldOffset = transform.rotation * worldOffset;
		return worldOffset;
	}

	Vector3 TorusSpawner()
	{
		float x;
		float y;
		float z;

		do
		{
			float randomRadius = Random.Range(torusProperties.innerRadius, torusProperties.outerRadius);
			float randomRadian = Random.Range(0, (2 * Mathf.PI));

			x = randomRadius * Mathf.Cos(randomRadian);
			y = Random.Range(-(torusProperties.height / 2), (torusProperties.height / 2));
			z = randomRadius * Mathf.Sin(randomRadian);

		} while (float.IsNaN(z) && float.IsNaN(x));

		Vector3 localPos = new Vector3(x, y, z);
		Vector3 worldOffset = transform.rotation * localPos;
		//Vector3 worldPos = transform.position + worldOffset;

		return worldOffset;
	}

	Vector3 SphereSpawner()
	{
		Vector3 worldOffset;

		do
		{
			worldOffset = new Vector3(Random.Range(-sphereProperties.outerSphereRadius, sphereProperties.outerSphereRadius), Random.Range(-sphereProperties.outerSphereRadius, sphereProperties.outerSphereRadius), Random.Range(-sphereProperties.outerSphereRadius, sphereProperties.outerSphereRadius));
		} while (Vector3.Distance(transform.position + worldOffset, transform.position) < sphereProperties.innerSphereRadius || Vector3.Distance(transform.position + worldOffset, transform.position) > sphereProperties.outerSphereRadius);

		return worldOffset;
	}

	void OnDrawGizmosSelected()
	{

		switch (AsteroidFieldType)
		{
			case type.Box:
				Gizmos.matrix = transform.localToWorldMatrix;
				Gizmos.color = Color.green;
				Gizmos.DrawWireCube(Vector3.zero, boxProperties.asteroidsChainSize * 2);
				break;
			case type.Torus:
				Gizmos.matrix = transform.localToWorldMatrix;
				Transform T = GetComponent<Transform>();
				float outerRadius = torusProperties.outerRadius;
				float innerRadius = torusProperties.innerRadius;
				float height = torusProperties.height;

				Gizmos.color = Color.green;
				float theta = 0;
				float outerX = outerRadius * Mathf.Cos(theta);
				float outerY = outerRadius * Mathf.Sin(theta);

				float innerX = innerRadius * Mathf.Cos(theta);
				float innerY = innerRadius * Mathf.Sin(theta);

				float heightRadius = innerRadius + ((outerRadius - innerRadius) / 2);
				float upperX = heightRadius * Mathf.Cos(theta);
				float upperY = heightRadius * Mathf.Sin(theta);

				//Outer radius
				Vector3 outerPos = Vector3.zero + new Vector3(outerX, 0, outerY);
				Vector3 outerNewPos = outerPos;
				Vector3 outerLastPos = outerPos;

				//Inner radius
				Vector3 innerPos = Vector3.zero + new Vector3(innerX, 0, innerY);
				Vector3 innerNewPos = innerPos;
				Vector3 innerLastPos = innerPos;
				//UpHeight
				Vector3 upperPos = Vector3.zero + new Vector3(innerX + (outerX - innerX) / 2, height / 2, innerY + (outerY - innerY) / 2);
				Vector3 upperNewPos = upperPos;
				Vector3 upperLastPos = upperPos;
				//DownHeigh
				Vector3 downPos = Vector3.zero + new Vector3(innerX + (outerX - innerX) / 2, -height / 2, innerY + (outerY - innerY) / 2);
				Vector3 downNewPos = downPos;
				Vector3 downLastPos = downPos;

				for (theta = 0.1f; theta < Mathf.PI * 2; theta += 0.1f)
				{
					outerX = outerRadius * Mathf.Cos(theta);
					outerY = outerRadius * Mathf.Sin(theta);
					outerNewPos = Vector3.zero + new Vector3(outerX, 0, outerY);

					innerX = innerRadius * Mathf.Cos(theta);
					innerY = innerRadius * Mathf.Sin(theta);
					innerNewPos = Vector3.zero + new Vector3(innerX, 0, innerY);

					upperX = heightRadius * Mathf.Cos(theta);
					upperY = heightRadius * Mathf.Sin(theta);
					upperNewPos = Vector3.zero + new Vector3(upperX, height / 2, upperY);

					downNewPos = Vector3.zero + new Vector3(upperX, -height / 2, upperY);

					Gizmos.color = Color.green;
					Gizmos.DrawLine(outerPos, outerNewPos);
					Gizmos.DrawLine(innerPos, innerNewPos);
					Gizmos.DrawLine(upperPos, upperNewPos);
					Gizmos.DrawLine(downPos, downNewPos);
					outerPos = outerNewPos;
					innerPos = innerNewPos;
					upperPos = upperNewPos;
					downPos = downNewPos;
				}
				Gizmos.DrawLine(outerPos, outerLastPos);
				Gizmos.DrawLine(innerPos, innerLastPos);
				Gizmos.DrawLine(upperPos, upperLastPos);
				Gizmos.DrawLine(downPos, downLastPos);
				break;
			case type.Sphere:
				Gizmos.color = Color.green;
				Gizmos.DrawWireSphere(transform.position, sphereProperties.outerSphereRadius);
				Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
				Gizmos.DrawSphere(transform.position, sphereProperties.innerSphereRadius);
				break;
		}

		if (asteroidsGenerated.Count > 0 && highlightAsteroids)
		{

			Gizmos.color = Color.green;

			foreach (GameObject go in asteroidsGenerated)
			{
				Gizmos.matrix = go.transform.localToWorldMatrix;
				Gizmos.DrawWireSphere(Vector3.zero, minDistance / go.transform.localScale.x);
			}
		}

	}

	[ContextMenu("Delete Field")]
	public void DeleteField()
	{
		if (asteroidsFieldCreated)
		{
			for (int i = 0; i < asteroidsCreated.Length; i++)
			{
				asteroidsCreated = new int[0];
			}
			asteroidsGenerated.Clear();
			DestroyImmediate(transform.Find("AsteroidContainer").gameObject);
			asteroidsFieldCreated = false;
		}
	}

	[System.Serializable]
	public struct BoxProp
	{
		public Vector3 asteroidsChainSize;
		public float maxMoveSpeed;
	}

	[System.Serializable]
	public struct TorusProp
	{
		public float innerRadius;
		public float outerRadius;
		[Range(1f, 10000f)]
		public float height;
		public float minOrbitalSpeed;
		public float maxOrbitalSpeed;
		public bool clockwise;
	}

	[System.Serializable]
	public struct SphereProp
	{
		public float innerSphereRadius;
		public float outerSphereRadius;
	}
}
