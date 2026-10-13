using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 맵 끝 등에 배치하는 씬 전환 포탈. 트리거에 상주 플레이어가 진입하면
    /// MultiSceneLoader에 지정한 SceneGroup 로드를 요청한다.
    ///
    /// 실제 씬 언로드/로드/EntryPoint 배치는 MultiSceneLoader가 담당하며,
    /// Game 씬과 상주 오브젝트(Player / CameraRig)는 그대로 유지된다.
    /// (즉, "Game·상주 오브젝트를 뺀 나머지 씬을 언로드하고 새 그룹을 로드"하는
    ///  동작은 MultiSceneLoader.LoadGroup이 이미 수행한다.)
    ///
    /// 포탈은 로더의 public API(LoadGroup)만 호출하는 얇은 소비자이며
    /// 프레임워크 구조를 바꾸지 않는다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Yeolha/Scene System/Portal")]
    public class Portal : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("이 포탈이 로드할 대상 SceneGroup")]
        [SerializeField] private SceneGroup targetGroup;

        [Tooltip("대상 그룹에서 플레이어를 배치할 EntryKey. 비우면 그룹의 DefaultEntryKey 사용.")]
        [SerializeField] private string entryKey = "";

        [Header("Activation")]
        [Tooltip("체크되어 있을 때만 발동한다. 외부(스테이지 클리어 등)에서 SetActive로 제어 가능.")]
        [SerializeField] private bool active = true;

        private bool _fired;

        public SceneGroup TargetGroup => targetGroup;
        public string EntryKey => entryKey;
        public bool Active => active;

        /// <summary>외부에서 포탈 발동 가능 여부를 제어한다. (예: 전투 종료 후 개방)</summary>
        public void SetActive(bool value) => active = value;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!active || _fired) return;

            var character = other.GetComponentInParent<Character>();
            if (character == null) return;

            // 상주 플레이어만 포탈을 발동시킨다.
            if (GameManager.Instance == null || character != GameManager.Instance.Player)
                return;

            if (targetGroup == null)
            {
                Debug.LogError("[Portal] targetGroup이 지정되지 않았습니다.", this);
                return;
            }

            var loader = MultiSceneLoader.Instance;
            if (loader == null)
            {
                Debug.LogError("[Portal] MultiSceneLoader를 찾지 못했습니다. Game 씬에 로더가 있는지 확인하세요.", this);
                return;
            }
            if (loader.IsLoading) return;

            _fired = true;
            string key = string.IsNullOrEmpty(entryKey) ? null : entryKey;
            loader.LoadGroup(targetGroup, key);
        }
    }
}
