using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 잔상(Afterimage) 연출 컴포넌트 — 구버전 KETO.CharacterTrail 이관(프레임워크 규격 재작성).
    ///
    /// 원리: 지정 시간(초) 동안 interval 간격으로 현재 SkinnedMeshRenderer들을 BakeMesh해
    /// 월드에 고정된 정지 메시(고스트)로 남기고, 각 고스트를 _Alpha/_LerpAmount로 페이드아웃한다.
    /// 고스트는 캐릭터를 따라가지 않도록 부모 없는 월드 컨테이너(Afterimage) 아래에 둔다.
    ///
    /// 사용처(구버전 동일):
    ///   - 강공격 대시 캔슬: CharacterAttackState가 Spawn(대시 길이) 호출.
    ///   - 절명기 접근 대시: CharacterExecutionPresentation이 Spawn(ExecutionApproachDuration) 호출.
    ///
    /// 트리거/타이밍만 상태·연출 계층이 소유하고, 이 컴포넌트는 "보이는 잔상"만 담당한다.
    /// </summary>
    [AddComponentMenu("Yeolha/Visual/Character Afterimage")]
    public class CharacterAfterimage : MonoBehaviour
    {
        [Header("Spawn")]
        [Tooltip("고스트를 남기는 간격(초). 작을수록 촘촘한 잔상.")]
        [SerializeField, Range(0.01f, 0.2f)] private float interval = 0.05f;

        [Tooltip("각 고스트 메시가 씬에 남아있는 최대 시간(초). 페이드아웃 후 파괴.")]
        [SerializeField, Range(0.1f, 5f)] private float destroyDelay = 3f;

        [Tooltip("고스트의 기준 위치/회전. 비우면 이 컴포넌트의 Transform 사용.")]
        [SerializeField] private Transform spawnTransform;

        [Tooltip("고스트 메시에 입힐 머티리얼(_Alpha/_LerpAmount 지원 필요). 비우면 잔상 생략.")]
        [SerializeField] private Material trailMaterial;

        [Header("Optional")]
        [Tooltip("잔상 동안 함께 켜둘 파티클(있으면). 없으면 무시.")]
        [SerializeField] private GameObject particles;

        // 셰이더 프로퍼티 (구버전 CharacterTrail.mat 규격)
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int LerpAmountId = Shader.PropertyToID("_LerpAmount");

        private SkinnedMeshRenderer[] _skinnedMeshRenderers;
        private GameObject _trailParent;
        private bool _missingMaterialWarned;

        private void Awake()
        {
            CollectRenderers();
        }

        private void Start()
        {
            if (particles != null)
                particles.SetActive(false);
        }

        /// <summary>
        /// 지정 시간(초) 동안 잔상을 생성한다. 이미 진행 중이어도 겹쳐 생성 가능(코루틴 병렬).
        /// 전달값을 그대로 지속시간으로 사용한다(구버전 수정 반영 — 긴 이동에서도 끝까지 잔상 유지).
        /// </summary>
        public bool Spawn(float spawnDuration)
        {
            if (spawnDuration <= 0f) return false;
            if (trailMaterial == null)
            {
                if (!_missingMaterialWarned)
                {
                    Debug.LogWarning("[CharacterAfterimage] trailMaterial 미할당 — 잔상을 생략한다.", this);
                    _missingMaterialWarned = true;
                }
                return false;
            }
            if (!isActiveAndEnabled) return false;

            CollectRenderers();
            EnsureTrailParent();
            if (particles != null) particles.SetActive(true);
            StartCoroutine(SpawnTrailCoroutine(spawnDuration));
            return true;
        }

        private void CollectRenderers()
        {
            if (_skinnedMeshRenderers != null && _skinnedMeshRenderers.Length > 0) return;
            _skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        /// <summary>
        /// 고스트 컨테이너 보장. 부모 없는 루트로 두어 잔상이 캐릭터를 따라가지 않고 월드에 고정된다.
        /// 씬 전환 등으로 파괴됐으면 lazy로 재생성한다.
        /// </summary>
        private void EnsureTrailParent()
        {
            if (_trailParent == null)
                _trailParent = new GameObject("Afterimage");
        }

        private IEnumerator SpawnTrailCoroutine(float elapsedTime)
        {
            while (elapsedTime > 0f)
            {
                elapsedTime -= interval;

                // 대기 중 캐릭터/컨테이너가 파괴되면 안전하게 종료
                if (this == null || _trailParent == null)
                    yield break;

                for (int i = 0; i < _skinnedMeshRenderers.Length; i++)
                {
                    var smr = _skinnedMeshRenderers[i];
                    if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy)
                        continue;

                    // 기준 Transform: 지정값이 있으면 그것, 없으면 각 메시 자신(자세/스케일이 정확히 일치).
                    Transform anchor = spawnTransform != null ? spawnTransform : smr.transform;

                    // useScale=true: 트랜스폼 스케일을 빼고 순수 로컬 공간으로 베이크(스케일/미러는 아래 행렬에서 한 번만 적용).
                    Mesh mesh = new Mesh();
                    smr.BakeMesh(mesh, true);

                    // 캐릭터 좌우 반전이 음수 스케일(미러)이라 rotation+lossyScale 분해로는 미러가 깨진다.
                    // 베이크 정점을 월드 행렬로 직접 변환해 고스트를 원점(identity)에 두면 미러/회전이 정확히 보존된다.
                    BakeToWorld(mesh, anchor.localToWorldMatrix);

                    GameObject ghost = new GameObject("AfterimageGhost");
                    ghost.transform.SetParent(_trailParent.transform, false);
                    ghost.transform.position = Vector3.zero;
                    ghost.transform.rotation = Quaternion.identity;
                    ghost.transform.localScale = Vector3.one;

                    var meshFilter = ghost.AddComponent<MeshFilter>();
                    var meshRenderer = ghost.AddComponent<MeshRenderer>();
                    meshFilter.mesh = mesh;
                    meshRenderer.material = trailMaterial;
                    meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = false;

                    StartCoroutine(FadeOut(meshRenderer));
                    Destroy(ghost, destroyDelay);
                }

                yield return new WaitForSeconds(interval);
            }

            if (particles != null) particles.SetActive(false);
        }

        /// <summary>
        /// 베이크 메시(SMR 로컬 공간)를 월드 공간으로 변환한다. 미러(행렬식 음수)면 삼각형 감기 순서를 뒤집어
        /// 백페이스 컬링이 정상 동작하도록 한다(음수 스케일 트랜스폼 렌더 시 Unity가 자동 처리하던 것을 수동 재현).
        /// </summary>
        private static void BakeToWorld(Mesh mesh, Matrix4x4 localToWorld)
        {
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = localToWorld.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;

            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                Matrix4x4 normalMatrix = localToWorld.inverse.transpose;
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                mesh.normals = normals;
            }

            Vector4[] tangents = mesh.tangents;
            if (tangents != null && tangents.Length == vertices.Length)
            {
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector3 t = localToWorld.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                    tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                }
                mesh.tangents = tangents;
            }

            if (localToWorld.determinant < 0f)
            {
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] tris = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                    {
                        int tmp = tris[i + 1];
                        tris[i + 1] = tris[i + 2];
                        tris[i + 2] = tmp;
                    }
                    mesh.SetTriangles(tris, sub);
                }
            }

            mesh.RecalculateBounds();
        }

        private IEnumerator FadeOut(MeshRenderer meshRenderer)
        {
            if (meshRenderer == null) yield break;
            Material mat = meshRenderer.material; // 인스턴스 (고스트 파괴 시 함께 정리됨)
            float value = 1f;

            while (value > 0f)
            {
                if (meshRenderer == null) yield break;
                value -= interval;
                mat.SetFloat(AlphaId, value);
                mat.SetFloat(LerpAmountId, 1f - value);
                yield return new WaitForSeconds(interval);
            }
        }

        private void OnDisable()
        {
            // 비활성/파괴 시 실행 중 코루틴을 중단해 dangling 참조 접근을 막는다.
            // 컨테이너 정리는 실제 파괴(OnDestroy)에서만 — 단순 비활성 후 재활성 대비.
            StopAllCoroutines();
            if (particles != null) particles.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_trailParent != null)
                Destroy(_trailParent);
        }
    }
}
