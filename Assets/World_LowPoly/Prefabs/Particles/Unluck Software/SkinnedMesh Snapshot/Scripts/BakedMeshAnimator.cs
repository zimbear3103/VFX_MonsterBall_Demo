using UnityEngine;
using System.Collections.Generic;

public class BakedMeshAnimator :MonoBehaviour
{
	public MeshRenderer animationMeshRenderer;
	public BakedMeshAnimation[] animations;
	public int startAnimation = 0;
	int currentAnimation = 0;
	MeshFilter meshFilter;
	public float currentFrame; //Mesh displayed
	int currentFrameInt;
	float currentSpeed; //Speed animation plays
	bool pingPongToggle;
	public float playSpeedMultiplier = 1f; //Only for use when scripting speed changes, to avoid having to save the original playSpeed.
	int meshCacheCount;
	public float transitionFailsafe = 0.4f; // Prevent infinite loop in case transitionframe is never played.
	float failsafe; // Counter to prevent getting stuck on a single frame
	int transitionFrame;
	int anim;
	bool transitioning = true;

	void Awake()
	{

		currentAnimation = Mathf.Clamp(startAnimation, 0, animations.Length - 1);
		if (animationMeshRenderer == null) animationMeshRenderer = GetComponent<MeshRenderer>();
		if (animationMeshRenderer == null)
		{
			Debug.LogError("BakedMeshAnimator: " + this + " has no assigned MeshRenderer!");
		}
		meshFilter = animationMeshRenderer.GetComponent<MeshFilter>();
		meshCacheCount = animations[currentAnimation].meshes.Length;
		currentSpeed = animations[currentAnimation].playSpeed;
		CreateCrossFadeMesh();
		StartCrossfade();

		if (meshFilter.sharedMesh == null) meshFilter.sharedMesh = animations[0].meshes[0];
#if UNITY_EDITOR
		if (animations.Length == 0)
			Debug.LogWarning(this.gameObject.name + "has no animations attached");
#endif
	}

	public void AnimateUpdate()
	{
		if (animations.Length == 0) return;
		Animate();
	}

	public void SetAnimation(int _animation, int _transitionFrame) //Set the transition frame manually.
	{
		if (currentAnimation == _animation) return;
		transitionFrame = _transitionFrame;
		anim = _animation;
		SetAnimCommon();
	}

	public void SetAnimation(int _animation) //Uses the transition frame set in the animation object.
	{
		if (currentAnimation == _animation) return;
		transitionFrame = animations[currentAnimation].transitionFrame;
		anim = _animation;
		SetAnimCommon();
	}

	void SetAnimCommon()
	{
		this.enabled = true;
		transitioning = true;
		StartCrossfade();
	}

	public void Animate()
	{
		if (!animationMeshRenderer.isVisible) return;
		if (transitioning)
		{
			if (crossfade || (int)currentFrame == transitionFrame || failsafe > transitionFailsafe)
			{
				failsafe = 0;
				transitioning = false;
				currentAnimation = anim;
				meshCacheCount = animations[currentAnimation].meshes.Length;
				currentSpeed = animations[currentAnimation].playSpeed;
				if (Time.time <1f && animations[currentAnimation].randomStartFrame) currentFrame = (int)Random.Range(meshCacheCount, 0);
				else if (crossfade) currentFrame = animations[currentAnimation].crossfadeFrame;
				else currentFrame = animations[currentAnimation].transitionFrame;
			} else
			{
				failsafe += Time.deltaTime;
			}
		}
		if (!doCrossfade)
		{
			if (animations[currentAnimation].pingPong) PingPongFrame();
			else NextFrame();
			if (currentFrameInt != (int)currentFrame)
			{
				currentFrameInt = (int)currentFrame;
				if (crossfade && crossfadeNormalFix)
				{
					animations[currentAnimation].meshes[(int)currentFrame].GetVertices(norms);
					crossfadeMeshEnd.SetVertices(norms);
				} else
				{
					meshFilter.sharedMesh = animations[currentAnimation].meshes[(int)currentFrame];
				}
			}
		}
		UpdateCrossfade();
	}

	public bool NextFrame()
	{
		currentFrame += currentSpeed * Time.deltaTime * playSpeedMultiplier;
		if (currentFrame > meshCacheCount + 1)
		{
			currentFrame = 0.0f;
			if (!animations[currentAnimation].loop) this.enabled = false;
			return true;
		}
		if (currentFrame >= meshCacheCount)
		{
			currentFrame = meshCacheCount - currentFrame;
			if (!animations[currentAnimation].loop) this.enabled = false;
			return true;
		}
		return false;
	}

