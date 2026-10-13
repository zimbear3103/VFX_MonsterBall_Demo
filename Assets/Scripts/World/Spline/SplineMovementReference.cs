using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 특정 월드 위치에 대한 스플라인 좌표계 정보.
    /// Forward = 스테이지 진행 방향(수평), Depth = 화면 깊이 방향(Forward에 수직).
    /// </summary>
    public struct SplineFrame
    {
        public Vector3 Forward;      // 수평화 + 정규화된 진행 방향
        public Vector3 Depth;        // Cross(up, Forward)
        public Vector3 NearestPoint; // 경로 위 최근접 월드 좌표
        public float T;              // 경로 전체 대비 진행률 (0~1, 근사)
        public bool Valid;           // 경로에서 유효하게 계산되었는가

        public static SplineFrame Fallback(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.right;
            forward.Normalize();
            return new SplineFrame
            {
                Forward = forward,
                Depth = Vector3.Cross(Vector3.up, forward).normalized,
                NearestPoint = Vector3.zero,
                T = 0f,
                Valid = false
            };
        }
    }

    /// <summary>
    /// 플레이어와 카메라가 공유하는 스플라인 기준 좌표계.
    ///
    /// 별도 패키지나 SplineContainer 없이, Transform 포인트 체인만으로 동작한다.
    /// 포인트는 자식 Transform을 순서대로 배치하거나 points 리스트에 등록한다.
    /// 씬에서 포인트를 드래그하면 경로가 즉시 반영된다.
    ///
    /// 역할:
    ///   월드 위치 → 최근접 세그먼트 → 진행 방향(접선) / 깊이 방향 계산.
    ///   (최근접 판정은 기존 프로젝트와 동일하게 Y를 무시한 XZ 평면 기준)
    ///
    /// 코너에서는 인접 세그먼트 방향을 거리 기반으로 블렌드해
    /// 꺾이는 지점에서 이동축/카메라가 홱 도는 것을 완화한다.
    /// 추가 스무딩(댐핑)은 소비자(PlayerMovement, PivotNode)가 각자 적용한다.
    /// </summary>
    public class SplineMovementReference : MonoBehaviour
    {
        [Header("Path Points")]
        [Tooltip("경로 포인트. 비워두면 자식 Transform들을 순서대로 사용한다.")]
        [SerializeField] private List<Transform> points = new List<Transform>();

        [Tooltip("포인트 리스트가 비어 있을 때 자식 Transform을 자동 사용")]
        [SerializeField] private bool useChildrenIfEmpty = true;

        [Header("Corner")]
        [Tooltip("코너(포인트 연결부) 주변에서 인접 세그먼트 방향을 블렌드할 거리 (m). 0이면 블렌드 없음.")]
        [SerializeField] private float cornerSmoothDistance = 3f;

        [Header("Stability")]
        [Tooltip("접선이 직전 프레임과 반대 방향으로 급반전되는 것을 방지")]
        [SerializeField] private bool preventTangentFlip = true;

        [Tooltip("경로가 없거나 계산 실패 시 사용할 기본 진행 방향")]
        [SerializeField] private Vector3 fallbackForward = Vector3.right;

        private readonly List<Transform> _resolved = new List<Transform>();
        private Vector3 _lastForward;
        private bool _hasLastForward;

        /// <summary>
        /// 스테이지 스플라인 교체 (MultiSceneLoader가 그룹 로드 시 호출).
        /// 이전 스테이지의 접선 잔존을 막기 위해 방향 캐시도 초기화한다.
        /// </summary>
        public void SetPoints(IReadOnlyList<Transform> newPoints)
        {
            points.Clear();
            if (newPoints != null)
            {
                for (int i = 0; i < newPoints.Count; i++)
                {
                    if (newPoints[i] != null)
                        points.Add(newPoints[i]);
                }
            }
            _hasLastForward = false;
        }

        /// <summary>유효한 경로(포인트 2개 이상)가 있는가.</summary>
        public bool HasSpline
        {
            get
            {
                ResolvePoints();
                return _resolved.Count >= 2;
            }
        }

        private void ResolvePoints()
        {
            _resolved.Clear();

            if (points.Count > 0)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    if (points[i] != null)
                        _resolved.Add(points[i]);
                }
            }
            else if (useChildrenIfEmpty)
            {
                for (int i = 0; i < transform.childCount; i++)
                    _resolved.Add(transform.GetChild(i));
            }
        }

        /// <summary>세그먼트 i의 수평 방향 (정규화). 퇴화 세그먼트면 zero.</summary>
        private Vector3 SegmentDirection(int i)
        {
            Vector3 d = _resolved[i + 1].position - _resolved[i].position;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
        }

        /// <summary>주어진 월드 위치의 스플라인 좌표계를 계산한다.</summary>
        public SplineFrame GetFrame(Vector3 worldPosition)
        {
            ResolvePoints();

            if (_resolved.Count < 2)
                return SplineFrame.Fallback(_hasLastForward ? _lastForward : fallbackForward);

            // XZ 평면 기준 최근접 세그먼트 탐색 (기존 프로젝트와 동일 규칙)
            float px = worldPosition.x;
            float pz = worldPosition.z;

            float bestDistanceSqr = float.MaxValue;
            int bestSegment = -1;
            float bestT = 0f;
            Vector3 bestClosest = Vector3.zero;

            float totalLength = 0f;
            float lengthBeforeBest = 0f;

            for (int i = 0; i < _resolved.Count - 1; i++)
            {
                Vector3 a = _resolved[i].position;
                Vector3 b = _resolved[i + 1].position;

                float abx = b.x - a.x;
                float abz = b.z - a.z;
                float abSqr = abx * abx + abz * abz;
                float segmentLength = Mathf.Sqrt(abSqr);

                if (abSqr < 0.0001f)
                {
                    totalLength += segmentLength;
                    continue;
                }

                float t = Mathf.Clamp01(((px - a.x) * abx + (pz - a.z) * abz) / abSqr);
                float cx = a.x + t * abx;
                float cz = a.z + t * abz;
                float dx = px - cx;
                float dz = pz - cz;
                float distanceSqr = dx * dx + dz * dz;

                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestSegment = i;
                    bestT = t;
                    bestClosest = Vector3.Lerp(a, b, t); // Y 포함 (경로 높이 반영)
                    lengthBeforeBest = totalLength + t * segmentLength;
                }

                totalLength += segmentLength;
            }

            if (bestSegment < 0)
                return SplineFrame.Fallback(_hasLastForward ? _lastForward : fallbackForward);

            // 기본 접선 = 세그먼트 방향
            Vector3 forward = SegmentDirection(bestSegment);
            if (forward == Vector3.zero)
                return SplineFrame.Fallback(_hasLastForward ? _lastForward : fallbackForward);

            // 코너 블렌드: 연결부에 가까울수록 인접 세그먼트 방향을 섞는다
            if (cornerSmoothDistance > 0f)
            {
                float segmentLength = Vector3.Distance(
                    new Vector3(_resolved[bestSegment].position.x, 0f, _resolved[bestSegment].position.z),
                    new Vector3(_resolved[bestSegment + 1].position.x, 0f, _resolved[bestSegment + 1].position.z));

                float distToStart = bestT * segmentLength;
                float distToEnd = (1f - bestT) * segmentLength;

                if (bestSegment > 0 && distToStart < cornerSmoothDistance)
                {
                    Vector3 prev = SegmentDirection(bestSegment - 1);
                    if (prev != Vector3.zero)
                    {
                        // 연결부 정중앙에서 50:50이 되도록 0~0.5 가중치
                        float w = 0.5f * (1f - distToStart / cornerSmoothDistance);
                        forward = Vector3.Slerp(forward, prev, w);
                    }
                }

                if (bestSegment < _resolved.Count - 2 && distToEnd < cornerSmoothDistance)
                {
                    Vector3 next = SegmentDirection(bestSegment + 1);
                    if (next != Vector3.zero)
                    {
                        float w = 0.5f * (1f - distToEnd / cornerSmoothDistance);
                        forward = Vector3.Slerp(forward, next, w);
                    }
                }
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return SplineFrame.Fallback(_hasLastForward ? _lastForward : fallbackForward);
            forward.Normalize();

            // 접선 급반전 방지
            if (preventTangentFlip && _hasLastForward && Vector3.Dot(forward, _lastForward) < 0f)
                forward = -forward;

            _lastForward = forward;
            _hasLastForward = true;

            return new SplineFrame
            {
                Forward = forward,
                Depth = Vector3.Cross(Vector3.up, forward).normalized,
                NearestPoint = bestClosest,
                T = totalLength > 0.0001f ? Mathf.Clamp01(lengthBeforeBest / totalLength) : 0f,
                Valid = true
            };
        }

        /// <summary>진행 방향만 필요할 때의 편의 접근자.</summary>
        public Vector3 GetForward(Vector3 worldPosition) => GetFrame(worldPosition).Forward;

        /// <summary>깊이 방향만 필요할 때의 편의 접근자.</summary>
        public Vector3 GetDepth(Vector3 worldPosition) => GetFrame(worldPosition).Depth;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            ResolvePoints();
            for (int i = 0; i < _resolved.Count; i++)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(_resolved[i].position, 0.25f);
                if (i > 0)
                {
                    Gizmos.color = Color.white;
                    Gizmos.DrawLine(_resolved[i - 1].position, _resolved[i].position);
                }
            }
        }
#endif
    }
}
