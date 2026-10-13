using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 그룹 전환 / 동일 그룹 텔레포트 포털.
    /// 구버전 MultiScenePortal(TDE GoToLevelEntryPoint 상속)에서
    /// (Target SceneGroup + EntryKey) 개념만 승계한 순수 트리거.
    ///
    /// 책임: 플레이어 진입 → 현재 그룹과 같으면 Teleport, 다르면 LoadGroup 요청.
    /// </summary>
    [AddComponentMenu("Yeolha/Scene System/Multi Scene Portal")]
    [RequireComponent(typeof(Collider))]
    public class MultiScenePortal : MonoBehaviour
    {
        [Tooltip("이동할 대상 SceneGroup. 현재 그룹과 같으면 텔레포트만 수행.")]
        [SerializeField] private SceneGroup targetGroup;

        [Tooltip("도착 씬의 SceneEntryPoint.EntryKey")]
        [SerializeField] private string entryKey = "Start";

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<Character>() == null) return;
            if (targetGroup == null)
            {
                Debug.LogWarning("[MultiScenePortal] TargetGroup이 비어있습니다.", this);
                return;
            }

            var loader = MultiSceneLoader.Instance;
            if (loader == null)
            {
                Debug.LogError("[MultiScenePortal] MultiSceneLoader가 없습니다. Game.unity 로드 여부를 확인하세요.", this);
                return;
            }

            if (loader.CurrentGroup == targetGroup)
                loader.TeleportToEntryPoint(entryKey);
            else
                loader.LoadGroup(targetGroup, entryKey);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.9f);
            var col = GetComponent<Collider>();
            if (col is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else
            {
                Gizmos.DrawWireSphere(transform.position, 1f);
            }
        }
#endif
    }
}
