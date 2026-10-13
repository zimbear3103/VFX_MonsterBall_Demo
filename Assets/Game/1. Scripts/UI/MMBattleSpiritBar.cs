using System.Collections;
using System.Reflection;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using MoreMountains.Tools;

namespace Aiara
{
    /// <summary>
    /// 몬스터(Mob/Boss) 머리 위에 표시되는 투혼(BattleSpirit) 바.
    /// 원본 MMHealthBar 의 Drawn 인프라(Canvas/MMProgressBar/MMFollowTarget)를 그대로 재사용하되,
    /// foreground 위에 "리셋 마커선"과 "현재 수치 TMP 텍스트"를 자식으로 추가한다.
    /// 원본 MMHealthBar 는 수정하지 않으며 상속으로만 확장한다.
    ///
    /// 갱신은 BattleSpiritResource(ability)가 매 프레임 UpdateBar(...)로 push 한다.
    /// 값/마커 비율이 바뀐 프레임에만 텍스트·마커를 갱신하여 정지 시 GC 0 에 수렴한다.
    /// </summary>
    [AddComponentMenu("TopDown Engine/GUI/MM Battle Spirit Bar")]
    public class MMBattleSpiritBar : MMHealthBar
    {
        [Header("Battle Spirit - Marker")]
        [Tooltip("리셋 예정 위치를 나타내는 세로 마커선의 크기 (월드 유닛)")]
        public Vector2 MarkerSize = new Vector2(0.02f, 0.2f);

        [Tooltip("리셋 마커선 색상")]
        public Color MarkerColor = Color.white;

        [Header("Battle Spirit - Value Text")]
        [Tooltip("현재 투혼 수치 텍스트를 표시할지 여부")]
        public bool ShowValueText = true;

        [Tooltip("현재 수치 표시 포맷 (투혼은 정수이므로 \"0\")")]
        public string ValueFormat = "0";

        [Tooltip("수치 텍스트 폰트 크기 (월드스페이스 기준이라 작게)")]
        public float ValueTextFontSize = 1f;

        [Tooltip("수치 텍스트 오프셋 (바 중심 기준, 월드 유닛)")]
        public Vector2 ValueTextOffset = new Vector2(0f, 0.2f);

        [Header("Attach Mode (통합 바 런타임 공유)")]
        [Tooltip("true면 이 컴포넌트는 자체 바를 생성하지 않고, 같은 오브젝트의 체력바(MMHealthBar)가 " +
                 "런타임에 생성한 통합 바 인스턴스 안의 'BattleSpiritBar' 서브바를 찾아 구동한다. " +
                 "체력바 프리팹(EnemyStatusBar)이 투혼바를 자식으로 포함하는 구조 전용. " +
                 "이 모드에선 HealthBarType/HealthBarPrefab을 자체 생성에 쓰지 않는다(Existing + TargetProgressBar 비움 권장).")]
        public bool AttachToHealthBar = false;

        // 마커가 따르는 트랙: foreground 와 동일 좌표계로 만든 RectTransform.
        // 마커는 이 트랙의 자식으로 들어가 anchorMin/Max.x = ratio 로 비율 정렬된다.
        protected RectTransform _markerRect;
        protected TMP_Text _valueText;

        // GC 회피: 값/마커 비율이 실제로 바뀐 프레임에만 텍스트/마커를 갱신.
        protected float _lastValue = float.NaN;        // base.UpdateBar show 판정용 (텍스트 표시 여부와 무관)
        protected float _lastDisplayedValue = float.NaN; // 텍스트 ToString 캐시용
        protected float _lastMarkerRatio = float.NaN;

        /// <summary>
        /// base 의 Drawn 바 생성 후, foreground 위에 마커 트랙/마커와 수치 텍스트를 추가한다.
        /// </summary>
        protected override void DrawHealthBar()
        {
            base.DrawHealthBar();

            // Drawn 모드가 아니거나 base 가 foreground 를 못 만들었으면(=Image 계층 없음) 확장 대상 없음.
            if (HealthBarType != HealthBarTypes.Drawn || _foregroundImage == null)
            {
                return;
            }

            // 원본 MMHealthBar 가 최상위 오브젝트 이름을 "HealthBar|..." 로 고정하므로 투혼 바에 맞게 교체.
            if (_progressBar != null)
            {
                _progressBar.gameObject.name = "BattleSpiritBar|" + this.gameObject.name;
            }

            // base 가 만든 "MMProgressBarContainer" (foreground 의 부모).
            Transform container = _foregroundImage.transform.parent;

            BuildMarker(container);
            if (ShowValueText)
            {
                BuildValueText(container);
            }
        }

