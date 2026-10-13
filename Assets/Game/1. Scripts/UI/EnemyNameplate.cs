using System.Reflection;
using UnityEngine;
using TMPro;
using MoreMountains.Tools;

namespace Aiara
{
    /// <summary>
    /// 적 머리 위 통합 바(EnemyStatusBar 인스턴스)의 "EnemyName Text"에 한글 표시 이름을 넣는다.
    /// MMHealthBar(Prefab 모드)가 런타임에 바를 생성하므로, MMBattleSpiritBar와 동일하게
    /// Update에서 바인딩될 때까지 매 프레임 시도한다(스폰 시 비활성 초기화 대응).
    /// 풀링 재사용으로 바가 파괴되면 자동으로 재바인딩된다.
    /// </summary>
    [AddComponentMenu("TopDown Engine/GUI/Enemy Nameplate")]
    public class EnemyNameplate : MonoBehaviour
    {
        [Tooltip("바 위에 표시할 이름 (한글 가능). 비워두면 아무 것도 표시하지 않는다.")]
        public string DisplayName;

        protected TMP_Text _nameText;

        protected virtual void Update()
        {
            // 바인딩 완료 & 살아있으면 조기 반환 (파괴되면 Unity null 판정 → 재바인딩)
            if (_nameText != null) { return; }
            if (string.IsNullOrEmpty(DisplayName)) { enabled = false; return; }

            TryBind();
        }

        /// <summary>
        /// 같은 오브젝트의 MMHealthBar(정확한 base 타입)가 생성한 바 인스턴스에서
        /// "EnemyName Text"를 찾아 이름을 넣는다. 아직 생성 전이면 다음 프레임 재시도.
        /// </summary>
        protected virtual void TryBind()
        {
            MMHealthBar healthBar = null;
            foreach (MMHealthBar hb in GetComponents<MMHealthBar>())
            {
                if (hb.GetType() == typeof(MMHealthBar)) { healthBar = hb; break; }
            }
            if (healthBar == null) { enabled = false; return; }

            FieldInfo pbField = typeof(MMHealthBar).GetField("_progressBar",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MMProgressBar pb = pbField != null ? pbField.GetValue(healthBar) as MMProgressBar : null;
            if (pb == null) { return; } // 아직 바 생성 전

            Transform textT = FindDeepChild(pb.transform, "EnemyName Text");
            if (textT == null) { return; }

            _nameText = textT.GetComponent<TMP_Text>();
            if (_nameText != null)
            {
                _nameText.text = DisplayName;
                if (!textT.gameObject.activeSelf) { textT.gameObject.SetActive(true); }
            }
        }

        /// <summary>Transform 계층을 재귀 순회(비활성 포함)하여 이름이 일치하는 첫 Transform 반환.</summary>
        protected static Transform FindDeepChild(Transform parent, string targetName)
        {
            if (parent.name == targetName) { return parent; }
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform r = FindDeepChild(parent.GetChild(i), targetName);
                if (r != null) { return r; }
            }
            return null;
        }
    }
}
