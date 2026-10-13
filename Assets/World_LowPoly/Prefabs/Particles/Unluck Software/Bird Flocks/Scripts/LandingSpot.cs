
///	Copyright Unluck Software /// www.chemicalbliss.com																															

using UnityEngine;
using System.Collections;
public class LandingSpot :MonoBehaviour
{
	[HideInInspector]
	public FlockChild _landingChild;
	[HideInInspector]
	public bool _landing;
	public LandingSpotController _controller;
	public Transform _transformCache;                    // Reference to transform component
	public Vector3 _preLandWaypoint;
	public float _distance;


	public void Start()
	{
		if (_transformCache == null) _transformCache = transform;
		if (_controller == null)
			_controller = _transformCache.parent.GetComponent<LandingSpotController>();
		if (_controller._autoCatchDelay.x > 0)
			StartCoroutine(GetFlockChild(_controller._autoCatchDelay.x, _controller._autoCatchDelay.y));
		if (_controller._randomRotate && _controller._parentBirdToSpot)
		{
			Debug.LogWarning(_controller + "\nEnabling random rotate and parent bird to spot is not yet available, disabling random rotate");
			_controller._randomRotate = false;
		}
	}
#if UNITY_EDITOR
	public void OnDrawGizmos()
	{
		if (_transformCache == null) _transformCache = transform;
		if (_controller == null)
			_controller = _transformCache.parent.GetComponent<LandingSpotController>();
		if (!_controller._drawGizmos) return;
		Gizmos.color = Color.yellow;
		// Draw a yellow cube at the transforms position
		if ((_landingChild != null) && _landing)
			Gizmos.DrawLine(_transformCache.position, _landingChild._thisT.position);
		if (_transformCache.rotation.eulerAngles.x != 0 || _transformCache.rotation.eulerAngles.z != 0)
			_transformCache.eulerAngles = new Vector3(0.0f, _transformCache.eulerAngles.y, 0.0f);
		Gizmos.DrawCube(new Vector3(_transformCache.position.x, _transformCache.position.y, _transformCache.position.z), Vector3.one * _controller._gizmoSize);
		Gizmos.DrawCube(_transformCache.position + (_transformCache.forward * _controller._gizmoSize), Vector3.one * _controller._gizmoSize * .5f);
		if(_preLandWaypoint != Vector3.zero)
		{
			Gizmos.color = Color.blue;
			Gizmos.DrawCube(_transformCache.position + _preLandWaypoint, Vector3.one * _controller._gizmoSize * .25f);
		}
		if (_transformCache.parent.GetChild(0) != _transformCache) return;
		Gizmos.color = new Color(1.0f, 1.0f, 0.0f, 1f);
		Gizmos.DrawWireSphere(_transformCache.position, _controller._maxBirdDistance);
		Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 1f);
		Gizmos.DrawWireSphere(_transformCache.position, _controller._minBirdDistance);
	}