	public bool PingPongFrame()
	{
		if (pingPongToggle)
			currentFrame += currentSpeed * Time.deltaTime * playSpeedMultiplier;
		else
			currentFrame -= currentSpeed * Time.deltaTime * playSpeedMultiplier;
		if (currentFrame <= 0)
		{
			currentFrame = 0.0f;
			pingPongToggle = true;
			return true;
		}
		if (currentFrame >= meshCacheCount)
		{
			pingPongToggle = false;
			currentFrame = (float)(meshCacheCount - 1);
			return true;
		}
		return false;
	}

	public void SetSpeedMultiplier(float speed)
	{
		playSpeedMultiplier = speed;
	}

	//CROSSFADE//
	public bool crossfade; // Morphs between animations, higher cpu load
	public bool crossfadeNormalFix; // Looks better but uses more cpu
	public float crossfadeFrequency = 0.05f; // How often crossfade updates (1 = 1 second aprox) (lower number = more cpu)
	public float crossfadeWeightAdd = .221f; // How much to crossfade eash update (1 = 100%)  (lower number = more cpu)
	bool doCrossfade;
	float crossfadeWeight = 1f;
	Mesh crossfadeMestTo;
	Mesh crossfadeMeshStart;
	Mesh crossfadeMeshEnd;
	List<Vector3> vertsStart = new List<Vector3>();
	List<Vector3> norms = new List<Vector3>();
	Vector3[] vertsCurrent;
	Vector3[] vertsDiff;
	List<Vector3> meshVerts = new List<Vector3>();
	float nextUpdate;

	void CrossfadeInit()
	{
		if (vertsDiff == null) vertsDiff = new Vector3[vertsStart.Count];
		crossfadeMestTo.GetVertices(meshVerts);
		for (int i = 0; i < vertsStart.Count; i++)
		{
			vertsDiff[i] = meshVerts[i] - vertsStart[i];
		}
	}

	void CreateCrossFadeMesh()
	{
		if (!crossfade) return;
		crossfadeMeshStart = meshFilter.sharedMesh;
		crossfadeMeshStart.GetVertices(vertsStart);
		if (crossfadeMeshEnd == null)
		{
			crossfadeMeshEnd = new Mesh();
			crossfadeMeshEnd.MarkDynamic();
			crossfadeMeshEnd.SetVertices(vertsStart);
			crossfadeMeshEnd.triangles = crossfadeMeshStart.triangles;
			crossfadeMeshEnd.uv = crossfadeMeshStart.uv;
			crossfadeMeshStart.GetNormals(norms);
			crossfadeMeshEnd.SetNormals(norms);
		}
	}

	void StartCrossfade()
	{
		if (!crossfade) return;
		crossfadeMeshStart = meshFilter.sharedMesh;
		crossfadeMeshStart.GetVertices(vertsStart);
		doCrossfade = true;
		crossfadeWeight = 0f;
		//CreateCrossFadeMesh();
		crossfadeMeshEnd.SetVertices(vertsStart);
		meshFilter.mesh = crossfadeMeshEnd;
		crossfadeMeshStart.GetVertices(vertsStart);
		if (vertsCurrent == null) vertsCurrent = new Vector3[vertsStart.Count];
	}

	void UpdateCrossfade()
	{
		if (!crossfade) return;
		nextUpdate += Time.deltaTime;
		if (nextUpdate < crossfadeFrequency) return;
		nextUpdate = 0f;
		if (crossfadeWeight >= 1f)
		{
			doCrossfade = false;
			return;
		}
		crossfadeMestTo = animations[currentAnimation].meshes[animations[currentAnimation].crossfadeFrame];
		if (crossfadeWeight == 0) CrossfadeInit();
		for (int i = 0; i < vertsCurrent.Length; i++)
		{
			vertsCurrent[i] = vertsStart[i];
		}
		if (vertsDiff.Length != vertsStart.Count)
		{
			return;
		}
		for (int i = 0; i < vertsCurrent.Length; i++)
		{
			vertsCurrent[i] += vertsDiff[i] * crossfadeWeight;
		}
		crossfadeMeshEnd.SetVertices(vertsCurrent);
		crossfadeWeight += crossfadeWeightAdd;
	}
}