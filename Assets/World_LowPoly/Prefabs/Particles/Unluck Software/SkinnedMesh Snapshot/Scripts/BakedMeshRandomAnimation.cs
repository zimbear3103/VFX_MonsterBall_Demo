#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class BakedMeshRandomAnimation : MonoBehaviour {
	BakedMeshAnimator animator;

	void Start () {
		animator = GetComponent<BakedMeshAnimator>();
	}
	
	void Randomanimation () {
		animator.SetAnimation(Random.Range(0, animator.animations.Length));
		Invoke("Randomanimation", 2);
	}

	[CustomEditor(typeof(BakedMeshRandomAnimation))]
	public class BakedMeshRandomAnimationEditor :Editor
	{
		public override void OnInspectorGUI()
		{
			BakedMeshRandomAnimation m_target = (BakedMeshRandomAnimation)target;

			DrawDefaultInspector();

			for (int i = 0; i < m_target.GetComponent<BakedMeshAnimator>().animations.Length; i++)
			{
				if(GUILayout.Button("animation " + i))
				{
					m_target.GetComponent<BakedMeshAnimator>().SetAnimation(i);

				}
			}

			if (GUI.changed)
			{
				EditorUtility.SetDirty(m_target);
			}

			
			}

		}
	}
#endif

