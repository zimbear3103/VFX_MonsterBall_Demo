using UnityEngine;
public class BakedMeshAnimation :MonoBehaviour
{
	public Mesh[] meshes;
	public float playSpeed = 30f;
	[HideInInspector]
	public Renderer rendererComponent;
	public bool randomStartFrame = true;
	public bool loop = true;
	public bool pingPong;
	public bool playOnAwake = true;
	public Transform transformCache;
	// Wait for frame before next animation.
	public int transitionFrame;
	public int crossfadeFrame;
}