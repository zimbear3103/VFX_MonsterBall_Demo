using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MirzaBeig.CinematicExplosions
{
    public class Billboard : MonoBehaviour
    {
        public Vector3 offset;

        void Start()
        {

        }

        void Update()
        {
            transform.forward = -Camera.main.transform.forward;
            transform.Rotate(offset, Space.Self);
        }
    }
}

