using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Level-facing climbable wall. Only walls carrying this component can be grabbed — not
    /// every wall. Drop the prefab in, size the collider, done. Hang / mantle points are
    /// derived from the collider bounds and the approach direction at grab time, so a wall
    /// parented to a MovingPlatform carries correctly.
    ///
    /// Ledge grab requires all three checks (CharacterState.CheckLedge -> FindGrabbable):
    ///   1) a front wall face within range at lip height,
    ///   2) a real top surface (down-ray hit),
    ///   3) standing clearance above the mantle target.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Yeolha/Traversal/Climbable Wall")]
    public class ClimbableSurface : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("전방 벽면에서 이 거리 안에 있어야 잡는다 (m)")]
        [SerializeField] private float detectionRange = 0.7f;
        [Tooltip("벽 윗면(lip)보다 이만큼 아래까지 손이 닿으면 잡힘 (m)")]
        [SerializeField] private float grabHeightBelow = 0.6f;
        [Tooltip("벽 윗면보다 이만큼 위까지 허용 (m)")]
        [SerializeField] private float grabHeightAbove = 0.4f;

        [Header("Hang / Mantle")]
        [Tooltip("매달릴 때 벽 윗면에서 내려오는 깊이 (m)")]
        [SerializeField] private float hangDrop = 1.0f;
        [Tooltip("매달릴 때 벽면에서 바깥으로 띄우는 거리 (m)")]
        [SerializeField] private float hangOutward = 0.25f;
        [Tooltip("올라선 뒤 윗면 안쪽으로 들어오는 거리 (m)")]
        [SerializeField] private float standInset = 0.45f;
        [Tooltip("윗면 위로 살짝 띄우는 높이 (m)")]
        [SerializeField] private float standUp = 0.05f;
        [Tooltip("맨틀(올라서기) 보간 시간 (s)")]
        [SerializeField] private float mantleDuration = 0.45f;

        [Header("Clearance (Advanced)")]
        [Tooltip("올라설 자리 위 장애물 검사 반경 (m)")]
        [SerializeField] private float clearanceRadius = 0.3f;
        [Tooltip("올라설 자리 위 필요한 여유 높이 (m)")]
        [SerializeField] private float clearanceHeight = 1.7f;

        [Header("Ledge Hang (trigger)")]
        [Tooltip("매달리는 위치 오프셋(로컬). 트리거에 닿으면 이 위치로 스납")]
        [SerializeField] private Vector3 hangOffset = new Vector3(0f, -1f, 0f);
        [Tooltip("HangExit 모션 후 이동할 지점(비우면 hangOffset 위치)")]
        [SerializeField] private Transform exitPoint;
        [Tooltip("HangExit 모션 길이(초). 이 시간 뒤 ExitPoint로 이동하고 상태 탈출")]
        [SerializeField, Min(0.05f)] private float exitDuration = 1f;
        [Tooltip("이 벽에 매달릴 때 바라볼 방향. 체크=오른쪽, 해제=왼쪽")]
        [SerializeField] private bool faceRight = true;
        [Tooltip("매달림 중 캐릭터 루트 yaw를 벽 회전에 맞출지 여부")]
        [SerializeField] private bool alignCharacterYaw = true;
        [Tooltip("벽 yaw 기준 추가 회전(도). 프리팹 축이 캐릭터 정면과 다를 때 보정용")]
        [SerializeField] private float hangYawOffset = 0f;


        private Collider _col;
        private Vector3 _hangWorld;
        private Vector3 _mantleWorld;

        public float MantleDuration => exitDuration;
        /// <summary>가장 최근 TryGrab에서 계산된 매달림 지점(월드).</summary>
        public Vector3 HangWorld => transform.position + transform.rotation * hangOffset;
        /// <summary>HangExit 후 이동할 지점(월드).</summary>
        public Vector3 ExitWorld => exitPoint != null ? exitPoint.position : (transform.position + transform.rotation * hangOffset);
        /// <summary>매달릴 때 바라볼 방향(true=오른쪽).</summary>
        public bool FaceRight => faceRight;
        /// <summary>매달림 중 캐릭터 루트 yaw를 벽 회전에 맞출지 여부.</summary>
        public bool AlignCharacterYaw => alignCharacterYaw;
        /// <summary>매달림 중 캐릭터 루트가 가질 목표 회전(벽 yaw + 오프셋, pitch/roll 제외).</summary>
        public Quaternion HangRotation => Quaternion.Euler(0f, transform.eulerAngles.y + hangYawOffset, 0f);
        /// <summary>HangExit 모션 길이(초).</summary>
        public float ExitDuration => exitDuration;
        /// <summary>가장 최근 TryGrab에서 계산된 올라설 지점(월드).</summary>
        public Vector3 MantleWorld => ExitWorld;

        private static readonly List<ClimbableSurface> _all = new List<ClimbableSurface>();

        private void Awake()
        {
            _col = GetComponent<Collider>();
            if (exitPoint == null) { var e = transform.Find("ExitPoint"); if (e != null) exitPoint = e; }
        }

        private void OnTriggerEnter(Collider other)
        {
            var ch = other.GetComponentInParent<Character>();
            if (ch != null && ch.Movement != null) ch.Movement.SetAvailableClimbable(this);
        }

        private void OnTriggerExit(Collider other)
        {
            var ch = other.GetComponentInParent<Character>();
            if (ch != null && ch.Movement != null) ch.Movement.ClearAvailableClimbable(this);
        }
        private void OnEnable() { if (_col == null) _col = GetComponent<Collider>(); if (!_all.Contains(this)) _all.Add(this); }
        private void OnDisable() { _all.Remove(this); }

        /// <summary>모든 등록된 벽 중 잡을 수 있는 첫 벽을 반환(없으면 null). CharacterState.CheckLedge가 호출.</summary>






        private void OnDrawGizmosSelected()
        {
            Vector3 hang = transform.position + transform.rotation * hangOffset;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(hang, 0.15f); // 매달림 지점
            Vector3 exit = exitPoint != null ? exitPoint.position : hang;
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(exit, Vector3.one * 0.2f); // 이탈 지점
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(hang, exit);
        }
    }
}
