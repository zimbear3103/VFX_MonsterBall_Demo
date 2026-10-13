using System.Collections.Generic;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화 동안 감춰둘 오브젝트들 — 플레이어가 들고 있는 무기처럼, 대화 자세와 같이 있으면 안 되는 것들.
    ///
    /// <b>캐릭터 루트</b>(<see cref="Yeolha.BeltScroll.Character"/>가 붙은 오브젝트)나 그 자식에 하나 달아두고,
    /// 감출 오브젝트를 <see cref="Objects"/>에 넣는다. 대화가 시작되면
    /// <see cref="DialogueTalkPresentation.Begin"/>이 여기를 찾아 끄고, 끝나면 다시 켠다.
    /// 배선은 <b>Tools/Aiara/대화 중 감출 오브젝트 등록</b> 메뉴가 해준다.
    ///
    /// 끄고 켜는 순간은 <see cref="HideDelay"/>(선딜레이)와 <see cref="ShowDelay"/>(후딜레이)로 미룰 수 있다 —
    /// 무기를 집어넣는 모션이 나가는 동안 무기가 손에 남아 있어야 하고, 다시 뽑는 모션에도 같은 사정이 있다.
    /// 시간은 타임스케일과 무관하게 흐른다(대화 중 슬로우모션·일시정지가 걸려도 제 속도로 간다).
    ///
    /// <b>우리가 끈 것만 되돌린다.</b> 대화 전부터 이미 꺼져 있던 오브젝트(아직 안 얻은 무기, 다른 시스템이
    /// 숨겨둔 이펙트)는 목록에 있어도 건드리지 않는다 — 대화가 끝났다고 없던 물건이 손에 생기면 안 된다.
    ///
    /// 목록에 <b>이 캐릭터 자신</b>을 넣으면 대화 중 플레이어가 통째로 사라지므로 등록 메뉴가 막아둔다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Hidden Objects")]
    public class DialogueHiddenObjects : MonoBehaviour
    {
        [Tooltip("대화 동안 꺼둘 오브젝트. 대화가 끝나면 우리가 끈 것만 다시 켠다. " +
                 "Tools/Aiara/대화 중 감출 오브젝트 등록 메뉴로 채우는 것이 편하다.")]
        public List<GameObject> Objects = new List<GameObject>();

        [Tooltip("선딜레이 — 대화가 시작되고 몇 초 뒤에 끌지. 0이면 시작하는 순간 끈다. " +
                 "무기를 집어넣는 모션이 있으면 그 길이만큼 준다.")]
        [Range(0f, 5f)]
        public float HideDelay = 0f;

        [Tooltip("후딜레이 — 대화가 끝나고 몇 초 뒤에 다시 켤지. 0이면 끝나는 순간 켠다. " +
                 "무기를 다시 뽑는 모션이 있으면 그 길이만큼 준다.")]
        [Range(0f, 5f)]
        public float ShowDelay = 0f;

        /// <summary>이번 대화에서 <b>우리가</b> 끈 오브젝트. 되돌릴 때 이것만 켠다.</summary>
        protected readonly List<GameObject> _hidden = new List<GameObject>();

        // 딜레이는 스스로 센다 — 이 컴포넌트는 대화 내내 켜져 있는 캐릭터에 붙어 있다.
        protected bool _hidePending;
        protected float _hideAt;
        protected bool _showPending;
        protected float _showAt;

        /// <summary>지금 감춰둔 것이 있는가.</summary>
        public bool IsHiding => _hidden.Count > 0;

        /// <summary>
        /// 대화 시작 — <see cref="HideDelay"/>만큼 기다렸다가 목록의 오브젝트를 끈다.
        /// 여러 번 불러도 안전하다.
        /// </summary>
        public virtual void BeginTalk()
        {
            // 앞 대화의 되돌리기가 아직 기다리는 중이면 취소한다 — 감춘 채로 이어가면 되고,
            // 여기서 켜버리면 무기가 한 번 깜빡인다.
            _showPending = false;

            if (HideDelay <= 0f)
            {
                _hidePending = false;
                HideNow();
                return;
            }

            _hidePending = true;
            _hideAt = Time.unscaledTime + HideDelay;
        }

        /// <summary>
        /// 대화 종료 — <see cref="ShowDelay"/>만큼 기다렸다가 우리가 껐던 것만 다시 켠다.
        ///
        /// 아직 끄지도 않았는데 대화가 끝났으면(선딜레이 도중 종료) 그냥 없던 일로 한다.
        /// </summary>
        public virtual void EndTalk()
        {
            _hidePending = false;

            if (_hidden.Count == 0)
            {
                _showPending = false;
                return;
            }

            if (ShowDelay <= 0f)
            {
                _showPending = false;
                ShowNow();
                return;
            }

            _showPending = true;
            _showAt = Time.unscaledTime + ShowDelay;
        }

        protected virtual void Update()
        {
            if (_hidePending && Time.unscaledTime >= _hideAt)
            {
                _hidePending = false;
                HideNow();
            }

            if (_showPending && Time.unscaledTime >= _showAt)
            {
                _showPending = false;
                ShowNow();
            }
        }

        /// <summary>목록에서 켜져 있는 것들을 끈다. 이미 꺼져 있던 것은 손대지 않는다.</summary>
        protected virtual void HideNow()
        {
            if (Objects == null)
            {
                return;
            }

            for (int i = 0; i < Objects.Count; i++)
            {
                GameObject target = Objects[i];

                // 이미 꺼져 있던 것은 손대지 않는다 — 켜는 것은 우리 몫이 아니다.
                if (target == null || !target.activeSelf || _hidden.Contains(target))
                {
                    continue;
                }

                target.SetActive(false);
                _hidden.Add(target);
            }
        }

        /// <summary>우리가 껐던 것만 다시 켠다.</summary>
        protected virtual void ShowNow()
        {
            for (int i = 0; i < _hidden.Count; i++)
            {
                GameObject target = _hidden[i];
                if (target != null)
                {
                    target.SetActive(true);
                }
            }

            _hidden.Clear();
        }

        /// <summary>
        /// 감춘 채로 이 컴포넌트가 꺼지거나 파괴되면 무기가 영영 사라진다 —
        /// 대화 도중 씬이 바뀌거나 캐릭터가 교체되는 경우가 그렇다. 마지막 기회에 되돌린다.
        ///
        /// 꺼진 동안에는 Update가 돌지 않으므로 남은 딜레이는 여기서 버리고 곧바로 켠다.
        /// </summary>
        protected virtual void OnDisable()
        {
            _hidePending = false;
            _showPending = false;
            ShowNow();
        }
    }
}
