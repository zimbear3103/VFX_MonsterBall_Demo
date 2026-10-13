
///	Copyright Unluck Software /// www.chemicalbliss.com																															

using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(FlockController))]
[CanEditMultipleObjects]
[System.Serializable]

public class FlockControllerEditor :Editor
{
	public SerializedProperty avoidanceMask;
	FlockController target_cs;

	public void OnEnable()
	{
		target_cs = (FlockController)target;
		avoidanceMask = serializedObject.FindProperty("_avoidanceMask");
	}

	public override void OnInspectorGUI()
	{
		GUIBeginBox();
		if (GUILayout.Button(GUIButtonText("Flock Properties", !EditorPrefs.GetBool("UnluckSoftware.BF.Properties")), EditorStyles.boldLabel))
			EditorPrefs.SetBool("UnluckSoftware.BF.Properties", !EditorPrefs.GetBool("UnluckSoftware.BF.Properties"));
		if (!EditorPrefs.GetBool("UnluckSoftware.BF.Properties"))
		{
			GUIBeginBox("", true, 2);
			target_cs._childPrefab = EditorGUILayout.ObjectField("Bird Prefab", target_cs._childPrefab, typeof(FlockChild), false) as FlockChild;
			EditorGUILayout.LabelField("Drag & Drop bird prefab from project folder", EditorStyles.miniLabel);
			GUIEndBox();
			GUIBeginBox("", true, 2);
			EditorGUILayout.LabelField("Roaming Area", EditorStyles.boldLabel);
			target_cs._positionSphere = EditorGUILayout.FloatField("Roaming Area Width", target_cs._positionSphere);
			if (target_cs._positionSphere < 0)
				target_cs._positionSphere = 0.0f;
			target_cs._positionSphereDepth = EditorGUILayout.FloatField("Roaming Area Depth", target_cs._positionSphereDepth);
			if (target_cs._positionSphereDepth < 0)
				target_cs._positionSphereDepth = 0.0f;
			target_cs._positionSphereHeight = EditorGUILayout.FloatField("Roaming Area Height", target_cs._positionSphereHeight);
			if (target_cs._positionSphereHeight < 0)
				target_cs._positionSphereHeight = 0.0f;
			GUIEndBox();
			GUIBeginBox("", true, 2);
			EditorGUILayout.LabelField("Flock Size", EditorStyles.boldLabel);
			target_cs._childAmount = (int)EditorGUILayout.Slider("Bird Amount", (float)target_cs._childAmount, 0.0f, 2000.0f);
			target_cs._spawnSphere = EditorGUILayout.FloatField("Flock Width", target_cs._spawnSphere);
			if (target_cs._spawnSphere < 0.01f)
				target_cs._spawnSphere = 0.01f;
			target_cs._spawnSphereDepth = EditorGUILayout.FloatField("Flock Depth", target_cs._spawnSphereDepth);
			if (target_cs._spawnSphereDepth < 0.01f)
				target_cs._spawnSphereDepth = 0.01f;
			target_cs._spawnSphereHeight = EditorGUILayout.FloatField("Flock Height", target_cs._spawnSphereHeight);
			if (target_cs._spawnSphereHeight < 0.01f)
				target_cs._spawnSphereHeight = 0.01f;
			target_cs._startPosOffset = EditorGUILayout.Vector3Field("Start Position Offset", target_cs._startPosOffset);
			target_cs._slowSpawn = EditorGUILayout.Toggle("Slowly Spawn Birds", target_cs._slowSpawn);
			GUIEndBox();
			GUIBeginBox("", true, 2);
			target_cs._skipFrame = EditorGUILayout.Toggle("Skip Frame", target_cs._skipFrame);
			EditorGUILayout.LabelField("Run script every other frame to increase performance.", EditorStyles.miniLabel);
			GUIEndBox();
		}
		GUIEndBox();

		GUIBeginBox();
		if (GUILayout.Button(GUIButtonText("Behaviors and Appearance", !EditorPrefs.GetBool("UnluckSoftware.BF.Behaviour")), EditorStyles.boldLabel))
			EditorPrefs.SetBool("UnluckSoftware.BF.Behaviour", !EditorPrefs.GetBool("UnluckSoftware.BF.Behaviour"));
		if (!EditorPrefs.GetBool("UnluckSoftware.BF.Behaviour"))
		{
			GUIBeginBox("", true, 2);	
			EditorGUILayout.LabelField("Change how the birds move and behave", EditorStyles.miniLabel);
			target_cs._minSpeed = EditorGUILayout.FloatField("Birds Min Speed", target_cs._minSpeed);
			target_cs._maxSpeed = EditorGUILayout.FloatField("Birds Max Speed", target_cs._maxSpeed);
			target_cs._diveValue = EditorGUILayout.FloatField("Birds Dive Depth", target_cs._diveValue);
			target_cs._diveFrequency = EditorGUILayout.Slider("Birds Dive Chance", target_cs._diveFrequency, 0.0f, .7f);
			target_cs._soarFrequency = EditorGUILayout.Slider("Birds Soar Chance", target_cs._soarFrequency, 0.0f, 1.0f);
			target_cs._soarMaxTime = EditorGUILayout.FloatField("Soar Time (0 = Always)", target_cs._soarMaxTime);
			target_cs._minDamping = EditorGUILayout.FloatField("Min Damping Turns", target_cs._minDamping);
			target_cs._maxDamping = EditorGUILayout.FloatField("Max Damping Turns", target_cs._maxDamping);
			EditorGUILayout.LabelField("Bigger number = faster turns", EditorStyles.miniLabel);
			target_cs._minScale = EditorGUILayout.FloatField("Birds Min Scale", target_cs._minScale);
			target_cs._maxScale = EditorGUILayout.FloatField("Birds Max Scale", target_cs._maxScale);
			EditorGUILayout.LabelField("Randomize size of birds when added", EditorStyles.miniLabel);
			GUIEndBox();
			GUIBeginBox("", true, 2);
			target_cs.LimitPitchRotation = EditorGUILayout.Toggle("Disable Pitch Rotation", target_cs.LimitPitchRotation);
			EditorGUILayout.LabelField("Flattens out rotation when flying or soaring upwards", EditorStyles.miniLabel);
			if (target_cs.LimitPitchRotation)
			{
				target_cs._flatSoar = EditorGUILayout.Toggle("Flat Soar", target_cs._flatSoar);
				target_cs._flatFly = EditorGUILayout.Toggle("Flat Flap", target_cs._flatFly);
			}
			GUIEndBox();
			GUIBeginBox("", true, 2);
			EditorGUILayout.LabelField("Bird Trigger Flock Waypoint", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Birds own waypoit triggers a new flock waypoint", EditorStyles.miniLabel);
			target_cs._childTriggerPos = EditorGUILayout.Toggle("Bird Trigger Waypoint", target_cs._childTriggerPos);
			target_cs._waypointDistance = EditorGUILayout.FloatField("Distance To Waypoint", target_cs._waypointDistance);
			GUIEndBox();
			GUIBeginBox("", true, 2);
			EditorGUILayout.LabelField("Automatic Flock Waypoint", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Automaticly change the flock waypoint (0 = never)", EditorStyles.miniLabel);
			target_cs._randomPositionTimer = EditorGUILayout.FloatField("Auto Waypoint Delay", target_cs._randomPositionTimer);
			if (target_cs._randomPositionTimer < 0)
			{
				target_cs._randomPositionTimer = 0.0f;
			}
			GUIEndBox();
			GUIBeginBox("", true, 2);
			EditorGUILayout.LabelField("Force Bird Waypoints", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Force all birds to change waypoints when flock changes waypoint", EditorStyles.miniLabel);
			target_cs._forceChildWaypoints = EditorGUILayout.Toggle("Force Bird Waypoints", target_cs._forceChildWaypoints);
			target_cs._forcedRandomDelay = (float)EditorGUILayout.IntField("Bird Waypoint Delay", (int)target_cs._forcedRandomDelay);
			GUIEndBox();
		}
		GUIEndBox();

		GUIBeginBox();
		if (GUILayout.Button(GUIButtonText("Animations", EditorPrefs.GetBool("UnluckSoftware.BF.Animations")), EditorStyles.boldLabel))
			EditorPrefs.SetBool("UnluckSoftware.BF.Animations", !EditorPrefs.GetBool("UnluckSoftware.BF.Animations"));
		if (EditorPrefs.GetBool("UnluckSoftware.BF.Animations"))
		{
			GUIBeginBox("", true, 2);
			target_cs._soarAnimation = EditorGUILayout.TextField("Soar Animation", target_cs._soarAnimation);
			target_cs._flapAnimation = EditorGUILayout.TextField("Flap Animation", target_cs._flapAnimation);
			target_cs._idleAnimation = EditorGUILayout.TextField("Idle Animation", target_cs._idleAnimation);
			target_cs._minAnimationSpeed = EditorGUILayout.FloatField("Min Anim Speed", target_cs._minAnimationSpeed);
			target_cs._maxAnimationSpeed = EditorGUILayout.FloatField("Max Anim Speed", target_cs._maxAnimationSpeed);
			GUIEndBox();
		}
		GUIEndBox();

		GUIBeginBox();
		if (GUILayout.Button(GUIButtonText("Avoidance", EditorPrefs.GetBool("UnluckSoftware.BF.Avoidance")), EditorStyles.boldLabel))
			EditorPrefs.SetBool("UnluckSoftware.BF.Avoidance", !EditorPrefs.GetBool("UnluckSoftware.BF.Avoidance"));
		if (EditorPrefs.GetBool("UnluckSoftware.BF.Avoidance"))
		{
			GUIBeginBox("", true, 2);
			EditorGUILayout.LabelField("Avoidance", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Birds will steer away from colliders (Ray)", EditorStyles.miniLabel);
			target_cs._birdAvoid = EditorGUILayout.Toggle("Bird Avoid", target_cs._birdAvoid);
			if (target_cs._birdAvoid)
			{
				EditorGUILayout.PropertyField(avoidanceMask, new GUIContent("Collider Mask"));
				target_cs._birdAvoidHorizontalForce = (int)EditorGUILayout.FloatField("Avoid Horizontal Force", (float)target_cs._birdAvoidHorizontalForce);
				target_cs._birdAvoidDistanceMin = EditorGUILayout.FloatField("Avoid Distance Min", target_cs._birdAvoidDistanceMin);
				target_cs._birdAvoidDistanceMax = EditorGUILayout.FloatField("Avoid Distance Max", target_cs._birdAvoidDistanceMax);
				target_cs._birdAvoidDown = EditorGUILayout.Toggle("Avoid Colliders Under", target_cs._birdAvoidDown);
				target_cs._birdAvoidUp = EditorGUILayout.Toggle("Avoid Colliders Over", target_cs._birdAvoidUp);
				if (target_cs._birdAvoidDown || target_cs._birdAvoidUp)
				{
					target_cs._birdAvoidVerticalForce = (int)EditorGUILayout.FloatField("Avoid Vertical Force", (float)target_cs._birdAvoidVerticalForce);
				}
				Undo.RecordObject(target_cs, "Bird Flock : Undo");
			}
			GUIEndBox();
		}
		GUIEndBox();

		GUIBeginBox();
		if (GUILayout.Button(GUIButtonText("Grouping", EditorPrefs.GetBool("UnluckSoftware.BF.Grouping")), EditorStyles.boldLabel))
			EditorPrefs.SetBool("UnluckSoftware.BF.Grouping", !EditorPrefs.GetBool("UnluckSoftware.BF.Grouping"));
		if (EditorPrefs.GetBool("UnluckSoftware.BF.Grouping"))
		{
			GUIBeginBox("", true, 2);
			EditorGUILayout.LabelField("Move birds into a parent transform", EditorStyles.miniLabel);
			target_cs._groupChildToFlock = EditorGUILayout.Toggle("Group to Flock", target_cs._groupChildToFlock);
			if (target_cs._groupChildToFlock)
			{
				GUI.enabled = false;
			}
			target_cs._groupChildToNewTransform = EditorGUILayout.Toggle("Group to New GameObject", target_cs._groupChildToNewTransform);
			target_cs._groupName = EditorGUILayout.TextField("Group Name", target_cs._groupName);
			GUI.enabled = true;
			GUIEndBox();
		}
		GUIEndBox();

		if (target_cs._forcedRandomDelay < 0)
		{
			target_cs._forcedRandomDelay = 0.0f;
		}
		if (GUI.changed)
		{
			serializedObject.ApplyModifiedProperties();
			serializedObject.Update();
			EditorUtility.SetDirty(target_cs);
		}
	}

	string GUIButtonText(string s, bool b)
	{
		if (b) return "= " + s;
		return "+ " + s;
	}
	
	void GUIBeginBox(string label = "", bool white = false, int s = 0)
	{
		if (white)
		{
			if (EditorGUIUtility.isProSkin)
				GUI.color = new Color(1.8f, 1.8f, 1.8f);
			else
				GUI.color = new Color(1.2f, 1.2f, 1.2f);
		} else
		{
			if (EditorGUIUtility.isProSkin)
				GUI.color = new Color(.55f, .55f, .55f);
			else
				GUI.color = new Color(.95f, .95f, .95f);
		}
		GUIStyle b = new GUIStyle("Box");
		EditorGUILayout.BeginVertical(b);
		GUI.color = Color.white;
		if (label != "") EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
		if (s > 0) GUILayout.Space(s);
	}
	
	static void GUIEndBox()
	{
		EditorGUILayout.EndVertical();
	}
}