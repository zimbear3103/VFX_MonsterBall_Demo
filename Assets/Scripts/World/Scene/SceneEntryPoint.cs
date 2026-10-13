using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 씬 진입점 마커. 구버전 SceneEntryPoint(TDE CheckPoint 상속)에서
    /// EntryKey + Transform 위치/회전 개념만 승계한 순수 마커.
    /// Scene 전환 시 (SceneGroup + EntryKey) 조합으로 플레이어 배치 위치를 결정한다.
    /// </summary>
    [AddComponentMenu("Yeolha/Scene System/Scene Entry Point")]
    public class SceneEntryPoint : MonoBehaviour
    {
        [Tooltip("포털/로더가 참조할 식별자. 같은 SceneGroup 안에서 유니크해야 한다.")]
        [SerializeField] private string entryKey = "Start";

        [Tooltip("스폰 시 플레이어가 오른쪽(스플라인 진행 방향)을 보게 할지")]
        [SerializeField] private bool faceRight = true;

        public string EntryKey => entryKey;
        public bool FaceRight => faceRight;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(entryKey))
                Debug.LogWarning($"[SceneEntryPoint] '{name}' 의 EntryKey가 비어있습니다.", this);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
        }
#endif
    }
}
