using MoreMountains.Tools;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue
{
    /// <summary>
    /// NPC 머리 위 말풍선 — <b>지금 조건이 맞아 열 수 있는 대화가 있을 때</b>만 띄운다.
    /// NPC 본체(또는 존이 붙은 오브젝트)에 달고, 띄울 오브젝트를 <see cref="Bubble"/>에 넣는다.
    /// 배선은 <b>Tools/Aiara/NPC 말풍선 붙이기</b> 메뉴가 해준다.
    ///
    /// <b>판정은 존에게 물어본다</b> — 게이트(퀘스트 상태), 남은 사용 횟수, 그리고 시트에서 온
    /// 대화 시작 조건(<see cref="NpcDialogueZone.ResolveConversationTitle"/>)까지 전부 존이 이미 알고 있다.
    /// 여기서 조건을 다시 적으면 "말풍선은 떴는데 말을 걸면 아무 일도 안 일어나는" 어긋남이 생긴다.
    ///
    /// 존 안에 들어왔을 때 뜨는 <see cref="NpcDialogueZone.ButtonPrompt"/>(B 안내)와는 다른 물건이다 —
    /// 그쪽은 "지금 누르면 말을 건다", 이쪽은 <b>멀리서도 보이는</b> "저 NPC에게 볼일이 있다"는 표시다.
    ///
    /// 조건 검사는 Lua 평가라 매 프레임 돌 필요가 없다. <see cref="CheckInterval"/>마다 한 번만 보고,
    /// NPC가 여럿일 때 같은 프레임에 몰리지 않도록 시작 시각을 조금씩 흩어둔다.
    ///
    /// <b>대화가 열리면 즉시 내린다.</b> 대화 중 여부만은 주기 검사에 맡기지 않는다 —
    /// 그러면 말을 건 직후에도 최대 한 주기(기본 0.5초) 동안 머리 위에 말풍선이 남는다.
    /// 대화가 끝나면 곧바로 다시 판정한다.
    ///
    /// <b>자리는 씬에 놓아둔 그대로 둔다.</b> 말풍선은 NPC의 자식이라 가만히 둬도 따라다닌다.
    /// 머리 본처럼 애니메이션으로 움직이는 지점에 붙이고 싶을 때만 <see cref="Anchor"/>를 지정하면,
    /// 그때부터 매 프레임 <see cref="Anchor"/> + <see cref="Offset"/> 자리로 옮긴다.
    /// (예전에는 Anchor가 비어 있어도 NPC 원점 기준으로 옮겨서, 에디터에서 맞춰둔 자리가 플레이하면 튀었다.)
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Npc Dialogue Bubble")]
    [DisallowMultipleComponent]
    public class NpcDialogueBubble : MonoBehaviour
    {
        [MMInspectorGroup("배선", true, 100)]

        [Tooltip("조건을 물어볼 대화 존. 비우면 이 오브젝트와 부모·자식에서 찾는다.")]
        public NpcDialogueZone Zone;

        [Tooltip("띄우고 내릴 말풍선 오브젝트. 비우면 아무것도 하지 않는다.")]
        public GameObject Bubble;

        [Tooltip("말풍선이 매 프레임 따라다닐 기준(머리 본 등). " +
                 "비워두면 씬에 놓아둔 자리를 그대로 쓴다 — 말풍선은 NPC의 자식이라 저절로 따라온다.")]
        public Transform Anchor;

        [MMInspectorGroup("자리", true, 101)]

        [Tooltip("Anchor를 지정했을 때 그 지점에서 얼마나 띄울지(월드 기준). " +
                 "Anchor가 비어 있으면 쓰지 않는다 — 그때는 놓아둔 자리가 그대로 유지된다.")]
        public Vector3 Offset = DefaultOffset;

        [Tooltip("항상 카메라를 보게 돌린다. 끄면 놓인 방향 그대로 있는다.")]
        public bool FaceCamera = true;

        [MMInspectorGroup("표시 조건", true, 102)]

        [Tooltip("조건을 다시 보는 주기(초). 시트 조건은 Lua로 평가되므로 매 프레임 돌릴 이유가 없다.")]
        public float CheckInterval = 0.5f;

        [Tooltip("플레이어가 이 거리 안에 있을 때만 띄운다. 0이면 거리 제한 없음(조건만 맞으면 항상).")]
        public float MaxDistance = 0f;

        [MMInspectorGroup("디버그", true, 103)]

        [Tooltip("말풍선이 뜨고 내릴 때 어떤 대화 때문인지 콘솔에 남긴다.")]
        public bool DebugLog = false;

        /// <summary>
        /// 말풍선을 처음 놓는 자리 — 머리 위로 올리고 화면 안쪽(-Z)으로 조금 당긴 값이다.
        /// 붙이기 메뉴가 이 값으로 놓아주고, 컴포넌트의 Offset 기본값도 같다.
        /// </summary>
        public static readonly Vector3 DefaultOffset = new Vector3(0f, 3f, -0.75f);

        /// <summary>지금 말풍선이 떠 있는가.</summary>
        public bool IsShown => _shown;

        protected bool _shown;
        protected float _nextCheckAt;
        protected bool _wasConversationActive;
        protected Character _player;
        protected Camera _camera;

        protected virtual void Awake()
        {
            if (Zone == null)
            {
                Zone = GetComponent<NpcDialogueZone>()
                       ?? GetComponentInChildren<NpcDialogueZone>(true)
                       ?? GetComponentInParent<NpcDialogueZone>();
            }

            Apply(false);

            // NPC가 여럿이면 같은 프레임에 전부 조건을 평가하게 된다. 시작 시각을 흩어 부하를 고르게 편다.
            _nextCheckAt = Time.time + Random.Range(0f, Mathf.Max(0.01f, CheckInterval));
        }

        protected virtual void OnDisable()
        {
            Apply(false);
        }

        protected virtual void Update()
        {
            bool conversationActive = DialogueManager.instance != null && DialogueManager.IsConversationActive;

            if (conversationActive)
            {
                // 주기를 기다리지 않는다. 말을 건 다음에도 말풍선이 남아 있으면 눈에 거슬린다.
                _wasConversationActive = true;
                Apply(false);
                return;
            }

            if (_wasConversationActive)
            {
                // 방금 대화가 끝났다 — 조건이 바뀌었을 수 있으니(퀘스트 진행, 사용 횟수 소진) 곧바로 다시 본다.
                _wasConversationActive = false;
                _nextCheckAt = 0f;
            }

            if (Time.time < _nextCheckAt)
            {
                return;
            }

            _nextCheckAt = Time.time + Mathf.Max(0.05f, CheckInterval);
            Apply(HasAvailableConversation());
        }

        /// <summary>애니메이션이 끝난 뒤에 자리를 잡아야 머리 본을 따라갈 때 한 프레임 늦지 않는다.</summary>
        protected virtual void LateUpdate()
        {
            if (!_shown || Bubble == null)
            {
                return;
            }

            // Anchor를 지정한 경우에만 옮긴다. 비어 있으면 놓아둔 자리를 그대로 지킨다.
            if (Anchor != null)
            {
                Bubble.transform.position = Anchor.position + Offset;
            }

            if (FaceCamera)
            {
                Camera cam = ResolveCamera();
                if (cam != null)
                {
                    // 카메라를 '바라보게' 하는 것이 아니라 카메라와 같은 방향을 보게 한다 —
                    // 화면 가장자리에서도 기울지 않고 반듯하게 서 있다.
                    Bubble.transform.rotation = cam.transform.rotation;
                }
            }
        }

        /// <summary>지금 이 NPC에게 열 수 있는 대화가 있는가.</summary>
        public virtual bool HasAvailableConversation()
        {
            if (Zone == null || !Zone.isActiveAndEnabled)
            {
                return false;
            }

            // 게이트·사용 횟수·대화 중 여부는 존이 이미 판단하고 있다.
            if (!Zone.CanTalk)
            {
                return false;
            }

            Character player = ResolvePlayer();
            if (player == null)
            {
                return false;
            }

            if (MaxDistance > 0f)
            {
                float distance = Vector3.Distance(player.transform.position, Zone.InteractionPoint);
                if (distance > MaxDistance)
                {
                    return false;
                }
            }

            Transform conversant = Zone.ConversantTransform != null ? Zone.ConversantTransform : Zone.transform;
            string title = Zone.ResolveConversationTitle(player.transform, conversant);

            if (DebugLog && !string.IsNullOrEmpty(title) && !_shown)
            {
                Debug.Log($"[NpcDialogueBubble] '{name}' — 열 수 있는 대화: {title}", this);
            }

            return !string.IsNullOrEmpty(title);
        }

        protected virtual void Apply(bool show)
        {
            _shown = show;

            if (Bubble != null && Bubble.activeSelf != show)
            {
                Bubble.SetActive(show);
            }
        }

        /// <summary>
        /// 조건을 평가할 때 넘길 플레이어. 시트 조건이 액터를 보는 경우가 있어 존과 같은 대상을 써야 한다.
        /// 찾는 방법은 대화 액션 쪽과 공유한다 — 두 곳이 서로 다른 플레이어를 잡으면 판정이 갈린다.
        /// </summary>
        protected virtual Character ResolvePlayer()
        {
            if (_player == null)
            {
                _player = DialogueActions.ResolvePlayer(null, null);
            }

            return _player;
        }

        /// <summary>
        /// 말풍선을 돌릴 기준 카메라.
        ///
        /// <see cref="Camera.main"/>은 MainCamera 태그가 붙은 카메라만 찾는다 — 태그를 빠뜨린 씬에서는
        /// null이 되어 말풍선이 놓인 각도 그대로 굳는다("카메라를 안 보고 옆을 본다"는 증상).
        /// 그래서 태그가 없으면 씬에 켜져 있는 카메라라도 쓴다.
        /// </summary>
        protected virtual Camera ResolveCamera()
        {
            if (_camera != null && _camera.isActiveAndEnabled)
            {
                return _camera;
            }

            _camera = Camera.main;

            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Object.FindFirstObjectByType<Camera>();
            }

            return _camera;
        }
    }
}
