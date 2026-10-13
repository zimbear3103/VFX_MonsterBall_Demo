using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class NPC_05Effect : MonoBehaviour
{
    [SerializeField] private ParticleSystem HitEffect;
    public void EffectEvent()
    {
        HitEffect.Play();
    }
}
