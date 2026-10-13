using UnityEngine;
using System.Collections;


namespace MechDamage
{

    public class Damage_Explosion_Big : MonoBehaviour
    {
        public GameObject cog;
        public int cogCount = 5;
        public GameObject metalRod;
        public int metalCount = 10;
        public GameObject spring;
        public int springCount = 2;
        public Transform explodePosition;
        public bool loop = false;
        public float loopDelayMin = 1f;
        public float loopDelayMax = 1f;

        private GameObject cloneCog;
        private GameObject cloneMetalRod;
        private GameObject cloneSpring;

        private float loopTime = 0f;
        private float loopDelay = 1f;

        void Start()
        {
            StartCoroutine("Metal");
        }

        IEnumerator Metal()
        {
            int metalCount = 1;
            do
            {
                loopDelay = Random.Range(loopDelayMin, loopDelayMax);
                loopTime = 0f;

                while (loopTime < loopDelay)
                {
                    loopTime += Time.deltaTime;
                    yield return null;
                }

                metalCount = 1;

                while (metalCount < cogCount)
                {

                    cloneCog = Instantiate(cog, explodePosition.position, explodePosition.rotation) as GameObject;
                    cloneCog.SetActive(true);
                    metalCount++;

                }

                metalCount = 1;

                while (metalCount < this.metalCount)
                {

                    cloneMetalRod = Instantiate(metalRod, explodePosition.position, explodePosition.rotation) as GameObject;
                    cloneMetalRod.SetActive(true);
                    metalCount++;

                }

                metalCount = 1;

                while (metalCount < springCount)
                {

                    cloneSpring = Instantiate(spring, explodePosition.position, explodePosition.rotation) as GameObject;
                    cloneSpring.SetActive(true);
                    metalCount++;

                }

                yield return new WaitForSeconds(0.01f);
            }
            while (loop);

        }

    }

}