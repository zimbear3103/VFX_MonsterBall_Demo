using UnityEngine;

public class BirdMaterialChanger : MonoBehaviour {
	public Material[] materials;
	static int counter;

	void Start () {
		ChangeMaterial();
	}
	
	void ChangeMaterial () {
		SkinnedMeshRenderer r = GetComponentInChildren<SkinnedMeshRenderer>();
		r.sharedMaterial = materials[counter];
		counter++;
	}
}
