using UnityEngine;
using System.Collections;

public class BakedMeshAnimatorUpdater : MonoBehaviour {
	BakedMeshAnimator animatedMesh;
	BakedMeshAnimator[] children;

	public  bool updateChildren;
	public bool randomizeSpeed;
	public float minSpeedMultiplier = 1f;
	public float maxSpeedMultiplier = 1f;

	void Start () {
        if (updateChildren) {
			children = transform.GetComponentsInChildren<BakedMeshAnimator>();
			for (int i = 0; i < children.Length; i++) {
				if(randomizeSpeed) children[i].SetSpeedMultiplier(Random.Range(minSpeedMultiplier, maxSpeedMultiplier));
			//	children[i].AnimateUpdate();
			}
		} else {
			animatedMesh = GetComponent<BakedMeshAnimator>();
			if (randomizeSpeed) animatedMesh.SetSpeedMultiplier(Random.Range(minSpeedMultiplier, maxSpeedMultiplier));
		}		
	}
	
	void Update () {
		if (updateChildren) {
			for(int i = 0; i < children.Length; i++) {
				children[i].AnimateUpdate();
			}
		} else {
			 animatedMesh.AnimateUpdate();
		}
	}
}
