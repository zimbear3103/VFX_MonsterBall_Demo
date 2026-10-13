using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Aiara
{
    public class SplineManager : MonoBehaviour
    {
        // 현재 활성화된 SplineManager (씬당 하나)
        public static SplineManager Current { get; private set; }

        [SerializeField] List<Spline> splines = new List<Spline>();

        // 성능 최적화: 프레임당 캐싱
        private Dictionary<Transform, CachedTangent> _tangentCache = new Dictionary<Transform, CachedTangent>();
        private int _lastCacheFrame = -1;

        private struct CachedTangent
        {
            public Vector3 tangent;
            public int frame;
        }

        private void OnEnable()
        {
            Current = this;
        }

        private void OnDisable()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        private void Start()
        {
            Spline previous = null;
            Spline spline = null;
            Spline next = null;

            for (int i = 0; i < splines.Count; ++i)
            {
                previous = null;
                next = null;

                spline = splines[i];
                if (i > 0)
                    previous = splines[i - 1];
                if (i < splines.Count - 1)
                    next = splines[i + 1];

                spline.Initialize(previous, next);
            }
        }

        // NOTE(ver2 포팅): 구 프로젝트의 Entity 타입은 미승계라 MonoBehaviour로 대체 (컴파일 오류 방지)
        public bool UpdateTransform(MonoBehaviour entity, bool position = false, bool force = false)
        {
            float minDistance = 99999f;
            float distance = 0f;

            Spline targetSpline = null;

            foreach (var spline in splines)
            {
                distance = Vector3.Distance(entity.transform.position, spline.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    targetSpline = spline;
                }
            }

            if (targetSpline == null)
            {
                return false;
            }

            return targetSpline.UpdateTransform(entity, position, force);
        }

        /// <summary>
        /// 주어진 위치에서 가장 가까운 스플라인을 반환합니다.
        /// </summary>
        public Spline GetNearestSpline(Vector3 position)
        {
            float minDistance = float.MaxValue;
            Spline nearestSpline = null;

            foreach (var spline in splines)
            {
                float distance = Vector3.Distance(position, spline.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestSpline = spline;
                }
            }

            return nearestSpline;
        }

        /// <summary>
        /// 주어진 Transform의 위치에서 가장 가까운 스플라인 세그먼트의 tangent 방향을 반환합니다.
        /// 프레임당 캐싱으로 성능 최적화됩니다.
        /// </summary>
        public Vector3 GetTangentAtPosition(Transform target)
        {
            if (target == null)
                return Vector3.right;

            int currentFrame = Time.frameCount;

            // 프레임이 바뀌면 캐시 클리어
            if (currentFrame != _lastCacheFrame)
            {
                _tangentCache.Clear();
                _lastCacheFrame = currentFrame;
            }

            // 캐시에 있으면 반환
            if (_tangentCache.TryGetValue(target, out CachedTangent cached))
            {
                return cached.tangent;
            }

            // 계산
            Vector3 tangent = CalculateTangentAtPosition(target.position);

            // 캐시에 저장
            _tangentCache[target] = new CachedTangent { tangent = tangent, frame = currentFrame };

            return tangent;
        }

        /// <summary>
        /// Vector3 위치로 호출하는 오버로드 (캐싱 없음, 하위 호환성)
        /// </summary>
        public Vector3 GetTangentAtPosition(Vector3 position)
        {
            return CalculateTangentAtPosition(position);
        }

        /// <summary>
        /// 실제 tangent 계산 로직
        /// </summary>
        private Vector3 CalculateTangentAtPosition(Vector3 position)
        {
            if (splines == null || splines.Count == 0)
                return Vector3.right;

            if (splines.Count == 1)
                return splines[0].Tangent;

            float minDistanceSqr = float.MaxValue;
            Vector3 bestTangent = Vector3.right;

            // Y축 무시한 위치
            float px = position.x;
            float pz = position.z;

            // 각 세그먼트(선분)에 대해 최단 거리 계산
            for (int i = 0; i < splines.Count - 1; i++)
            {
                Vector3 aPos = splines[i].transform.position;
                Vector3 bPos = splines[i + 1].transform.position;

                float ax = aPos.x, az = aPos.z;
                float bx = bPos.x, bz = bPos.z;

                // 선분 AB 벡터
                float abx = bx - ax;
                float abz = bz - az;
                float abSqrMag = abx * abx + abz * abz;

                if (abSqrMag < 0.0001f) continue; // 너무 짧은 세그먼트 스킵

                // 선분 위의 가장 가까운 점 t 계산
                float t = Mathf.Clamp01(((px - ax) * abx + (pz - az) * abz) / abSqrMag);

                // 가장 가까운 점
                float closestX = ax + t * abx;
                float closestZ = az + t * abz;

                // 거리 제곱 (sqrt 생략으로 성능 향상)
                float dx = px - closestX;
                float dz = pz - closestZ;
                float distanceSqr = dx * dx + dz * dz;

                if (distanceSqr < minDistanceSqr)
                {
                    minDistanceSqr = distanceSqr;
                    // 세그먼트의 방향을 tangent로 사용
                    float mag = Mathf.Sqrt(abSqrMag);
                    bestTangent = new Vector3(abx / mag, 0f, abz / mag);
                }
            }

            return bestTangent;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (splines != null && splines.Count > 1)
            {
                Spline point = splines[0];

                bool isSelected = (UnityEditor.Selection.activeGameObject == gameObject || (UnityEditor.Selection.activeGameObject != null && UnityEditor.Selection.activeGameObject.transform.parent && UnityEditor.Selection.activeGameObject.transform.parent.gameObject == gameObject));
                try
                {
                    for (int i = 1; i < splines.Count; i++)
                    {
                        Gizmos.color = isSelected ? Color.green : Color.white * 0.8f;
                        Spline pointB = splines[i];
                        if (!Application.isPlaying)
                        {
                            Gizmos.DrawSphere(point.transform.position, 0.2f);
                        }

                        if (i > 0)
                        {
                            Gizmos.color = isSelected ? Color.white : Color.white * 0.8f;
                            Gizmos.DrawLine(point.transform.position, pointB.transform.position);
                            if (isSelected)
                            {
                                Vector3 pathForward = (point.transform.position - pointB.transform.position);
                                Vector3 pathRight = Quaternion.AngleAxis(90, Vector3.up) * pathForward.normalized;
                                Gizmos.color = Color.green;
                                for (int a = 0; a < (int)(pathForward.magnitude); a++)
                                {
                                    DrawArrow(point.transform.position - pathForward.normalized * a, pathForward.normalized, pathRight);
                                }

                            }
                        }
                        point = pointB;
                    }
                }
                catch
                { }
                Gizmos.color = Color.white;
                if (splines[splines.Count - 1])
                {
                    Gizmos.color = isSelected ? Color.green : Color.white * 0.8f;
                    Gizmos.DrawSphere(splines[splines.Count - 1].transform.position, 0.2f);
                }
            }
        }

        void DrawArrow(Vector3 position, Vector3 pathForward, Vector3 pathRight)
        {
            Vector3 arrowLineA = position + pathForward * 0.5f + pathRight * 0.25f;
            Vector3 arrowLineB = position + pathForward * 0.5f - pathRight * 0.25f;
            Gizmos.DrawLine(position, arrowLineA);
            Gizmos.DrawLine(position, arrowLineB);
        }
#endif
    }
}
