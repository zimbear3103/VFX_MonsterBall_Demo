using System.Collections;
using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화 동안에만 나타나는 보조 NPC. <b>플레이어 오브젝트의 자식</b>으로 달아두고 평소에는 꺼둔다.
    ///
    /// 대화가 시작되면 <see cref="DialogueControlLock"/>이 <see cref="BeginTalk"/>을 부른다 —
    /// <see cref="ShowDelay"/>만큼 기다렸다가 <b>그때 오브젝트를 켜고</b> 곧바로 Animator의 <c>Show</c>를 켠다.
    /// 딜레이를 두는 이유는 플레이어의 Talk 모션이 먼저 나가고 그 위에 보조 캐릭터가 얹혀야 하기 때문이고,
    /// 켜는 것까지 미루는 이유는 기다리는 동안 등장 전 포즈가 화면에 서 있으면 안 되기 때문이다.
    ///
    /// 그래서 <b>기다리는 시간은 스스로 셀 수 없다</b> — 꺼져 있는 오브젝트에서는 Update도 코루틴도 돌지 않는다.
    /// 대신 대화를 여는 쪽(플레이어 <see cref="Character"/>)을 타이머 주인으로 빌려 쓴다.
    ///
    /// 대화가 끝나면 <see cref="EndTalk"/>이 <c>Show</c>를 끄고, <b>사라지는 애니메이션이 끝날 만큼</b>
    /// (<see cref="HideDuration"/>) 기다렸다가 오브젝트를 다시 끈다. 너무 일찍 끄면 사라지는 모션이 잘리고,
    /// 안 끄면 보이지도 않는 오브젝트가 계속 돌아간다.
    ///
    /// 오브젝트를 켜고 끄는 것과 <c>Show</c>를 따로 두는 이유: 켜기만 해서는 등장 모션이 없고,
    /// <c>Show</c>만 써서는 대화 밖에서도 Animator·리그가 계속 돌아간다. 켜는 건 존재를, <c>Show</c>는 연출을 맡는다.
    ///
    /// 시간은 타임스케일과 무관하게 흐른다 — 대화 중 슬로우모션이나 일시정지가 걸려도 등장·퇴장은 제 속도로 간다
    /// (대화 카메라·방향 전환과 같은 규칙).
    ///
    /// 플레이어 밑에 여럿 달려 있으면 <b>먼저 찾은 하나</b>만 쓴다. 대화마다 다른 보조 캐릭터를 세우려면
    /// 그때 가서 존이 지목하는 방식으로 넓히면 된다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Companion")]
    public class DialogueCompanion : MonoBehaviour
    {
        [Tooltip("Show를 켜고 끌 Animator. 비우면 자기 자신(및 자식)에서 찾는다.")]
        public Animator CompanionAnimator;

        [Tooltip("대화가 시작되고 몇 초 뒤에 나타날지. 플레이어의 Talk 모션이 먼저 나가도록 살짝 늦춘다.")]
        [Range(0f, 3f)]
        public float ShowDelay = 0.15f;

        [Tooltip("사라지는 애니메이션 길이(초). 이 시간이 지나면 오브젝트를 끈다. " +
                 "0이면 Show를 끄자마자 끄므로 퇴장 모션이 보이지 않는다.")]
        [Range(0f, 5f)]
        public float HideDuration = 0.5f;

        [Tooltip("플레이를 시작할 때 자동으로 꺼둔다. 씬에서 켜둔 채로 작업해도 플레이하면 사라진다.")]
        public bool HideOnAwake = true;

        /// <summary>지금 대화 중인가. 아직 등장 딜레이를 기다리는 동안(=꺼져 있는 동안)도 포함한다.</summary>
        public bool IsTalking => _talking;

        protected bool _talking;
        protected bool _shown;
        protected bool _hidePending;
        protected float _hideAt;

        // 등장 대기는 남의 몸을 빌려 돈다(우리는 아직 꺼져 있다). 취소하려면 주인도 같이 들고 있어야 한다.
        protected Coroutine _showRoutine;
        protected MonoBehaviour _showRoutineHost;

        // 파라미터 존재 여부는 한 번만 확인한다. 없는 이름에 SetBool을 하면 에디터가 매번 경고를 뱉는다.
        protected bool _showParameterChecked;
        protected bool _hasShowParameter;

        /// <summary>
        /// 씬에서 켜둔 채로 시작했으면 여기서 꺼둔다.
        ///
        /// **꺼진 채로 시작한 오브젝트는 Awake가 지금 돌지 않는다** — 처음 켜지는 순간, 즉
        /// <see cref="BeginTalk"/> 안의 SetActive(true)에서 비로소 돈다. 그때 아무 생각 없이 꺼버리면
        /// 방금 켠 오브젝트를 스스로 도로 끄게 되므로(대화 내내 안 나온다), 대화 때문에 켜진 경우
        /// (<see cref="_talking"/>)는 건너뛴다. BeginTalk이 켜기 **전에** 그 깃발을 세우는 이유가 이것이다.
        /// </summary>
        protected virtual void Awake()
        {
            ResolveAnimator();

            if (HideOnAwake && !_talking)
            {
                CancelTimers();
                ApplyShow(false);
                gameObject.SetActive(false);
            }
        }

        protected virtual void OnDisable()
        {
            // 꺼진 동안에는 Update가 돌지 않는다. 남은 타이머를 들고 있으면 다음에 켜질 때
            // 지난 시각으로 곧바로 터져 등장·퇴장이 한 프레임에 지나가 버린다.
            // 등장 대기는 남의 몸에서 도는 중이라, 우리가 꺼졌다고 멈추지 않는다 — 여기서 직접 끊는다.
            CancelTimers();
            _talking = false;
            _shown = false;
        }

        /// <summary>
        /// 대화 시작 — 딜레이만큼 기다렸다가 오브젝트를 켜고 등장 애니메이션을 시작한다.
        ///
        /// 꺼져 있는 오브젝트에도 그냥 부르면 된다(메서드 호출은 활성 여부와 무관하다).
        /// </summary>
        /// <param name="timerHost">
        /// 등장 딜레이를 세어 줄 주인. 우리가 꺼져 있는 동안에는 코루틴을 스스로 돌릴 수 없어서 빌린다.
        /// 보통은 대화를 연 플레이어 <see cref="Character"/>가 들어온다. 비우면 우리가 이미 켜져 있을 때만
        /// 스스로 센다.
        /// </param>
        public virtual void BeginTalk(MonoBehaviour timerHost = null)
        {
            _talking = true;
            _hidePending = false;
            StopShowRoutine();

            if (ShowDelay <= 0f)
            {
                ActivateAndShow();
                return;
            }

            MonoBehaviour host = timerHost != null && timerHost.isActiveAndEnabled ? timerHost : null;
            if (host == null && gameObject.activeInHierarchy)
            {
                host = this;
            }

            if (host == null)
            {
                // 시간을 세어 줄 사람이 없다. 딜레이를 포기하고 바로 등장한다 — 안 나오는 것보다는 낫다.
                ActivateAndShow();
                return;
            }

            _showRoutineHost = host;
            _showRoutine = host.StartCoroutine(ShowAfterDelay(ShowDelay));
        }

        /// <summary>딜레이를 센 뒤 켠다. 기다리는 사이에 대화가 끝났으면 아무 일도 하지 않는다.</summary>
        protected virtual IEnumerator ShowAfterDelay(float delay)
        {
            // 대화 중 슬로우모션·일시정지가 걸려도 등장 타이밍은 제 속도로 간다.
            float showAt = Time.unscaledTime + delay;
            while (Time.unscaledTime < showAt)
            {
                yield return null;
            }

            _showRoutine = null;
            _showRoutineHost = null;

            if (!_talking)
            {
                yield break;
            }

            ActivateAndShow();
        }

        /// <summary>
        /// 오브젝트를 켜고 곧바로 <c>Show</c>를 올린다.
        ///
        /// <see cref="_talking"/>이 이미 서 있는 상태로 켜야 한다 — 처음 켜지는 순간 <see cref="Awake"/>가
        /// 돌면서 Hide On Awake로 스스로 도로 꺼버리는 것을 그 깃발이 막는다.
        /// </summary>
        protected virtual void ActivateAndShow()
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            ResolveAnimator();
            ApplyShow(true);
        }

        /// <summary>대화 종료 — 사라지는 모션을 태우고, 끝나면 오브젝트를 끈다.</summary>
        public virtual void EndTalk()
        {
            _talking = false;
            StopShowRoutine();

            if (!gameObject.activeSelf)
            {
                // 등장 딜레이 중에 대화가 끝났다 — 켜지지도 않았으니 끌 것도, 태울 모션도 없다.
                CancelTimers();
                return;
            }

            // 아직 나타나지 않았다면 사라질 모션도 없다. 곧바로 끈다.
            bool wasShown = _shown;

            ApplyShow(false);

            float wait = wasShown ? Mathf.Max(0f, HideDuration) : 0f;

            if (wait <= 0f)
            {
                Deactivate();
                return;
            }

            _hidePending = true;
            _hideAt = Time.unscaledTime + wait;
        }

        protected virtual void Update()
        {
            if (_hidePending && Time.unscaledTime >= _hideAt)
            {
                _hidePending = false;
                Deactivate();
            }
        }

        protected virtual void Deactivate()
        {
            CancelTimers();

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        protected virtual void CancelTimers()
        {
            StopShowRoutine();
            _hidePending = false;
        }

        /// <summary>남의 몸에서 돌고 있는 등장 대기를 끊는다. 주인이 이미 사라졌으면 참조만 버린다.</summary>
        protected virtual void StopShowRoutine()
        {
            if (_showRoutine != null && _showRoutineHost != null)
            {
                _showRoutineHost.StopCoroutine(_showRoutine);
            }

            _showRoutine = null;
            _showRoutineHost = null;
        }

        protected virtual void ApplyShow(bool value)
        {
            _shown = value;

            if (CompanionAnimator == null || !HasShowParameter())
            {
                return;
            }

            CompanionAnimator.SetBool(AnimParams.CompanionShow, value);
        }

        protected virtual void ResolveAnimator()
        {
            if (CompanionAnimator == null)
            {
                CompanionAnimator = GetComponentInChildren<Animator>(true);
            }
        }

        protected virtual bool HasShowParameter()
        {
            if (_showParameterChecked)
            {
                return _hasShowParameter;
            }

            if (CompanionAnimator == null || CompanionAnimator.runtimeAnimatorController == null)
            {
                // 컨트롤러가 아직 없으면 판정을 미룬다 — 다음에 다시 물어본다.
                return false;
            }

            _showParameterChecked = true;
            _hasShowParameter = false;

            AnimatorControllerParameter[] parameters = CompanionAnimator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].type == AnimatorControllerParameterType.Bool
                    && parameters[i].name == AnimParams.CompanionShow)
                {
                    _hasShowParameter = true;
                    break;
                }
            }

            if (!_hasShowParameter)
            {
                Debug.LogWarning($"[DialogueCompanion] '{name}'의 Animator에 Bool 파라미터 " +
                                 $"'{AnimParams.CompanionShow}'가 없어 등장·퇴장 연출을 걸 수 없습니다.", this);
            }

            return _hasShowParameter;
        }
    }
}
