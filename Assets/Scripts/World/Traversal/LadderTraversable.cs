using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Level-facing ladder. Drop the prefab in, set Height (or scale it), done.
    ///
    /// The three child zones (ClimbVolume / BottomZone / TopZone) and the two exit points
    /// are laid out automatically from Height in OnValidate, so the level designer normally
    /// only touches Height, Climb Speed and the exit offsets. Zones are just detection
    /// volumes — they never own gameplay state. When the player enters a zone with the right
    /// up/down input, CharacterState.CheckLadder asks CharacterStateManager to enter
    /// LadderClimb; the manager stays the single source of truth.
    ///
    /// All geometry is resolved from this transform every query, so a ladder parented to a
    /// MovingPlatform carries correctly with the platform.
    /// </summary>
    [AddComponentMenu("Yeolha/Traversal/Ladder")]
    public class LadderTraversable : MonoBehaviour
    {
        [Header("Ladder")]
        [Tooltip("사다리 오를 수 있는 높이(m, 로컬). 이 값만 바꾸면 Zone/Exit가 자동 재배치된다.")]
        [SerializeField, Min(0.5f)] private float height = 3f;
        [Tooltip("오르내리는 속도 (m/s)")]
        [SerializeField, Min(0.1f)] private float climbSpeed = 3f;

        [Header("Climb Step (event-driven)")]
        [Tooltip("climbingUp/Down 애니 이벤트 1회당 이동할 거리 (m)")]
        [SerializeField, Min(0.01f)] private float climbStepDistance = 1f;
        [Tooltip("한 칸 이동 속도 (m/s)")]
        [SerializeField, Min(0.1f)] private float climbStepSpeed = 3f;

        [Header("Facing")]
        [Tooltip("이 사다리를 타면 바라볼 방향. 체크=오른쪽, 해제=왼쪽(비주얼 미러링)")]
        [SerializeField] private bool faceRight = true;
        [Tooltip("등반 중 캐릭터 루트 yaw를 사다리 회전에 맞출지 여부")]
        [SerializeField] private bool alignCharacterYaw = true;
        [Tooltip("사다리 yaw 기준 추가 회전(도). 프리팹 축이 캐릭터 정면과 다를 때 보정용")]
        [SerializeField] private float climbYawOffset = 0f;



        [Header("Exit")]
        [Tooltip("하차(빠져나가기) 애니가 끝날 때까지 캐릭터를 묶어둘 시간(초). 이 동안 이동·중력·회전이 모두 정지한다. 클립 끝에 ladderExitEnd 애니 이벤트를 넣으면 이 값과 무관하게 그 시점에 정확히 풀린다.")]
        [SerializeField, Min(0f)] private float exitLockDuration = 1f;
        [Tooltip("상단에서 올라섰을 때 사다리 꼭대기보다 얼마나 위에 설지 (m)")]
        [SerializeField] private float topExitOffset = 0.3f;
        [Tooltip("하단에서 내려섰을 때 바닥 기준 오프셋 (m)")]
        [SerializeField] private float bottomExitOffset = 0f;

        [Header("Climb Position")]
        [Tooltip("등반 시 캐릭터가 붙는 위치 오프셋(로컬). X=좌우, Z=앞뒤. 사다리 기둥 중심에서 이만큼 띄워 붙음.")]
        [SerializeField] private Vector3 climbOffset = Vector3.zero;


        [Header("Auto Layout (Advanced)")]
        [Tooltip("사다리 폭 (Zone/Gizmo용)")]
        [SerializeField] private float width = 0.6f;
        [Tooltip("사다리 깊이 (Zone/Gizmo용)")]
        [SerializeField] private float depth = 0.4f;
        [Tooltip("Bottom/Top Zone의 세로 두께")]
        [SerializeField] private float zoneHeight = 0.7f;

        [Header("References (prefab 내부 자동 구성)")]
        [SerializeField] private BoxCollider climbVolume;
        [SerializeField] private LadderZone bottomZone;
        [SerializeField] private LadderZone topZone;
        [SerializeField] private Transform topExitPoint;
        [SerializeField] private Transform bottomExitPoint;

        // ─────────── 조회 API (상태가 사용) ───────────
        public float ClimbSpeed => climbSpeed;
        /// <summary>climbingUp/Down 이벤트 1회당 이동 거리(m).</summary>
        public float StepDistance => climbStepDistance;
        /// <summary>한 칸 이동 속도(m/s).</summary>
        public float StepSpeed => climbStepSpeed;
        /// <summary>하차 애니가 끝날 때까지 캐릭터를 묶어둘 시간(초). ladderExitEnd 이벤트가 오면 그 즉시 풀린다.</summary>
        public float ExitLockDuration => exitLockDuration;
        /// <summary>등반 시 바라볼 방향(true=오른쪽).</summary>
        public bool FaceRight => faceRight;
        /// <summary>등반 중 캐릭터 루트 yaw를 사다리 회전에 맞출지 여부.</summary>
        public bool AlignCharacterYaw => alignCharacterYaw;
        /// <summary>등반 중 캐릭터 루트가 가질 목표 회전(사다리 yaw + 오프셋, pitch/roll 제외).</summary>
        public Quaternion ClimbRotation => Quaternion.Euler(0f, transform.eulerAngles.y + climbYawOffset, 0f);


        private float WorldHeight => height * Mathf.Abs(transform.lossyScale.y);
        public Vector3 BottomWorld => transform.position;
        public Vector3 TopWorld => transform.position + Vector3.up * WorldHeight;
        /// <summary>주어진 높이(y)에서 사다리 중심선상의 월드 좌표(X/Z는 사다리 축).</summary>
        /// <summary>주어진 높이(y)에서 캐릭터가 붙을 사다리 중심선 월드 좌표(climbOffset 반영). X/Z는 사다리 축+오프셋.</summary>
        public Vector3 CenterAtHeight(float worldY)
        {
            Vector3 off = transform.rotation * new Vector3(climbOffset.x, 0f, climbOffset.z);
            return new Vector3(transform.position.x + off.x, worldY + climbOffset.y, transform.position.z + off.z);
        }
        public Vector3 TopExitWorld => topExitPoint != null ? topExitPoint.position : TopWorld + Vector3.up * topExitOffset;
        public Vector3 BottomExitWorld => bottomExitPoint != null ? bottomExitPoint.position : BottomWorld + Vector3.up * bottomExitOffset;

        // ─────────── Zone 통지 (LadderZone이 호출) ───────────
        public void NotifyZoneEnter(bool atTop, Character character)
        {
            if (character != null && character.Movement != null)
                character.Movement.SetAvailableLadder(this, atTop);
        }

        public void NotifyZoneExit(Character character)
        {
            if (character != null && character.Movement != null)
                character.Movement.ClearAvailableLadder(this);
        }

        private void Awake() { ResolveRefs(); LayoutZones(); }

        /// <summary>비어있는 참조를 자식 이름으로 자동 복원(프리팥 자기구성 — 레벨 제작자가 수동 연결할 필요 없음).</summary>
        private void ResolveRefs()
        {
            var t = transform;
            if (climbVolume == null) { var c = t.Find("ClimbVolume"); if (c != null) climbVolume = c.GetComponent<BoxCollider>(); }
            if (bottomZone == null) { var c = t.Find("BottomZone"); if (c != null) bottomZone = c.GetComponent<LadderZone>(); }
            if (topZone == null) { var c = t.Find("TopZone"); if (c != null) topZone = c.GetComponent<LadderZone>(); }
            if (topExitPoint == null) { var c = t.Find("TopExitPoint"); if (c != null) topExitPoint = c; }
            if (bottomExitPoint == null) { var c = t.Find("BottomExitPoint"); if (c != null) bottomExitPoint = c; }
        }

        private void OnValidate() { ResolveRefs(); LayoutZones(); }

        /// <summary>Height 기준으로 Zone/Exit 콜리더·포인트 배치. OnValidate(에디터)와 Awake(런타임) 모두에서 호출.</summary>
        /// <summary>Height 기준으로 Zone/Exit 콜리더·포인트 배치. OnValidate(에디터)와 Awake(런타임) 모두에서 호출.</summary>
        private void LayoutZones()
        {
            // 감지 Zone은 널너하게 — 벨트스크롤에서 플레이어가 조금 빗나가도 잡히게 한다.
            float bandW = Mathf.Max(width * 2f, 1.4f);
            float bandD = Mathf.Max(depth * 3f, 1.4f);
            float bandH = 1.4f;
            if (climbVolume != null)
            {
                climbVolume.isTrigger = true;
                climbVolume.center = new Vector3(0f, height * 0.5f, 0f);
                climbVolume.size = new Vector3(width, height, depth);
            }
            if (bottomZone != null)
            {
                var c = bottomZone.GetComponent<BoxCollider>();
                if (c != null) { c.isTrigger = true; c.center = new Vector3(0f, 0.5f, 0f);
                  //  c.size = new Vector3(bandW, bandH, bandD); 
                }
                bottomZone.transform.localPosition = Vector3.zero;
            }
            if (topZone != null)
            {
                var c = topZone.GetComponent<BoxCollider>();
                if (c != null) { c.isTrigger = true; c.center = new Vector3(0f, height - 0.5f, 0f);
                   // c.size = new Vector3(bandW, bandH, bandD); 
                }
                topZone.transform.localPosition = Vector3.zero;
            }
            if (topExitPoint != null) topExitPoint.localPosition = new Vector3(0f, height + topExitOffset, 0f);
            if (bottomExitPoint != null) bottomExitPoint.localPosition = new Vector3(0f, bottomExitOffset, 0f);
        }


        private void OnDrawGizmosSelected()
        {
            Vector3 b = BottomWorld;
            Vector3 t = TopWorld;
            // Climb axis
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(b, t);
            Gizmos.DrawWireSphere(b, 0.08f);
            Gizmos.DrawWireSphere(t, 0.08f);
            // Attach line (climbOffset 반영) — 캐릭터가 실제로 붙는 선
            Gizmos.color = Color.green;
            Gizmos.DrawLine(CenterAtHeight(b.y), CenterAtHeight(t.y));

            // Zones
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
            Gizmos.DrawCube(new Vector3(0f, zoneHeight * 0.5f, 0f), new Vector3(width * 1.3f, zoneHeight, depth * 1.3f));
            Gizmos.color = new Color(1f, 0.7f, 0.1f, 0.25f);
            Gizmos.DrawCube(new Vector3(0f, height - zoneHeight * 0.5f, 0f), new Vector3(width * 1.3f, zoneHeight, depth * 1.3f));
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.12f);
            Gizmos.DrawCube(new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, depth));
            Gizmos.matrix = Matrix4x4.identity;
            // Exit points
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(TopExitWorld, Vector3.one * 0.2f);
            Gizmos.DrawWireCube(BottomExitWorld, Vector3.one * 0.2f);
        }
    }
}
