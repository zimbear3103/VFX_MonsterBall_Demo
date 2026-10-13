using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 절명기 가능 표시 — 그로기 상태의 적 머리 위에 처형 입력 아이콘(R3Button)을 띄운다.
    /// 구버전 CharacterExecuted.UpdateExecutionIndicator / UpdateIndicatorSideAndPosition 이관.
    ///
    /// 표시 조건: CurrentState == Groggy + 생존 + CharacterActionPoints.CanBeExecuted.
    /// 그로기 진입 후 appearDelay(구버전 ExecutionIndicatorDelay=1s) 뒤에 등장하고,
    /// 처형 시작(Executing/Executed)·그로기 회복·사망 시 즉시 숨는다.
    /// 좌/우 스프라이트·오프셋은 메인 카메라의 화면 x좌표 기준으로 판정한다
    /// (월드 x 비교 금지 — 스플라인 구간에서 좌우가 뒤집힌다. 구버전 주석 계승).
    /// 프리팹 인스턴스는 lazy Instantiate 후 SetActive 토글로 재사용한다.
    /// </summary>
    [RequireComponent(typeof(Character))]
    [AddComponentMenu("Yeolha/UI/Execution Indicator")]
    public class ExecutionIndicator : MonoBehaviour
    {
        [Tooltip("표시할 인디케이터 프리팹 (구버전 R3Button). 비우면 아무것도 표시하지 않는다")]
        [SerializeField] private GameObject indicatorPrefab;
        [Tooltip("머리 위 표시 높이(m) (구버전 ExecutionIndicatorHeight)")]
        [SerializeField] private float height = 1.5f;
        [Tooltip("플레이어 쪽으로의 가로 오프셋(m) (구버전 ExecutionIndicatorSideOffset)")]
        [SerializeField] private float sideOffset = 1.5f;
        [Tooltip("그로기 진입 후 표시까지 지연(초) (구버전 ExecutionIndicatorDelay)")]
        [SerializeField] private float appearDelay = 1f;

        private Character _character;
        private CharacterActionPoints _points;
        private GameObject _instance;
        private ExecutionIndicatorView _view;
        private float _groggyTime;
        private bool _wasEligible;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _points = GetComponent<CharacterActionPoints>();
        }

        private void OnDisable()
        {
            _wasEligible = false;
            if (_instance != null) _instance.SetActive(false);
        }

        private void LateUpdate()
        {
            bool eligible = indicatorPrefab != null
                && _character != null
                && !_character.IsDead
                && _character.CurrentState == CharacterStateType.Groggy
                && (_points == null || _points.CanBeExecuted);

            if (eligible && !_wasEligible)
                _groggyTime = Time.time; // 그로기 진입 시각 기록 → 지연 후 등장
            _wasEligible = eligible;

            bool show = eligible && Time.time - _groggyTime >= appearDelay;
            if (!show)
            {
                if (_instance != null && _instance.activeSelf) _instance.SetActive(false);
                return;
            }

            if (_instance == null)
            {
                _instance = Instantiate(indicatorPrefab);
                _view = _instance.GetComponent<ExecutionIndicatorView>();
            }
            if (!_instance.activeSelf) _instance.SetActive(true);

            // 좌/우 판정: 메인 카메라 화면 x좌표 기준 (구버전 UpdateIndicatorSideAndPosition)
            var cam = UnityEngine.Camera.main;
            bool playerOnRight = true;
            var gm = GameManager.Instance;
            var player = gm != null ? gm.Player : null;
            if (cam != null && player != null)
            {
                float px = cam.WorldToScreenPoint(player.transform.position).x;
                float ex = cam.WorldToScreenPoint(transform.position).x;
                playerOnRight = px >= ex;
            }

            Vector3 pos = transform.position + Vector3.up * height;
            if (cam != null)
            {
                pos += cam.transform.right * (playerOnRight ? sideOffset : -sideOffset);
                _instance.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
            }
            _instance.transform.position = pos;
            _view?.SetSide(playerOnRight);
        }
    }
}
