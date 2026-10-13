using MoreMountains.Tools;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 맵에 깔아두는 이벤트 트리거. 플레이어가 구역에 들어왔을 때 조건이 맞으면 이벤트를 시작한다.
    ///
    /// 발생 조건(어떤 퀘스트가 활성/완료여야 하는지)은 EventData 시트에 있고,
    /// 임포터가 그것을 대화의 시작 조건으로 넣어둔다. 그래서 이 컴포넌트는 조건을 직접 알지 못하고
    /// <see cref="DialogueManager.ConversationHasValidEntry"/>에 물어보기만 한다.
    /// 조건이 바뀌면 시트만 고치고 다시 임포트하면 된다.
    ///
    /// 콜라이더는 Is Trigger로 두어야 한다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Event Zone")]
    [RequireComponent(typeof(Collider))]
    public class EventZone : MonoBehaviour
    {
        private const string EventLockOwner = "DialogueEvent";

        [MMInspectorGroup("이벤트", true, 100)]

        [Tooltip("시작할 이벤트 ID. EventData 시트의 ID와 정확히 같아야 한다. (예: EVENT_01_001)")]
        public string EventID;

        [Tooltip("대화 상대로 쓸 Transform. 비우면 이 오브젝트를 쓴다. " +
                 "시퀀서의 Camera()/LookAt() 같은 연출이 이 위치를 기준으로 잡는다.")]
        public Transform ConversantTransform;

        [MMInspectorGroup("실행 조건", true, 101)]

        [Tooltip("한 번 실행하면 다시 실행하지 않는다. " +
                 "끄면 조건이 맞을 때마다 반복 실행된다 — 이벤트가 퀘스트 상태를 바꾸지 않으면 무한 반복이 되니 주의.")]
        public bool TriggerOnce = true;

        [Tooltip("플레이어가 구역 안에 있는 동안 조건을 다시 검사한다. " +
                 "이미 구역 안에 서 있는데 나중에 퀘스트가 열리는 경우를 잡아준다.")]
        public bool RecheckWhileInside = true;

        [Tooltip("조건 재검사 주기(초)")]
        public float RecheckInterval = 0.5f;

        [MMInspectorGroup("이벤트 중 잠금", true, 102)]

        [Tooltip("이벤트가 진행되는 동안 플레이어 조작을 전부 막는다. " +
                 "이동(스틱)과 버튼(점프·공격·대시)을 각각 다른 방식으로 끊어야 해서 둘 다 처리한다.")]
        public bool LockControlDuringEvent = true;

        [MMInspectorGroup("이벤트 연출 배치", true, 104)]

        [Tooltip("이벤트 시작 시 플레이어 위치와 카메라를 정해진 자리에 놓는 컴포넌트. " +
                 "비워두면 이 오브젝트(및 자식)에서 자동으로 찾는다. 없으면 배치 없이 그 자리에서 시작한다.")]
        public DialogueStaging Staging;

        [MMInspectorGroup("디버그", true, 103)]

        [Tooltip("이벤트가 시작되지 못한 이유를 콘솔에 남긴다. " +
                 "같은 이유는 한 번만 찍으므로 재검사 때문에 도배되지 않는다.")]
        public bool DebugLog = false;

        protected Character _player;
        protected DialogueControlLock _controlLock;
        protected bool _playerInside;
        protected bool _eventRunning;
        protected bool _alreadyTriggered;
        protected bool _subscribed;
        protected float _nextRecheckAt;
        protected string _lastSkipReason;

        /// <summary>이미 실행이 끝나서 더 이상 반응하지 않는 상태인가.</summary>
        public virtual bool IsSpent => TriggerOnce && _alreadyTriggered;

        protected virtual void Reset()
        {
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }
        }

        protected virtual void Awake()
        {
            if (Staging == null)
            {
                Staging = GetComponentInChildren<DialogueStaging>(true);
            }
        }

        protected virtual void OnDisable()
        {
            Unsubscribe();

            // 이벤트 도중 존이 꺼지면 잠금이 영영 남는다 — 여기서 반드시 푼다.
            if (_eventRunning)
            {
                _eventRunning = false;
                _controlLock.Unlock();
                EndTalkPresentation();

                if (Staging != null)
                {
                    Staging.Release();
                }
            }

            _playerInside = false;
        }

        protected virtual void Update()
        {
            if (!RecheckWhileInside || !_playerInside || _eventRunning || IsSpent)
            {
                return;
            }

            if (Time.time < _nextRecheckAt)
            {
                return;
            }

            _nextRecheckAt = Time.time + Mathf.Max(0.1f, RecheckInterval);
            TryStartEvent();
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            // 콜라이더가 붙은 오브젝트 자신에서만 찾는다 — 캐릭터 루트(CharacterController 캡슐)가
            // Character를 들고 있다. 자식 Hurtbox까지 훑으면 자식 콜라이더가 하나씩 드나들 때마다
            // 진입/이탈이 어긋난다.
            Character character = other.gameObject.MMGetComponentNoAlloc<Character>();
            if (character == null || character.Faction != FactionType.Player)
            {
                return;
            }

            _player = character;
            _playerInside = true;
            _nextRecheckAt = Time.time + Mathf.Max(0.1f, RecheckInterval);

            TryStartEvent();
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            Character character = other.gameObject.MMGetComponentNoAlloc<Character>();
            if (character == null || character != _player)
            {
                return;
            }

            _playerInside = false;

            // 이벤트 중에 구역을 벗어나도 잠금은 여기서 풀지 않는다 —
            // 이벤트가 끝날 때가 유일한 해제 경로여야 "이벤트 중인데 움직여지는" 상태가 안 생긴다.
        }

        /// <summary>
        /// 조건을 확인하고 만족하면 이벤트를 시작한다. 조건 판정은 대화 DB에 들어 있으므로
        /// 여기서는 "시작 가능한 항목이 있는가"만 묻는다.
        /// </summary>
        public virtual bool TryStartEvent()
        {
            if (_eventRunning || IsSpent || string.IsNullOrEmpty(EventID))
            {
                return false;
            }

            // Instance가 아니라 instance를 쓴다 — 없는데 만들어버리면 안 된다.
            if (DialogueManager.instance == null)
            {
                LogSkip($"씬에 Dialogue Manager가 없습니다. 프리팹을 배치하고 Initial Database를 물려주세요.");
                return false;
            }

            // 다른 이벤트나 대화가 진행 중이면 끼어들지 않는다. 구역 안에 있으면 재검사로 다시 시도된다.
            if (DialogueManager.IsConversationActive)
            {
                LogSkip("다른 대화가 진행 중입니다.");
                return false;
            }

            Transform actor = _player != null ? _player.transform : null;
            Transform conversant = ConversantTransform != null ? ConversantTransform : transform;

            if (!DialogueManager.ConversationHasValidEntry(EventID, actor, conversant))
            {
                // 'ID가 틀림'과 '조건 미달'은 원인이 전혀 다르므로 구분해서 알려준다.
                bool conversationExists = DialogueManager.masterDatabase != null
                    && DialogueManager.masterDatabase.GetConversation(EventID) != null;

                LogSkip(conversationExists
                    ? "조건이 아직 맞지 않습니다. (EventData의 ActiveQuest/ClearQuest 상태를 확인하세요 — " +
                      "QuestInitializer가 같은 씬에서 시작 퀘스트를 켰는지도 함께 보세요.)"
                    : $"'{EventID}'라는 이벤트가 데이터베이스에 없습니다. " +
                      "EventData 시트의 ID와 철자가 같은지, 임포트를 다시 돌렸는지 확인하세요.");

                return false;
            }

            _lastSkipReason = null;

            // 대화가 같은 프레임에 끝나버려도 잠금이 풀리도록 시작 전에 구독과 잠금을 끝내둔다.
            _eventRunning = true;
            _alreadyTriggered = true;
            Subscribe();

            if (LockControlDuringEvent && _player != null)
            {
                _controlLock.Lock(_player, EventLockOwner);
            }

            // 대화 자세(IsTalk)는 모든 이벤트에 조건 없이 붙는다 — 잠금과는 별개다.
            // 그 위에 얹히는 대화 모션·보조 NPC는 Action 노드(PlayerTalk1/PlayerTalk2)가 정하므로,
            // 존은 그 액션이 걸릴 대상만 알려준다.
            DialogueActions.SetPlayer(_player);
            DialogueTalkPresentation.Begin(_player);

            // 배치는 잠금 뒤에 한다 — 먼저 옮기면 아직 살아 있는 스틱 입력이 그 프레임에 밀어낸다.
            if (Staging != null && _player != null)
            {
                Staging.Apply(_player);
            }

            DialogueManager.StartConversation(EventID, actor, conversant);

            // Pixel Crushers가 대화를 조용히 거절할 수 있다 — Dialogue UI 미할당, 다른 대화 진행 중,
            // 데이터베이스에 없는 제목 등. 그때는 conversationEnded가 오지 않아 해제 경로가 사라지고
            // 먼저 걸어둔 잠금·연출만 영영 남는다.
            if (_eventRunning && !DialogueManager.IsConversationActive)
            {
                Debug.LogError($"[EventZone] '{EventID}' 이벤트가 시작되지 않아 잠금과 연출을 되돌립니다. " +
                               "Dialogue Manager의 Dialogue UI와 Initial Database가 비어 있지 않은지 확인하고, " +
                               "콘솔의 'Dialogue System:' 메시지에서 거절 이유를 보세요.", this);

                _eventRunning = false;
                _alreadyTriggered = false;
                Unsubscribe();

                if (Staging != null)
                {
                    Staging.Release();
                }

                _controlLock.Unlock();
                EndTalkPresentation();
                return false;
            }

            return true;
        }

        /// <summary>대화 자세를 풀고 액션이 걸어둔 모션·보조 NPC까지 되돌린다. 여러 번 불러도 안전하다.</summary>
        protected virtual void EndTalkPresentation()
        {
            DialogueActions.ClearPlayer(_player);
            DialogueTalkPresentation.End(_player);
        }

        /// <summary>
        /// 시작하지 못한 이유를 남긴다. 재검사가 매 0.5초 돌기 때문에, 같은 이유가 이어지는 동안은
        /// 한 번만 찍는다 — 그래야 콘솔이 살아 있고, 원인이 바뀌는 순간이 눈에 띈다.
        /// </summary>
        protected virtual void LogSkip(string reason)
        {
            if (!DebugLog || string.Equals(_lastSkipReason, reason))
            {
                return;
            }

            _lastSkipReason = reason;
            Debug.Log($"[EventZone] '{EventID}' 시작 안 함 — {reason}", this);
        }

        protected virtual void Subscribe()
        {
            if (_subscribed || DialogueManager.instance == null)
            {
                return;
            }

            DialogueManager.instance.conversationEnded += OnConversationEnded;
            _subscribed = true;
        }

        protected virtual void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            if (DialogueManager.instance != null)
            {
                DialogueManager.instance.conversationEnded -= OnConversationEnded;
            }

            _subscribed = false;
        }

        protected virtual void OnConversationEnded(Transform actor)
        {
            _eventRunning = false;
            Unsubscribe();

            if (Staging != null)
            {
                Staging.Release();
            }

            _controlLock.Unlock();
            EndTalkPresentation();

            // 반복 허용이면 다음 검사 때 조건을 다시 본다.
            if (!TriggerOnce)
            {
                _alreadyTriggered = false;
                _nextRecheckAt = Time.time + Mathf.Max(0.1f, RecheckInterval);
            }
        }

#if UNITY_EDITOR
        protected virtual void OnDrawGizmos()
        {
            var boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                return;
            }

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(boxCollider.center, boxCollider.size);
        }
#endif
    }
}
