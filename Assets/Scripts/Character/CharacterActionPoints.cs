using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 잡기/처형 연출용 기준점(앵커) 데이터 — 구 프로젝트 CharacterActionPoints 이관.
    ///
    /// 시전자(플레이어) 측: GrabPoints(GrabType별 잡기 정렬 기준), ExecutePoint(처형 대상 탐색 중심).
    /// 피격자(적) 측: GrabbedPoint(잡힐 때 자기 몸의 기준 오프셋), ExecutePoint(시전자가 접근할 위치),
    ///               ExecuteEndPoint(처형 종료 후 시전자가 서는 위치), 잡기/처형 가능 여부 + 타입.
    /// Transform 미지정 시 캐릭터 루트를 사용한다(그레이스풀 폴백).
    /// </summary>
    [AddComponentMenu("Yeolha/Character/Character Action Points")]
    public class CharacterActionPoints : MonoBehaviour
    {
        [System.Serializable]
        public class GrabPointEntry
        {
            [Tooltip("이 기준점을 사용할 GrabType (Animator GrabType Int와 동일)")]
            public int grabType = 1;
            [Tooltip("잡힌 적을 정렬할 기준 Transform (시전자 기준)")]
            public Transform point;
        }

        [Header("Attacker Anchors")]
        [Tooltip("GrabType별 잡기 정렬 기준점 목록. 미지정 GrabType은 defaultGrabPoint → 루트 순 폴백")]
        [SerializeField] private List<GrabPointEntry> grabPoints = new List<GrabPointEntry>();
        [Tooltip("GrabType 매칭 실패 시 사용할 기본 잡기 기준점")]
        [SerializeField] private Transform defaultGrabPoint;

        [Header("Victim Anchors")]
        [Tooltip("잡힐 때 자기 몸의 기준점(이 점이 시전자 GrabPoint에 맞춰진다). 비우면 루트")]
        [SerializeField] private Transform grabbedPoint;
        [Tooltip("처형 시전자가 접근/정렬할 위치. 비우면 루트")]
        [SerializeField] private Transform executePoint;
        [Tooltip("처형 종료 후 시전자가 서는 위치. 비우면 executePoint → 루트 순 폴백")]
        [SerializeField] private Transform executeEndPoint;
        [Tooltip("처형 중 애니 이벤트 이펙트(피격자 기준)의 기준 위치. 비우면 루트. 자식 Transform을 드래그해 적별로 이펙트 위치 조절")]
        [SerializeField] private Transform executeEffectPoint;

        [Header("Victim Capability")]
        [Tooltip("이 캐릭터를 콤보 파생 잡기(ActionGrab)로 잡을 수 있는가 (구버전 CanBeActionGrabbed)")]
        [SerializeField] private bool canBeGrabbed = true;
        [Tooltip("이 캐릭터를 잡을 때 사용할 기본 GrabType (0 = ActionGrab1). 음수 = 잡기 면역 (구버전 GrabType)")]
        [SerializeField] private int victimGrabType = 0;
        [Tooltip("이 캐릭터를 처형할 수 있는가 (구버전 CanBeExecuted)")]
        [SerializeField] private bool canBeExecuted = true;
        [Tooltip("처형 시 사용할 ExecuteType (0 = ExecuteA, 1 = ExecuteB, 2 = ExecuteC). 음수 = 처형 면역 (구버전 ExecuteTypeValue)")]
        [SerializeField] private int executeType = 0;
        [Tooltip("처형 시 시전자가 피격자와 같은 방향을 보는가. false = 마주보기 (구버전 ExecutorFacing Same/Opposite)")]
        [SerializeField] private bool executorFacesSameDirection = false;

        public bool CanBeGrabbed => canBeGrabbed && victimGrabType >= 0;
        public int VictimGrabType => victimGrabType;
        public bool CanBeExecuted => canBeExecuted && executeType >= 0;
        public int ExecuteType => executeType;
        public bool ExecutorFacesSameDirection => executorFacesSameDirection;

        /// <summary>GrabType에 해당하는 시전자 잡기 기준점(월드 Transform). 폴백: default → 루트.</summary>
        public Transform ResolveGrabPoint(int grabType)
        {
            for (int i = 0; i < grabPoints.Count; i++)
                if (grabPoints[i] != null && grabPoints[i].grabType == grabType && grabPoints[i].point != null)
                    return grabPoints[i].point;
            return defaultGrabPoint != null ? defaultGrabPoint : transform;
        }

        /// <summary>피격자 몸의 잡힘 기준점. 비우면 루트.</summary>
        public Transform GrabbedPoint => grabbedPoint != null ? grabbedPoint : transform;
        /// <summary>처형 접근 목표 지점. 비우면 루트.</summary>
        public Transform ExecutePoint => executePoint != null ? executePoint : transform;
        /// <summary>처형 종료 후 시전자 위치. 폴백: executePoint → 루트.</summary>
        public Transform ExecuteEndPoint => executeEndPoint != null ? executeEndPoint : ExecutePoint;
        /// <summary>처형 이벤트 이펙트 기준 위치. 비우면 루트.</summary>
        public Transform ExecuteEffectPoint => executeEffectPoint != null ? executeEffectPoint : transform;
    }
}