        /// <summary>
        /// Prefab / Existing 모드 지원: base가 _progressBar를 채운다(Prefab=인스턴스화, Existing=TargetProgressBar).
        /// 마커선/숫자 텍스트는 그 바 하위에 이미 자식으로 포함돼 있으므로(생성 시 Drawn 출력을 그대로 저장),
        /// 코드로 다시 만들지 않고 이름으로 찾아 참조만 연결한다.
        /// (Drawn 모드는 기존대로 DrawHealthBar에서 생성되므로 여기서 건드리지 않는다.)
        /// </summary>
        // AttachToHealthBar: 통합 바를 적 하위로 재부모화했는지(1회) + 투혼 서브바 바인딩 완료 여부
        protected bool _statusBarReparented;

        public override void Initialization()
        {
            base.Initialization();

            // AttachToHealthBar: 자체 바를 만들지 않는다. 재부모화/바인딩은 Update()에서 처리.
            // (코루틴 대신 Update를 쓰는 이유: 스폰 시스템이 적을 '비활성 상태로 초기화'하는 경우
            //  StartCoroutine이 실패해 영영 안 붙는다. Update는 활성화 이후 매 프레임 돌아 확실하다.)
            if (AttachToHealthBar) return;

            if (HealthBarType != HealthBarTypes.Drawn && _progressBar != null)
            {
                BindPrefabModeChildren();
            }
        }

        /// <summary>
        /// base(MMHealthBar.Update: 바 표시/러프)에 더해, AttachToHealthBar면 재부모화+투혼 바인딩을
        /// 완료될 때까지 매 프레임 시도한다. 완료되면(또는 바가 파괴돼 재부착이 필요하면) 자동 재개.
        /// </summary>
        protected override void Update()
        {
            base.Update();

            if (!AttachToHealthBar) return;

            // 풀링 재사용 등으로 바인딩한 바가 파괴됐으면 재부착을 위해 리셋
            if (_progressBar == null) _statusBarReparented = false;

            if (_statusBarReparented && _progressBar != null) return; // 완료됨

            TryAttachToSpawnedBar();
        }

