using UnityEngine;

namespace Aiara
{
    /// <summary>
    /// 활성화된 후 지정 시간이 지나면 자동으로 비활성화됩니다.
    /// 오브젝트 풀링 시 재사용을 위해 이펙트 프리팹에 붙여 사용합니다.
    /// </summary>
    public class AutoDeactivate : MonoBehaviour
    {
        [Tooltip("활성화 후 비활성화까지의 시간 (초)")]
        public float Lifetime = 2f;

        private float _timer;

        private void OnEnable()
        {
            _timer = Lifetime;
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