#endif
	public IEnumerator GetFlockChild(float minDelay, float maxDelay)
	{
		yield return new WaitForSeconds(Random.Range(minDelay, maxDelay));
		if (_controller._flock.gameObject.activeInHierarchy && (_landingChild == null))
		{
			FlockChild foundChild = null;   //Used to tell if a bird has been found to land
			for (int i = 0; i < _controller._flock._roamers.Count; i++)
			{
				FlockChild child = _controller._flock._roamers[i];
				if (child != null && !child._landing && child.gameObject.activeInHierarchy)
				{
					child._landingSpot = this;
					_distance = Vector3.Distance(child._thisT.position, _transformCache.position);
					if (!_controller._onlyBirdsAbove)
					{
						if ((foundChild == null) && _controller._maxBirdDistance > _distance && _controller._minBirdDistance < _distance)
						{
							foundChild = child;
							if (!_controller._takeClosest) break;
						} else if ((foundChild != null) && Vector3.Distance(foundChild._thisT.position, _transformCache.position) > _distance)
						{
							foundChild = child;
						}
					} else
					{
						if ((foundChild == null) && child._thisT.position.y > _transformCache.position.y && _controller._maxBirdDistance > _distance && _controller._minBirdDistance < _distance)
						{
							foundChild = child;
							if (!_controller._takeClosest) break;
						} else if ((foundChild != null) && child._thisT.position.y > _transformCache.position.y && Vector3.Distance(foundChild._thisT.position, _transformCache.position) > _distance)
						{
							foundChild = child;
						}
					}
				}
			}
			if (foundChild != null)
			{
				if (_controller._abortLanding) Invoke("ReleaseFlockChild", _controller._abortLandingTimer);
				_landingChild = foundChild;
				if (_controller._parentBirdToSpot) _landingChild.transform.SetParent(this.transform);
				_landing = true;
				_landingChild._landing = true;
				if (_controller._autoDismountDelay.x > 0) Invoke("ReleaseFlockChild", Random.Range(_controller._autoDismountDelay.x, _controller._autoDismountDelay.y));
				_controller._activeLandingSpots++;
				RandomRotate();
			} else if (_controller._autoCatchDelay.x > 0)
			{
				StartCoroutine(GetFlockChild(_controller._autoCatchDelay.x, _controller._autoCatchDelay.y));
			}
		}
	}
	public void InstantLand()
	{
		if (_controller._flock.gameObject.activeInHierarchy && (_landingChild == null))
		{

			FlockChild foundChild = null;

			for (int i = 0; i < _controller._flock._roamers.Count; i++)
			{
				FlockChild child = _controller._flock._roamers[i];
				if (!child._landing)// && !child._dived)
				{
					foundChild = child;
				}
			}
			if (foundChild != null)
			{
				//	Debug.Log("Instant Land");
				_landingChild = foundChild;

				if (_controller._parentBirdToSpot) _landingChild.transform.SetParent(this.transform);
				_landingChild._move = false;

				foundChild._speed = 0;
				foundChild._targetSpeed = 0;
				foundChild._landingSpot = this;
				_landing = true;
				_controller._activeLandingSpots++;
				_landingChild._landing = true;

				_landingChild._thisT.position = _landingChild.GetLandingSpotPosition();

				if (_controller._randomRotate) _landingChild._thisT.Rotate(Vector3.up, Random.Range(0f, 360f));
				else _landingChild._thisT.rotation = _transformCache.rotation;

				//_landingChild._thisT.position = _transformCache.position + _landingChild._landingPosOffset * _landingChild._thisT.localScale.y;

				if (!_landingChild._animationIsBaked)
				{
					if (!_landingChild._animator) _landingChild._modelAnimation.Play(_landingChild._spawner._idleAnimation);
					else _landingChild._animator.Play(_landingChild._spawner._idleAnimation);
				} else _landingChild._bakedAnimator.SetAnimation(2, -1);
				
				if (_controller._autoDismountDelay.x > 0) Invoke("ReleaseFlockChild", Random.Range(_controller._autoDismountDelay.x, _controller._autoDismountDelay.y));
			} else if (_controller._autoCatchDelay.x > 0)
			{
				StartCoroutine(GetFlockChild(_controller._autoCatchDelay.x, _controller._autoCatchDelay.y));
			}
		}
	}
	public void ReleaseFlockChild()
	{
		if (_controller._flock.gameObject.activeInHierarchy && _landingChild != null)
		{

			//	Debug.Log("Release");
			
			EmitFeathers();
			//landingChild._modelT.eulerAngles = new Vector3(0, 0, 0);
			_landingChild._modelT.localEulerAngles = new Vector3(0, 0, 0);
			_landing = false;
			_landingChild._avoid = true;
			_landingChild._closeToSpot = false;
			//Reset flock child to flight mode
			_landingChild._damping = _landingChild._spawner._maxDamping;
			_landingChild._targetSpeed = Random.Range(_landingChild._spawner._minSpeed, _landingChild._spawner._maxSpeed);
			_landingChild._move = true;
			_landingChild._landing = false;
			_landingChild.currentAnim = "";
			_landingChild.Flap(.1f);

			//if (!landingChild._animationIsBaked)
			//{
			//	if (!landingChild._animator) landingChild._modelAnimation.CrossFade(landingChild._spawner._flapAnimation, .2f);
			//	else landingChild._animator.CrossFade(landingChild._spawner._flapAnimation, .2f);
			//} else landingChild._bakedAnimator.SetAnimation(0, -1);
			//landingChild._dived = true;
			//	landingChild._speed = 0f;


			if (_controller._parentBirdToSpot) _landingChild._spawner.AddChildToParent(_landingChild._thisT);
			//	landingChild.Flap();
			_landingChild._wayPoint = new Vector3(_landingChild._wayPoint.x+5, _transformCache.position.y + 5, _landingChild._wayPoint.z+5);
			_landingChild._damping = 0.1f;
			if (_controller._autoCatchDelay.x > 0)
			{
				StartCoroutine(GetFlockChild(_controller._autoCatchDelay.x + 0.1f, _controller._autoCatchDelay.y + 0.1f));
			}
			_controller._activeLandingSpots--;
			if (_controller._abortLanding)
			{
				CancelInvoke("ReleaseFlockChild");
				if (_landingChild.currentAnim != "Idle") _landingChild.FindWaypoint();
			}
			_landingChild = null;

		}
	}

	public void RandomRotate()
	{
		if (_controller._randomRotate)
		{
			Quaternion rot = _transformCache.rotation;
			Vector3 rotE = rot.eulerAngles;
			rotE.y = Random.Range(-180, 180);
			rot.eulerAngles = rotE;
			_transformCache.rotation = rot;
		}
	}

	public void EmitFeathers()
	{
		if (_controller._featherPS == null) return;
		_controller._featherPS.position = _landingChild._thisT.position;
		_controller._featherParticles.Emit(Random.Range(0, 3));
	}
}