        /// <summary>
        /// AttachToHealthBar 모드 핵심(멱등):
        /// 1) 같은 오브젝트의 base 체력바(MMHealthBar)가 런타임 생성한 통합 바(_progressBar, 리플렉션)를 찾는다.
        /// 2) 그 통합 바를 적(this.transform) 하위로 재부모화한다 — MM Prefab 모드는 바를 씬 루트에 두어
        ///    따라다니지 않으므로. 단, MMBillboard가 Start에서 만드는 컨테이너(_parentContainer)를 대상으로
        ///    하고, 컨테이너 생성을 기다린다(먼저 옮기면 빌보드가 다시 씬 루트로 nest하는 레이스가 있음).
        ///    컨테이너를 파괴하지 않고 통째로 옮겨 계층 추적 + 적과 동반 파괴 + 빌보드 회전 유지.
        /// 3) 통합 바 안의 'BattleSpiritBar' 서브바를 _progressBar로 연결(비활성이면 활성화) + 마커/숫자 바인딩.
        /// </summary>
        protected virtual void TryAttachToSpawnedBar()
        {
            // 1) 형제 base 체력바 찾기 (투혼바 서브클래스 제외)
            MMHealthBar healthBar = null;
            foreach (MMHealthBar hb in GetComponents<MMHealthBar>())
            {
                if (hb.GetType() == typeof(MMHealthBar)) { healthBar = hb; break; }
            }
            if (healthBar == null) return;

            FieldInfo pbField = typeof(MMHealthBar).GetField("_progressBar",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MMProgressBar healthPB = pbField != null ? pbField.GetValue(healthBar) as MMProgressBar : null;
            if (healthPB == null) return; // 아직 생성 전
            Transform barRoot = healthPB.transform;

            // 2) 재부모화 대상 = MMBillboard의 컨테이너(_parentContainer). 생성될 때까지 대기.
            Transform container = barRoot;
            MMBillboard bb = barRoot.GetComponent<MMBillboard>();
            if (bb != null && bb.NestObject)
            {
                FieldInfo pcField = typeof(MMBillboard).GetField("_parentContainer",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                GameObject pc = pcField != null ? pcField.GetValue(bb) as GameObject : null;
                if (pc == null) return; // 빌보드가 아직 컨테이너를 만들지 않음 — 다음 프레임
                container = pc.transform;
            }
            else if (barRoot.parent != null)
            {
                container = barRoot.parent;
            }

            if (container != transform && !container.IsChildOf(transform))
            {
                container.SetParent(transform, true); // 월드 트랜스폼 유지하며 적 하위로
            }
            _statusBarReparented = (container == transform) || container.IsChildOf(transform);

            // 3) 투혼 서브바 바인딩
            if (_progressBar == null)
            {
                Transform spiritT = FindDeepChild(barRoot, "BattleSpiritBar");
                if (spiritT == null) return;
                MMProgressBar spb = spiritT.GetComponent<MMProgressBar>();
                if (spb == null) return;
                _progressBar = spb;
                if (!spiritT.gameObject.activeSelf) spiritT.gameObject.SetActive(true);
                BindPrefabModeChildren();
            }
        }

        /// <summary>
        /// 인스턴스화(또는 Existing으로 지정)된 바 하위에서 마커선("BattleSpirit Marker")과
        /// 숫자 텍스트("BattleSpirit Value Text")를 이름으로 찾아 _markerRect / _valueText에 연결한다.
        /// ShowValueText가 꺼져 있으면 텍스트를 숨긴다.
        /// </summary>
        protected virtual void BindPrefabModeChildren()
        {
            Transform root = _progressBar.transform;

            Transform marker = FindDeepChild(root, "BattleSpirit Marker");
            _markerRect = (marker != null) ? marker.GetComponent<RectTransform>() : null;

            Transform text = FindDeepChild(root, "BattleSpirit Value Text");
            if (text != null)
            {
                text.gameObject.SetActive(ShowValueText);
                _valueText = ShowValueText ? text.GetComponent<TMP_Text>() : null;
            }
        }

        /// <summary>Transform 계층을 재귀 순회(비활성 포함)하여 이름이 일치하는 첫 Transform 반환.</summary>
        protected static Transform FindDeepChild(Transform parent, string targetName)
        {
            if (parent.name == targetName) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform r = FindDeepChild(parent.GetChild(i), targetName);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>
        /// foreground 와 동일 좌표계(pivot=zero, sizeDelta=Size-BackgroundPadding*2, anchoredPosition=-size/2)의
        /// MarkerTrack 을 만들고, 그 자식으로 세로 마커선을 추가한다.
        /// 트랙이 fill 0~1 을 폭 전체에 매핑하므로 마커의 anchorMin/Max.x = ratio 가 정확히 채움 위치와 정렬된다.
        /// 마커는 형제 중 마지막(맨 위)에 배치해 foreground 에 가려지지 않게 한다.
        /// </summary>
        protected virtual void BuildMarker(Transform container)
        {
            Vector2 trackSize = Size - BackgroundPadding * 2f;

            // 트랙 (보이지 않는 정렬 기준 컨테이너)
            GameObject trackGO = new GameObject("BattleSpirit Marker Track");
            trackGO.transform.SetParent(container, false);
            trackGO.transform.localScale = Vector3.one;
            RectTransform trackRect = trackGO.AddComponent<RectTransform>();
            trackRect.pivot = Vector2.zero;
            trackRect.sizeDelta = trackSize;
            trackRect.anchoredPosition = -trackSize / 2f;
            trackRect.SetAsLastSibling();

            // 마커선 (트랙 자식)
            GameObject markerGO = new GameObject("BattleSpirit Marker");
            markerGO.transform.SetParent(trackRect, false);
            markerGO.transform.localScale = Vector3.one;
            Image markerImage = markerGO.AddComponent<Image>();
            markerImage.color = MarkerColor;
            markerImage.raycastTarget = false;

            _markerRect = markerImage.rectTransform;
            // y 방향은 트랙 높이 전체를 채우고, x 는 anchor 비율로 위치를 잡는다.
            _markerRect.pivot = new Vector2(0.5f, 0f);
            _markerRect.anchorMin = new Vector2(0f, 0f);
            _markerRect.anchorMax = new Vector2(0f, 1f);
            _markerRect.sizeDelta = new Vector2(MarkerSize.x, 0f);
            _markerRect.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// 현재 수치 TMP 텍스트를 바 위(오프셋)에 추가한다.
        /// </summary>
        protected virtual void BuildValueText(Transform container)
        {
            GameObject textGO = new GameObject("BattleSpirit Value Text");
            textGO.transform.SetParent(container, false);
            textGO.transform.localScale = Vector3.one;

            _valueText = textGO.AddComponent<TextMeshProUGUI>();
            _valueText.fontSize = ValueTextFontSize;
            _valueText.alignment = TextAlignmentOptions.Center;
            _valueText.raycastTarget = false;
            _valueText.enableWordWrapping = false;
            _valueText.overflowMode = TextOverflowModes.Overflow;

            RectTransform textRect = _valueText.rectTransform;
            // 바 중심(container 원점) 기준 오프셋. 폭은 넉넉히 잡아 짧은 숫자가 잘리지 않게.
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(Mathf.Max(Size.x, 1f), Mathf.Max(Size.y, 0.5f));
            textRect.anchoredPosition = ValueTextOffset;
        }

        /// <summary>
        /// BattleSpiritResource 가 매 프레임 호출한다.
        /// 바 채움(base lerp/delayed)과 마커/텍스트를 한 번에 갱신한다.
        /// </summary>
        /// <param name="current">현재 투혼 절대 수치</param>
        /// <param name="min">최소(0)</param>
        /// <param name="max">최대 투혼</param>
        /// <param name="ratio">0~1 채움 비율 (현재/최대)</param>
        /// <param name="markerRatio">리셋 마커 비율 0~1</param>
        public virtual void UpdateBar(float current, float min, float max, float ratio, float markerRatio)
        {
            // AttachToHealthBar: 체력바가 통합 바를 생성했으면 첫 호출에 지연 바인딩. 아직이면 이번 프레임 스킵.
            if (AttachToHealthBar && _progressBar == null)
            {
                TryAttachToSpawnedBar();
                if (_progressBar == null) return;
            }

            // 값이 바뀐 프레임에만 show=true 를 넘긴다 (항상 표시 모드면 show 와 무관하게 표시 유지).
            bool changed = !Mathf.Approximately(current, _lastValue);
            _lastValue = current;

            base.UpdateBar(current, min, max, changed);

            UpdateMarker(markerRatio);
            if (ShowValueText)
            {
                UpdateValueText(current);
            }
        }

        /// <summary>
        /// 마커 비율은 런타임에 거의 안 바뀌므로 대부분 early-return.
        /// </summary>
        protected virtual void UpdateMarker(float markerRatio)
        {
            if (_markerRect == null) { return; }
            markerRatio = Mathf.Clamp01(markerRatio);
            if (Mathf.Approximately(markerRatio, _lastMarkerRatio)) { return; }
            _lastMarkerRatio = markerRatio;

            // anchorMin.x = anchorMax.x = ratio: 트랙 폭에 무관하게 비율 위치 정렬, 리스케일 자동 대응.
            Vector2 aMin = _markerRect.anchorMin; aMin.x = markerRatio; _markerRect.anchorMin = aMin;
            Vector2 aMax = _markerRect.anchorMax; aMax.x = markerRatio; _markerRect.anchorMax = aMax;
            Vector2 pos = _markerRect.anchoredPosition; pos.x = 0f; _markerRect.anchoredPosition = pos;
        }

        /// <summary>
        /// 값이 바뀐 프레임에만 ToString → 정지 상태에서 GC 0 에 수렴.
        /// </summary>
        protected virtual void UpdateValueText(float current)
        {
            if (_valueText == null) { return; }
            if (Mathf.Approximately(current, _lastDisplayedValue)) { return; }
            _lastDisplayedValue = current;
            _valueText.text = current.ToString(ValueFormat); // "0" 포맷이 정수로 반올림 표시
        }
    }
}
