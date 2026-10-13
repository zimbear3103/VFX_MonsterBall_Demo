using UnityEngine;

namespace TrailsFX.Demos {

	public class MoveObject : MonoBehaviour {

		Rigidbody rb;

		void Start () {
			rb = GetComponent<Rigidbody>();
		}

		void Update () {
			if (rb == null)
				return;

			Vector3 direction = Vector3.zero;
			if (InputProxy.GetKey(KeyCode.A)) {
				direction = Vector3.right;
			}
			if (InputProxy.GetKey(KeyCode.D)) {
				direction = Vector3.left;
			}
			if (InputProxy.GetKey(KeyCode.W)) {
				direction = Vector3.back;
			}
			if (InputProxy.GetKey(KeyCode.S)) {
				direction = Vector3.forward;
			}
			rb.AddForce(direction * 10);

		}
	}

}