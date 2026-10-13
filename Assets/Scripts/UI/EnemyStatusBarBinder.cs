using UnityEngine;
using TMPro;
using MoreMountains.Tools;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Per-enemy driver for the migrated legacy EnemyStatusBar art prefab.
    /// Instantiates the prefab, places it on the bar layer (rendered post-processing-free by
    /// WorldBarRTRig), follows the enemy head with camera billboarding, and drives HP (plus the
    /// mob touhon sub-bar when the enemy uses touhon) from Character events. The enemy name is
    /// auto-sourced from CharacterAbility.DisplayName. Replaces the old code-built WorldStatusBar.
    /// </summary>
    public class EnemyStatusBarBinder : MonoBehaviour
    {
        private GameObject _prefab;
        private float _heightOffset = 2.3f;
        private string _layerName = "Bars";

        private Character _character;
        private Transform _root;
        private MMProgressBar _hpBar;
        private MMProgressBar _touhonBar;
        private GameObject _touhonBarGo;
        private TMP_Text _valueText;
        private Camera _camera;
        // 시각(모델) 앵커 — 잡기/다운처럼 본 변위로 모델이 루트에서 떨어질 때 바가 몸을 따라가게 한다.
        // XZ만 앵커를 쓰고 높이는 루트 기준 유지(눕는 모션에 바가 오르내리지 않게).
        private Transform _visualAnchor;

        /// <summary>생성된 상태바 루트(생성 전이면 null). CharacterCameraFade 등 시각 시스템 참고용.</summary>
        public Transform BarRoot => _root;

        public void Init(GameObject prefab, float heightOffset, string layerName)
        {
            _prefab = prefab;
            _heightOffset = heightOffset;
            _layerName = layerName;
        }

        private void Start()
        {
            _character = GetComponent<Character>();
            if (_character == null || _prefab == null) { enabled = false; return; }
            ResolveVisualAnchor();

            var inst = Instantiate(_prefab);
            inst.name = "EnemyStatusBar (" + gameObject.name + ")";
            _root = inst.transform;
            SetLayerRecursive(inst, LayerMask.NameToLayer(_layerName));

            // We control facing ourselves; disable the prefab's own billboard to avoid double control.
            var bb = inst.GetComponentInChildren<MMBillboard>(true);
            if (bb != null) bb.enabled = false;

            // HP bar = root MMProgressBar; touhon sub-bar = child "BattleSpiritBar".
            _hpBar = inst.GetComponent<MMProgressBar>();
            var subT = FindDeep(inst.transform, "BattleSpiritBar");
            if (subT != null)
            {
                _touhonBarGo = subT.gameObject;
                _touhonBar = subT.GetComponent<MMProgressBar>();
                var vt = FindDeep(subT, "BattleSpirit Value Text");
                if (vt != null) _valueText = vt.GetComponent<TMP_Text>();
            }

            // Enemy name from character data.
            var nameT = FindDeep(inst.transform, "EnemyName Text");
            if (nameT != null)
            {
                var tmp = nameT.GetComponent<TMP_Text>();
                if (tmp != null)
                    tmp.text = _character.Ability != null ? _character.Ability.DisplayName : gameObject.name;
            }

            bool useTouhon = _character.MaxTouhon > 0f;
            if (_touhonBarGo != null) _touhonBarGo.SetActive(useTouhon);

            _character.OnHealthChanged += OnHP;
            _character.OnTouhonChanged += OnTouhon;
            OnHP(_character.CurrentHP, _character.MaxHP);
            if (useTouhon) OnTouhon(_character.CurrentTouhon, _character.MaxTouhon);
        }

        private void OnDestroy()
        {
            if (_character != null)
            {
                _character.OnHealthChanged -= OnHP;
                _character.OnTouhonChanged -= OnTouhon;
            }
            if (_root != null) Destroy(_root.gameObject);
        }

        private void OnHP(float cur, float max)
        {
            if (_hpBar != null) _hpBar.UpdateBar(cur, 0f, max);
            if (_root != null) _root.gameObject.SetActive(cur > 0f); // hide on death (legacy HideBarAtZero)
        }

        private void OnTouhon(float cur, float max)
        {
            if (_touhonBar != null) _touhonBar.UpdateBar(cur, 0f, max);
            if (_valueText != null) _valueText.text = Mathf.RoundToInt(cur).ToString();
        }

        /// <summary>힙 본 → 스킨 루트본 순으로 시각 앵커 탐색. 없으면 루트만 추종.</summary>
        private void ResolveVisualAnchor()
        {
            var animator = _character.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
                _visualAnchor = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (_visualAnchor == null)
            {
                var smr = _character.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) _visualAnchor = smr.rootBone != null ? smr.rootBone : smr.transform;
            }
        }

        private void LateUpdate()
        {
            if (_root == null || _character == null) return;
            Vector3 basePos = _character.transform.position;
            if (_visualAnchor != null)
            {
                basePos.x = _visualAnchor.position.x;
                basePos.z = _visualAnchor.position.z;
            }
            _root.position = basePos + Vector3.up * _heightOffset;
            if (_camera == null) _camera = Camera.main;
            if (_camera != null)
                // Face the canvas front (+Z) TOWARD the camera. Using +forward points +Z away from
                // the camera → we'd see the back and text/name renders mirrored (matches MMBillboard's
                // OffsetDirection = Vector3.back).
                _root.rotation = Quaternion.LookRotation(-_camera.transform.forward, _camera.transform.up);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0) return;
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        private static Transform FindDeep(Transform parent, string targetName)
        {
            if (parent.name == targetName) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var r = FindDeep(parent.GetChild(i), targetName);
                if (r != null) return r;
            }
            return null;
        }
    }
}
