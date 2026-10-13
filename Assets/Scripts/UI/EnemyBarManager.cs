using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// AIController Registry에 등록된 적에게 EnemyStatusBarBinder를 자동 부착하는 매니저 (씬 1개).
    /// 프리팹을 수정하지 않고 런타임에 부여한다 — 스폰/씬 배치 적 모두 커버 (0.5s 주기 스캔).
    /// 바 디자인은 구버전 EnemyStatusBar 프리팹, 포스트프로세싱 차단은 WorldBarRTRig(RT 리그)가 담당.
    /// </summary>
    public class EnemyBarManager : MonoBehaviour
    {
        [Tooltip("등록 적 스캔 주기(초)")]
        [SerializeField] private float scanInterval = 0.5f;
        [Tooltip("적 머리 위 바 높이 오프셋(m)")]
        [SerializeField] private float barHeightOffset = 2.3f;
        [Tooltip("적 머리 위 바 프리팹 (구버전 EnemyStatusBar 이관)")]
        [SerializeField] private GameObject barPrefab;
        [Tooltip("바가 렌더될 레이어 (WorldBarRTRig와 동일하게 유지)")]
        [SerializeField] private string barLayerName = "Bars";

        private float _timer;

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = scanInterval;
            Scan();
        }

        private void Scan()
        {
            var ai = AIController.Instance;
            if (ai == null) return;
            var list = ai.Characters;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var enemy = list[i];
                if (enemy == null) continue;
                if (enemy.GetComponent<EnemyStatusBarBinder>() != null) continue;
                var binder = enemy.gameObject.AddComponent<EnemyStatusBarBinder>();
                binder.Init(barPrefab, barHeightOffset, barLayerName);
            }
        }
    }
}
