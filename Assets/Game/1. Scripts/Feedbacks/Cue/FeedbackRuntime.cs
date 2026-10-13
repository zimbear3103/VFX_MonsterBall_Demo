// FeedbackRuntime.cs
// Runtime backbone for the FeedbackCue system: a persistent coroutine host,
// a simple prefab pool (replacement for MMF_InstantiateObject + MMMiniObjectPooler),
// a time controller for hitstop / slow-motion, and a lightweight one-shot audio player.
// Feel-independent: this file references no MoreMountains types.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Aiara
{
    // Persistent, hidden MonoBehaviour used to run coroutines and manage global time restore.
    public class FeedbackRuntimeHost : MonoBehaviour
    {
        private static FeedbackRuntimeHost _instance;

        public static FeedbackRuntimeHost Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("[FeedbackRuntime]");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<FeedbackRuntimeHost>();
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        public Coroutine Run(IEnumerator routine)
        {
            return StartCoroutine(routine);
        }
    }

    // Marker + lifetime handler for pooled instances. Restarts particle systems on spawn
    // and returns itself to the pool after its lifetime elapses.
    public class PooledInstance : MonoBehaviour
    {
        public GameObject Origin;
        private ParticleSystem[] _particles;
        private bool _cached;
        private Coroutine _returnRoutine;

        public void BeginLifetime(float lifetime)
        {
            if (!_cached)
            {
                _particles = GetComponentsInChildren<ParticleSystem>(true);
                _cached = true;
            }
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] == null) continue;
                _particles[i].Clear(true);
                _particles[i].Play(true);
            }
            if (_returnRoutine != null) StopCoroutine(_returnRoutine);
            if (lifetime > 0f) _returnRoutine = StartCoroutine(ReturnAfter(lifetime));
        }

        private IEnumerator ReturnAfter(float lifetime)
        {
            yield return new WaitForSeconds(lifetime);
            _returnRoutine = null;
            VFXPool.Return(gameObject, Origin);
        }
    }

    // Minimal prefab pool. Spawn reuses a deactivated instance or instantiates a new one.
    public static class VFXPool
    {
        private class PoolEntry
        {
            public readonly Stack<GameObject> Free = new Stack<GameObject>();
        }

        private static readonly Dictionary<GameObject, PoolEntry> _pools = new Dictionary<GameObject, PoolEntry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _pools.Clear();
        }

        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale, float lifetime, bool pooled)
        {
            if (prefab == null) return null;

            GameObject go = null;
            PoolEntry entry = null;
            if (pooled)
            {
                if (!_pools.TryGetValue(prefab, out entry))
                {
                    entry = new PoolEntry();
                    _pools[prefab] = entry;
                }
                while (entry.Free.Count > 0 && go == null)
                {
                    go = entry.Free.Pop();
                }
            }

            if (go == null)
            {
                go = Object.Instantiate(prefab);
                PooledInstance created = go.GetComponent<PooledInstance>();
                if (created == null) created = go.AddComponent<PooledInstance>();
                created.Origin = pooled ? prefab : null;
            }

            Transform tr = go.transform;
            tr.SetParent(null, false);
            tr.position = position;
            tr.rotation = rotation;
            tr.localScale = scale;
            go.SetActive(true);

            PooledInstance pi = go.GetComponent<PooledInstance>();
            if (pi != null) pi.BeginLifetime(lifetime);
            return go;
        }

        public static void Return(GameObject go, GameObject prefab)
        {
            if (go == null) return;
            go.SetActive(false);
            if (prefab != null && _pools.TryGetValue(prefab, out PoolEntry entry))
            {
                entry.Free.Push(go);
            }
        }
    }

    // Global time controller for hitstop (freeze frame) and slow motion.
    // Non-stacking: concurrent requests never add up — a single driver applies the STRONGEST
    // (lowest) instantaneous scale each frame, so multi-hit swings / multiple victims cannot
    // deepen or extend each other beyond the strongest single request.
    // Uses realtime so it keeps running while Time.timeScale is lowered.
    public static class FeedbackTime
    {
        private class TimeRequest
        {
            public float Scale;
            public float EaseIn;
            public float Hold;
            public float EaseOut;
            public float StartTime; // realtimeSinceStartup
            public float TotalDuration => EaseIn + Hold + EaseOut;
        }

        private static float _baseScale = 1f;
        private static readonly List<TimeRequest> _requests = new List<TimeRequest>();
        private static Coroutine _driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _baseScale = 1f;
            _requests.Clear();
            _driver = null;
        }

        // Freeze frame: timeScale snaps to freezeScale for duration (realtime), then restores.
        public static void Hitstop(float duration, float freezeScale = 0f)
        {
            if (duration <= 0f) return;
            AddRequest(Mathf.Clamp01(freezeScale), 0f, duration, 0f);
        }

        // Slow motion: eases timeScale to targetScale (easeIn), holds for duration, eases back (easeOut).
        public static void SlowMotion(float targetScale, float duration, float easeIn = 0.02f, float easeOut = 0.2f)
        {
            if (duration <= 0f && easeIn <= 0f && easeOut <= 0f) return;
            AddRequest(Mathf.Clamp01(targetScale), Mathf.Max(0f, easeIn), Mathf.Max(0f, duration), Mathf.Max(0f, easeOut));
        }

        private static void AddRequest(float scale, float easeIn, float hold, float easeOut)
        {
            _requests.Add(new TimeRequest
            {
                Scale = scale,
                EaseIn = easeIn,
                Hold = hold,
                EaseOut = easeOut,
                StartTime = Time.realtimeSinceStartup
            });
            if (_driver == null) _driver = FeedbackRuntimeHost.Instance.Run(DriverRoutine());
        }

        // Single writer for Time.timeScale. Each request contributes its instantaneous eased value;
        // the effective scale is the minimum of all active requests (strongest wins, never additive).
        private static IEnumerator DriverRoutine()
        {
            while (_requests.Count > 0)
            {
                float now = Time.realtimeSinceStartup;
                float effective = _baseScale;
                for (int i = _requests.Count - 1; i >= 0; i--)
                {
                    TimeRequest r = _requests[i];
                    float t = now - r.StartTime;
                    if (t >= r.TotalDuration)
                    {
                        _requests.RemoveAt(i);
                        continue;
                    }
                    float v;
                    if (t < r.EaseIn) v = Mathf.Lerp(_baseScale, r.Scale, t / r.EaseIn);
                    else if (t < r.EaseIn + r.Hold) v = r.Scale;
                    else v = Mathf.Lerp(r.Scale, _baseScale, (t - r.EaseIn - r.Hold) / r.EaseOut);
                    if (v < effective) effective = v;
                }
                Time.timeScale = effective;
                yield return null;
            }
            Time.timeScale = _baseScale;
            _driver = null;
        }
    }

    // Non-stacking gamepad rumble. Concurrent requests don't add up — each frame the driver
    // applies the strongest low/high motor speed among active requests, then zeroes the motors
    // when the last request expires (so overlapping rumbles can't cut each other short).
    public static class FeedbackRumble
    {
        private class RumbleRequest
        {
            public float Low;
            public float High;
            public float EndTime; // realtimeSinceStartup
        }

        private static readonly List<RumbleRequest> _requests = new List<RumbleRequest>();
        private static Coroutine _driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _requests.Clear();
            _driver = null;
        }

        public static void Play(float lowFrequency, float highFrequency, float duration)
        {
            if (duration <= 0f) return;
            if (!RumbleStep.Authorized) return;
            _requests.Add(new RumbleRequest
            {
                Low = Mathf.Clamp01(lowFrequency),
                High = Mathf.Clamp01(highFrequency),
                EndTime = Time.realtimeSinceStartup + duration
            });
            if (_driver == null) _driver = FeedbackRuntimeHost.Instance.Run(DriverRoutine());
        }

        private static IEnumerator DriverRoutine()
        {
            while (_requests.Count > 0)
            {
                float now = Time.realtimeSinceStartup;
                float low = 0f, high = 0f;
                for (int i = _requests.Count - 1; i >= 0; i--)
                {
                    RumbleRequest r = _requests[i];
                    if (now >= r.EndTime)
                    {
                        _requests.RemoveAt(i);
                        continue;
                    }
                    if (r.Low > low) low = r.Low;
                    if (r.High > high) high = r.High;
                }
                var pad = UnityEngine.InputSystem.Gamepad.current;
                if (pad != null) pad.SetMotorSpeeds(low, high);
                yield return null;
            }
            var endPad = UnityEngine.InputSystem.Gamepad.current;
            if (endPad != null) endPad.SetMotorSpeeds(0f, 0f);
            _driver = null;
        }
    }

    // Lightweight one-shot audio via a small round-robin pool of AudioSources.
    public static class FeedbackAudio
    {
        private static AudioSource[] _sources;
        private static int _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _sources = null;
            _next = 0;
        }

        public static void Play(AudioClip clip, float volume)
        {
            if (clip == null) return;
            EnsureSources();
            AudioSource src = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private static void EnsureSources()
        {
            if (_sources != null && _sources[0] != null) return;
            Transform host = FeedbackRuntimeHost.Instance.transform;
            _sources = new AudioSource[6];
            for (int i = 0; i < _sources.Length; i++)
            {
                AudioSource s = host.gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _sources[i] = s;
            }
        }
    }
}
