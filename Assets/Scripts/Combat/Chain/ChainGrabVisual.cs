using System.Collections;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 체인 그랩 비주얼 드라이버 — 구버전 CharacterChainGrab의 시각 연출부만 이식.
    /// Kyeoms FX ChainGrab 프리팹(ChainSkill_Throw 애니메이터 + "Grab" 트리거)을 소유하고
    /// 활성화 / 목표 조준 / 거리 기반 길이 스케일 / 손 분리 추종 / 회수 왜곡 / 종료 애니 대기 후
    /// 비활성만 담당한다. 게임플레이(대상 선정·데미지·상태)는 CharacterChainGrab이 소유한다.
    /// </summary>
    public class ChainGrabVisual : MonoBehaviour
    {
        [Header("Visual Object")]
        [Tooltip("체인 비주얼 루트(비우면 캐릭터 자식에서 이름으로 탐색)")]
        [SerializeField] private GameObject visualObject;
        [Tooltip("비주얼 자동 탐색 이름 (구버전 ChainGrabVisualObjectName)")]
        [SerializeField] private string visualObjectName = "ChainGrab";
        [Tooltip("체인 비주얼 애니메이터의 회수 트리거 (구버전 ChainGrabVisualTriggerName)")]
        [SerializeField] private string grabTriggerName = "Grab";

        [Header("Aiming")]
        [Tooltip("비주얼의 길이 축(로컬) — ChainSkill_Throw 자식이 X-90 회전이라 루트 기준 +Y로 뻗는다")]
        [SerializeField] private Vector3 localForwardAxis = new Vector3(0f, 1f, 0f);
        [Tooltip("조준 후 추가 회전 오프셋(Euler) (구버전 ChainVisualForwardOffsetEuler)")]
        [SerializeField] private Vector3 forwardOffsetEuler = Vector3.zero;
        [Tooltip("활성화 시 손에서 분리해 월드에 두고 원래 부모 위치만 추종(부모 스케일/회전 영향 제거)")]
        [SerializeField] private bool detachFromHand = true;
        [Tooltip("분리 추종 시 원래 부모 로컬 기준 시작 오프셋 (구버전 ChainVisualStartOffset)")]
        [SerializeField] private Vector3 startOffset = new Vector3(0f, 0.01f, -0.15f);

        [Header("Length Scale")]
        [Tooltip("거리(m)당 길이 축 스케일 (구버전 장휴 프리팹 튜닝값 0.06)")]
        [SerializeField] private float scalePerMeter = 0.06f;
        [Tooltip("길이 스케일 상수 오프셋")]
        [SerializeField] private float scaleOffset = 0f;
        [Tooltip("길이 스케일 하한")]
        [SerializeField] private float minScale = 0.02f;
        [Tooltip("길이 스케일 상한")]
        [SerializeField] private float maxScale = 3f;

        [Header("Retract")]
        [Tooltip("회수 왜곡 이펙트 자식 이름 (구버전 RetractDistortionName)")]
        [SerializeField] private string distortionName = "Distortion";
        [Tooltip("왜곡 이펙트 수명(초)")]
        [SerializeField] private float distortionDuration = 1f;
        [Tooltip("왜곡 이펙트 스폰 위치 오프셋(m) — 캐릭터 루트 로컬 방향 기준. 체인의 뻗은 회전/스케일 영향을 받지 않는다")]
        [SerializeField] private Vector3 distortionOffset = Vector3.zero;
        [Tooltip("켜면 왜곡 이펙트를 Distortion 자식의 현재 위치가 아니라 체인 루트 위치 + 오프셋에 스폰한다(체인이 길게 뻗은 채 회수돼도 손 근처에 고정)")]
        [SerializeField] private bool distortionAtRoot = false;
        [Tooltip("종료 애니메이션 대기 한도(초) (구버전 VisualEndMaxWait)")]
        [SerializeField] private float endMaxWait = 2f;

        private GameObject _resolved;
        private Animator _animator;
        private Transform _originalParent;
        private Vector3 _baseLocalScale = Vector3.one;
        private Vector3 _origLocalPos;
        private Quaternion _origLocalRot;
        private bool _detached;
        private bool _rollFlipped;
        private bool _grabTriggerFired;
        private bool _freezeAim;
        private Vector3 _frozenPos;
        private Coroutine _deferredRoutine;

        /// <summary>비주얼이 현재 켜져 있는가.</summary>
        public bool IsActive => _resolved != null && _resolved.activeInHierarchy;

        /// <summary>체인 루트(비주얼 시작점) 월드 위치. 비주얼이 없으면 캐릭터 위 1.2m.</summary>
        public Vector3 RootPosition
        {
            get
            {
                if (_resolved != null) return AnchorFollowPosition();
                return transform.position + Vector3.up * 1.2f;
            }
        }

        private void Awake()
        {
            ResolveVisual();
        }

        private void ResolveVisual()
        {
            if (_resolved != null) return;
            _resolved = visualObject;
            if (_resolved == null && !string.IsNullOrEmpty(visualObjectName))
            {
                var found = FindDeepChild(transform, visualObjectName);
                if (found != null) _resolved = found.gameObject;
            }
            if (_resolved == null) return;

            _originalParent = _resolved.transform.parent;
            _baseLocalScale = _resolved.transform.localScale;
            _origLocalPos = _resolved.transform.localPosition;
            _origLocalRot = _resolved.transform.localRotation;
            _animator = ResolveAnimator(_resolved);
            _resolved.SetActive(false);
        }

        /// <summary>체인 비주얼 활성화(발사 시). 손 분리·스케일 초기화 포함.</summary>
        public void Activate()
        {
            ResolveVisual();
            if (_resolved == null) return;
            StopDeferred();
            Reattach();
            _freezeAim = false;
            _rollFlipped = false;
            _grabTriggerFired = false;
            _resolved.SetActive(true);
            if (_animator != null && HasTrigger(_animator, grabTriggerName))
                _animator.ResetTrigger(grabTriggerName);
            if (detachFromHand) Detach();
        }

        /// <summary>
        /// 매 프레임(LateUpdate 권장) 목표 지점으로 조준.
        /// scaleByDistance=true(홀드 구간)에서만 거리 비례 길이 스케일을 적용하고,
        /// false(뻗기/당기기/회수)면 원본 스케일 유지 — FX 던지기 애니가 자연스럽게 늘어난다
        /// (구버전 ScaleChainVisualDuringExtendRetract=false 튜닝 이식).
        /// </summary>
        public void UpdateAim(Vector3 targetPos, bool scaleByDistance = true)
        {
            if (_resolved == null || !_resolved.activeSelf) return;
            var t = _resolved.transform;

            Vector3 rootPos = _detached ? AnchorFollowPosition() : t.position;
            if (_detached) t.position = _freezeAim ? _frozenPos : rootPos;

            if (_freezeAim)
            {
                if (scaleByDistance) ApplyScale(t, Vector3.Distance(t.position, targetPos));
                else t.localScale = _baseLocalScale;
                return;
            }

            Vector3 d = targetPos - t.position;
            float dist = d.magnitude;
            if (dist > 0.001f)
            {
                Vector3 dn = d / dist;
                // 좌우 반전 시 손바닥 뒤집힘 방지용 180도 롤(히스테리시스) — 구버전 이식.
                if (!_rollFlipped && dn.x < -0.05f) _rollFlipped = true;
                else if (_rollFlipped && dn.x > 0.02f) _rollFlipped = false;

                Vector3 refUp = Mathf.Abs(Vector3.Dot(dn, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
                Quaternion rot = Quaternion.LookRotation(dn, refUp)
                                 * Quaternion.FromToRotation(localForwardAxis, Vector3.forward);
                if (_rollFlipped) rot = Quaternion.AngleAxis(180f, dn) * rot;
                rot *= Quaternion.Euler(forwardOffsetEuler);
                t.rotation = rot;
            }
            if (scaleByDistance) ApplyScale(t, dist);
            else t.localScale = _baseLocalScale;
        }

        /// <summary>회수/종료 동안 현재 위치·회전을 고정한다(구버전 FreezeVisualAim).</summary>
        public void FreezeAim()
        {
            if (_resolved == null) return;
            _freezeAim = true;
            _frozenPos = _resolved.transform.position;
        }

        /// <summary>체인 비주얼 애니메이터에 회수 트리거를 쏜다(활성화 후 1회만 — 회수 애니 재재생 방지).</summary>
        public void FireGrabTrigger()
        {
            if (_grabTriggerFired) return;
            if (_animator != null && HasTrigger(_animator, grabTriggerName))
            {
                _animator.SetTrigger(grabTriggerName);
                _grabTriggerFired = true;
            }
        }

        /// <summary>회수 트리거 → 종료 애니 완료(또는 한도) 대기 → 왜곡 재생 → 비활성.</summary>
        public void DeactivateDeferred(bool playDistortion = true)
        {
            if (_resolved == null || !_resolved.activeInHierarchy) { DeactivateImmediate(); return; }
            StopDeferred();
            _deferredRoutine = StartCoroutine(DeferredRoutine(playDistortion));
        }

        /// <summary>즉시 비활성(강제 종료/정리용).</summary>
        public void DeactivateImmediate()
        {
            StopDeferred();
            if (_resolved == null) return;
            Reattach();
            _resolved.transform.localScale = _baseLocalScale;
            _resolved.SetActive(false);
            _freezeAim = false;
        }

        private IEnumerator DeferredRoutine(bool playDistortion)
        {
            // 트리거 발사 '전'의 상태를 기억해 둔다 — 던지기(Throw) 상태는 이미 끝나 있어
            // (normalizedTime>=1) 바로 "완료"로 오판하고 회수 애니를 자르는 버그 방지.
            int prevStateHash = 0;
            if (_animator != null)
                prevStateHash = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;

            FreezeAim();
            FireGrabTrigger();
            float start = Time.time;

            // 1) 회수(grab_end) 상태로 실제 전이될 때까지 대기.
            while (_animator != null && Time.time - start < endMaxWait)
            {
                var st = _animator.GetCurrentAnimatorStateInfo(0);
                if (_animator.IsInTransition(0) || st.shortNameHash != prevStateHash) break;
                yield return null;
            }
            // 2) 회수 상태가 끝까지 재생될 때까지 대기.
            while (_animator != null && Time.time - start < endMaxWait)
            {
                var st = _animator.GetCurrentAnimatorStateInfo(0);
                if (!_animator.IsInTransition(0) && st.shortNameHash != prevStateHash
                    && !st.loop && st.normalizedTime >= 0.99f) break;
                yield return null;
            }

            if (playDistortion) PlayRetractDistortion();
            _deferredRoutine = null;
            DeactivateImmediate();
        }

        /// <summary>회수 왜곡 이펙트 — 원본을 복제해 씬 루트에서 재생(부모 비활성에 안전).</summary>
        public void PlayRetractDistortion()
        {
            if (_resolved == null || string.IsNullOrEmpty(distortionName)) return;
            var template = FindDeepChild(_resolved.transform, distortionName);
            if (template == null) return;

            // 스폰 위치: 기본은 Distortion 자식의 현재 위치. distortionAtRoot면 체인 루트(손 노드) 기준.
            // 오프셋은 캐릭터(원래 부모) 로컬 방향으로 적용 — 체인의 뻗은 회전/스케일 영향을 받지 않는다.
            Vector3 basePos = distortionAtRoot ? AnchorFollowPosition() : template.position;
            Vector3 pos = basePos + transform.TransformDirection(distortionOffset);

            var clone = Instantiate(template.gameObject, pos, template.rotation);
            clone.transform.SetParent(null, true);
            clone.SetActive(true);
            foreach (var ps in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Play(true);
            }
            Destroy(clone, Mathf.Max(0.1f, distortionDuration));
        }

        private void ApplyScale(Transform t, float distance)
        {
            float len = Mathf.Clamp(distance * scalePerMeter + scaleOffset, minScale, maxScale);
            Vector3 axis = localForwardAxis.normalized;
            Vector3 s = _baseLocalScale;
            s.x = Mathf.Lerp(_baseLocalScale.x, len, Mathf.Abs(axis.x));
            s.y = Mathf.Lerp(_baseLocalScale.y, len, Mathf.Abs(axis.y));
            s.z = Mathf.Lerp(_baseLocalScale.z, len, Mathf.Abs(axis.z));
            t.localScale = s;
        }

        private Vector3 AnchorFollowPosition()
        {
            if (_originalParent == null) return _resolved != null ? _resolved.transform.position : transform.position;
            return _originalParent.position + _originalParent.TransformDirection(startOffset);
        }

        private void Detach()
        {
            if (_resolved == null || _detached) return;
            _resolved.transform.SetParent(null, true);
            _resolved.transform.position = AnchorFollowPosition();
            _detached = true;
        }

        private void Reattach()
        {
            if (_resolved == null || !_detached) return;
            _resolved.transform.SetParent(_originalParent, false);
            _resolved.transform.localPosition = _origLocalPos;
            _resolved.transform.localRotation = _origLocalRot;
            _resolved.transform.localScale = _baseLocalScale;
            _detached = false;
        }

        private void StopDeferred()
        {
            if (_deferredRoutine != null) { StopCoroutine(_deferredRoutine); _deferredRoutine = null; }
        }

        /// <summary>구버전 ResolveChainVisualAnimator 이식 — Grab 트리거 보유 > 이름에 ChainSkill 포함 > 컨트롤러 보유 순.</summary>
        private Animator ResolveAnimator(GameObject rootObj)
        {
            var all = rootObj.GetComponentsInChildren<Animator>(true);
            foreach (var a in all)
                if (a.runtimeAnimatorController != null && HasTrigger(a, grabTriggerName)) return a;
            foreach (var a in all)
                if (a.runtimeAnimatorController != null && a.gameObject.name.Contains("ChainSkill")) return a;
            foreach (var a in all)
                if (a.runtimeAnimatorController != null) return a;
            return all.Length > 0 ? all[0] : null;
        }

        private static bool HasTrigger(Animator anim, string paramName)
        {
            if (anim == null || string.IsNullOrEmpty(paramName)) return false;
            foreach (var p in anim.parameters)
                if (p.name == paramName && p.type == AnimatorControllerParameterType.Trigger) return true;
            return false;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.name == childName) return c;
                var r = FindDeepChild(c, childName);
                if (r != null) return r;
            }
            return null;
        }

        private void OnDisable()
        {
            DeactivateImmediate();
        }
    }
}
