using Aiara;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HS_ProjectileMover : MonoBehaviour
{
    [SerializeField] public float speed = 15f;
    [SerializeField] protected float hitOffset = 0f;
    [SerializeField] protected bool UseFirePointRotation;
    [SerializeField] protected Vector3 rotationOffset = new Vector3(0, 0, 0);
    [SerializeField] protected GameObject hit;
    [SerializeField] protected ParticleSystem hitPS;
    [SerializeField] protected GameObject flash;
    [SerializeField] protected Rigidbody rb;
    [SerializeField] protected Collider col;
    [SerializeField] protected Light lightSourse;
    [SerializeField] protected GameObject[] Detached;
    [SerializeField] protected ParticleSystem projectilePS;
    private bool startChecker = false;
    [SerializeField]protected bool notDestroy = false;

    public Transform distortionEffect;
    public Transform targetTransform;
    //public AttackInfo attackInfo;

    protected virtual void Start()
    {
        if (!startChecker)
        {
            /*lightSourse = GetComponent<Light>();
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            if (hit != null)
                hitPS = hit.GetComponent<ParticleSystem>();*/
            if (flash != null)
            {
                flash.transform.parent = null;
                Destroy(flash, 5);
            }
        }
        if (notDestroy)
            StartCoroutine(DisableTimer(5));
        else
            Destroy(gameObject, 5);
        startChecker = true;
    }

    protected virtual IEnumerator DisableTimer(float time)
    {
        yield return new WaitForSeconds(time);
        if(gameObject.activeSelf)
            gameObject.SetActive(false);
        yield break;
    }

    protected virtual void OnEnable()
    {
        if (startChecker)
        {
            if (flash != null)
            {
                flash.transform.parent = null;
                Destroy(flash, 5);
            }
            if (lightSourse != null)
                lightSourse.enabled = true;
            col.enabled = true;
            rb.constraints = RigidbodyConstraints.None;
        }
    }

    protected virtual void FixedUpdate()
    {
        if (speed != 0)
        {
            if (targetTransform != null)
            {
                var direction = (targetTransform.position - transform.position).normalized;
                rb.linearVelocity = direction * speed;
            }
            else
            {
                rb.linearVelocity = transform.forward * speed;
            }
        }
    }

    //protected virtual void OnCollisionEnter(Collision collision)
    //{
    //    //Lock all axes movement and rotation
    //    rb.constraints = RigidbodyConstraints.FreezeAll;
    //    //speed = 0;
    //    if (lightSourse != null)
    //        lightSourse.enabled = false;
    //    col.enabled = false;
    //    projectilePS.Stop();
    //    projectilePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    //    ContactPoint contact = collision.contacts[0];
    //    Quaternion rot = Quaternion.FromToRotation(Vector3.up, contact.normal);
    //    Vector3 pos = contact.point + contact.normal * hitOffset;

    //    //Spawn hit effect on collision
    //    if (hit != null)
    //    {
    //        hit.transform.rotation = rot;
    //        hit.transform.position = pos;
    //        if (UseFirePointRotation) { hit.transform.rotation = gameObject.transform.rotation * Quaternion.Euler(0, 180f, 0); }
    //        else if (rotationOffset != Vector3.zero) { hit.transform.rotation = Quaternion.Euler(rotationOffset); }
    //        else { hit.transform.LookAt(contact.point + contact.normal); }
    //        hitPS.Play();
    //    }

    //    //Removing trail from the projectile on cillision enter or smooth removing. Detached elements must have "AutoDestroying script"
    //    foreach (var detachedPrefab in Detached)
    //    {
    //        if (detachedPrefab != null)
    //        {
    //            ParticleSystem detachedPS = detachedPrefab.GetComponent<ParticleSystem>();
    //            detachedPS.Stop();
    //        }
    //    }
    //    if (notDestroy)
    //        StartCoroutine(DisableTimer(hitPS.main.duration));
    //    else
    //    {
    //        if (hitPS != null)
    //        {
    //            Destroy(gameObject, hitPS.main.duration);
    //        }
    //        else
    //            Destroy(gameObject, 1);
    //    }
    //}

    private void OnTriggerEnter(Collider other)
    {
        /*var hitbox = other.GetComponent<HitBox>();

        if (hitbox != null)
        {
            //if (hitbox.TargetEntity == attackInfo.attacker)
            //    return;
            //Lock all axes movement and rotation
            rb.constraints = RigidbodyConstraints.FreezeAll;
            //speed = 0;
            if (lightSourse != null)
                lightSourse.enabled = false;
            col.enabled = false;
            if (distortionEffect != null)
            {
                distortionEffect.SetParent(null);
                Destroy(distortionEffect.gameObject, 1f);
            }
            projectilePS.Stop();
            projectilePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var contact = other.ClosestPoint(transform.position);

            //Spawn hit effect on collision
            if (hit != null)
            {
                hit.transform.LookAt(contact);
                hitPS.Play();
            }

            //Removing trail from the projectile on cillision enter or smooth removing. Detached elements must have "AutoDestroying script"
            foreach (var detachedPrefab in Detached)
            {
                if (detachedPrefab != null)
                {
                    ParticleSystem detachedPS = detachedPrefab.GetComponent<ParticleSystem>();
                    detachedPS.Stop();
                }
            }
            if (notDestroy)
                StartCoroutine(DisableTimer(hitPS.main.duration));
            else
            {
                if (hitPS != null)
                {
                    Destroy(gameObject, hitPS.main.duration);
                }
                else
                    Destroy(gameObject, 1);
            }

            //hitbox.OnDamaged(new DamageInfo(attackInfo, 1, contact, transform.position, attackInfo.attackDirection, transform.rotation));
        }*/
    }
}
