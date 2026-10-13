using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 배틀존 저작 툴 윈도우.
    ///
    /// - 열린 씬의 BattleZone 목록 훑기 / 새 존 생성(트리거 + Walls + SpawnPoints + Wave_1)
    /// - 트리거 박스 크기 조절, 벽 자동 맞춤
    /// - 웨이브 추가·삭제·순서 변경, 웨이브별 스폰 엔트리 편집
    /// - 씬 뷰 클릭으로 몬스터 배치(스폰 포인트 / 선발대 두 가지 모드)
    /// - 세팅 검증 및 원클릭 수정
    /// </summary>
    public class BattleZoneToolWindow : EditorWindow
    {
        private enum PlaceMode
        {
            /// <summary>스폰 포인트를 만들고 spawnEntries에 등록한다(전투 시작 후 생성).</summary>
            SpawnPoint,

            /// <summary>프리팹 인스턴스를 씬에 직접 두고 preplacedEnemies에 등록한다(선발대).</summary>
            Preplaced,
        }

        private const string PrefKeyPrefab = "Yeolha.BattleZoneTool.Prefab";

        [MenuItem("Tools/Yeolha/배틀존 툴")]
        public static void Open()
        {
            BattleZoneToolWindow window = GetWindow<BattleZoneToolWindow>();
            window.titleContent = new GUIContent("배틀존 툴");
            window.minSize = new Vector2(360f, 480f);
            window.Show();
        }

        private BattleZone _zone;
        private Vector2 _scroll;

        // 벽 맞춤
        private BattleZoneAuthoring.BlockAxis _blockAxis = BattleZoneAuthoring.BlockAxis.Z;
        private float _wallWidthPadding = 2f;
        private float _wallHeightPadding = 4f;

        // 배치
        private bool _placing;
        private PlaceMode _placeMode = PlaceMode.SpawnPoint;
        private GameObject _placePrefab;
        private int _placeWave;
        private float _placeDelay;
        private bool _placeStartInactive = true;
        private bool _placeOnFlatPlane;

        // 표시
        private bool _editBounds;
        private readonly BoxBoundsHandle _boxHandle = new BoxBoundsHandle();
        private readonly Dictionary<EnemySpawner, bool> _waveFoldouts = new Dictionary<EnemySpawner, bool>();
        private GameObject[] _enemyPrefabs;
        private string[] _enemyPrefabNames;


        // ─────────── 수명 주기 ───────────

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += Repaint;
            RestorePrefab();
            PickZoneFromSelection();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= Repaint;
            _placing = false;
        }

        private void OnSelectionChange()
        {
            PickZoneFromSelection();
            Repaint();
        }

        private void PickZoneFromSelection()
        {
            if (Selection.activeGameObject == null) return;

            BattleZone found = Selection.activeGameObject.GetComponentInParent<BattleZone>();
            if (found != null) _zone = found;
        }


        // ─────────── 윈도우 GUI ───────────

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawZonePicker();

            if (_zone == null)
            {
                EditorGUILayout.HelpBox("편집할 배틀존을 고르거나 새로 만드세요.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();
            DrawZoneSettings();

            EditorGUILayout.Space();
            DrawWaves();

            EditorGUILayout.Space();
            DrawPlacement();

            EditorGUILayout.Space();
            DrawValidation();

            EditorGUILayout.EndScrollView();
        }

        private void DrawZonePicker()
        {
            EditorGUILayout.LabelField("배틀존", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                BattleZone picked = (BattleZone)EditorGUILayout.ObjectField(_zone, typeof(BattleZone), true);
                if (picked != _zone)
                {
                    _zone = picked;
                    _placing = false;
                }

                if (GUILayout.Button("새 존 생성", GUILayout.Width(90f)))
                {
                    Vector3 position = SceneView.lastActiveSceneView != null
                        ? SceneView.lastActiveSceneView.pivot
                        : Vector3.zero;

                    _zone = BattleZoneAuthoring.CreateZone(position);
                    _placing = false;
                }
            }

            BattleZone[] zones = Object.FindObjectsByType<BattleZone>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (zones.Length == 0) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"씬의 존 {zones.Length}개", GUILayout.Width(90f));
                for (int i = 0; i < zones.Length; i++)
                {
                    bool active = zones[i] == _zone;
                    if (GUILayout.Toggle(active, zones[i].name, EditorStyles.miniButton) && !active)
                    {
                        _zone = zones[i];
                        Selection.activeGameObject = zones[i].gameObject;
                        _placing = false;
                    }
                }
            }
        }

        private void DrawZoneSettings()
        {
            EditorGUILayout.LabelField("존 설정", EditorStyles.boldLabel);

            SerializedObject so = new SerializedObject(_zone);
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty(BattleZoneAuthoring.P_BattleWalls),
                new GUIContent("전투 중 활성화할 벽"));
            EditorGUILayout.PropertyField(so.FindProperty(BattleZoneAuthoring.P_TriggerOnce),
                new GUIContent("1회만 발동"));
            so.ApplyModifiedProperties();

            BoxCollider box = _zone.GetComponent<BoxCollider>();
            if (box != null)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 center = EditorGUILayout.Vector3Field("트리거 Center", box.center);
                Vector3 size = EditorGUILayout.Vector3Field("트리거 Size", box.size);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(box, "Edit Battle Zone Bounds");
                    box.center = center;
                    box.size = Vector3.Max(size, Vector3.one * 0.1f);
                    EditorUtility.SetDirty(box);
                }

                _editBounds = EditorGUILayout.ToggleLeft("씬 뷰에서 박스 핸들로 편집", _editBounds);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("벽 자동 맞춤", EditorStyles.miniBoldLabel);
            _blockAxis = (BattleZoneAuthoring.BlockAxis)EditorGUILayout.EnumPopup(
                new GUIContent("차단 축", "이 축의 양 끝을 벽으로 막는다(존 로컬 기준)."), _blockAxis);
            _wallWidthPadding = EditorGUILayout.FloatField("폭 여유", _wallWidthPadding);
            _wallHeightPadding = EditorGUILayout.FloatField("높이 여유", _wallHeightPadding);

            if (GUILayout.Button("트리거 양 끝에 벽 배치 / 갱신"))
            {
                BattleZoneAuthoring.FitWalls(_zone, _blockAxis, _wallWidthPadding, _wallHeightPadding);
                SceneView.RepaintAll();
            }
        }

        private void DrawWaves()
        {
            EditorGUILayout.LabelField("웨이브", EditorStyles.boldLabel);

            List<EnemySpawner> waves = BattleZoneAuthoring.GetWaves(_zone);

            for (int i = 0; i < waves.Count; i++)
                DrawWave(waves, i);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 웨이브 추가"))
                {
                    BattleZoneAuthoring.AddWave(_zone);
                    Repaint();
                }

                EnemySpawner link = (EnemySpawner)EditorGUILayout.ObjectField(
                    null, typeof(EnemySpawner), true, GUILayout.Width(140f));
                if (link != null) BattleZoneAuthoring.LinkWave(_zone, link);
            }
        }

        private void DrawWave(List<EnemySpawner> waves, int index)
        {
            EnemySpawner wave = waves[index];

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color prev = GUI.color;
                    GUI.color = BattleZoneGizmos.WaveColor(index);
                    GUILayout.Label("■", GUILayout.Width(14f));
                    GUI.color = prev;

                    if (wave == null)
                    {
                        EditorGUILayout.LabelField($"Wave {index + 1}  (비어 있음)");
                    }
                    else
                    {
                        bool open = _waveFoldouts.TryGetValue(wave, out bool v) && v;
                        int entries = BattleZoneAuthoring.GetEntryCount(wave);
                        int pre = BattleZoneAuthoring.GetPreplacedCount(wave);

                        bool next = EditorGUILayout.Foldout(open,
                            $"Wave {index + 1}  ·  {wave.gameObject.name}  ·  스폰 {entries} / 선발대 {pre}", true);
                        _waveFoldouts[wave] = next;
                    }

                    if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
                        BattleZoneAuthoring.MoveWave(_zone, index, -1);
                    if (GUILayout.Button("▼", GUILayout.Width(24f)))
                        BattleZoneAuthoring.MoveWave(_zone, index, 1);
                    if (GUILayout.Button("X", EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                    {
                        bool deleteObject = wave != null && EditorUtility.DisplayDialog(
                            "웨이브 삭제",
                            $"'{wave.gameObject.name}' 오브젝트도 함께 삭제할까요?",
                            "오브젝트까지 삭제", "목록에서만 제거");

                        BattleZoneAuthoring.RemoveWave(_zone, index, deleteObject);
                        GUIUtility.ExitGUI();
                    }
                }

                if (wave == null) return;
                if (!_waveFoldouts.TryGetValue(wave, out bool expanded) || !expanded) return;

                EditorGUI.indentLevel++;
                DrawWaveBody(wave, index);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawWaveBody(EnemySpawner wave, int waveIndex)
        {
            SerializedObject so = new SerializedObject(wave);
            so.Update();

            SerializedProperty entries = so.FindProperty(BattleZoneAuthoring.P_SpawnEntries);

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty e = entries.GetArrayElementAtIndex(i);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(e.FindPropertyRelative(BattleZoneAuthoring.P_EntryPrefab),
                        GUIContent.none);

                    SerializedProperty pointProp = e.FindPropertyRelative(BattleZoneAuthoring.P_EntryPoint);
                    EditorGUILayout.PropertyField(pointProp, GUIContent.none, GUILayout.Width(110f));

                    EditorGUILayout.PropertyField(e.FindPropertyRelative(BattleZoneAuthoring.P_EntryDelay),
                        GUIContent.none, GUILayout.Width(40f));

                    Transform point = pointProp.objectReferenceValue as Transform;
                    using (new EditorGUI.DisabledScope(point == null))
                    {
                        if (GUILayout.Button("보기", EditorStyles.miniButton, GUILayout.Width(36f)) && point != null)
                        {
                            Selection.activeTransform = point;
                            SceneView.lastActiveSceneView?.Frame(
                                new Bounds(point.position, Vector3.one * 4f), false);
                        }
                    }

                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22f)))
                    {
                        so.ApplyModifiedProperties();
                        bool deletePoint = point != null && EditorUtility.DisplayDialog(
                            "엔트리 삭제", $"스폰 포인트 '{point.name}'도 함께 삭제할까요?", "함께 삭제", "엔트리만 삭제");

                        BattleZoneAuthoring.RemoveEntry(wave, i, deletePoint);
                        GUIUtility.ExitGUI();
                    }
                }

                // 스폰 포인트가 없는 엔트리는 플레이어 기준 스폰이므로 방향/거리를 노출한다.
                if (e.FindPropertyRelative(BattleZoneAuthoring.P_EntryPoint).objectReferenceValue == null)
                {
                    EditorGUI.indentLevel++;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(e.FindPropertyRelative(BattleZoneAuthoring.P_EntrySide),
                            new GUIContent("방향"));
                        EditorGUILayout.PropertyField(e.FindPropertyRelative(BattleZoneAuthoring.P_EntryDistance),
                            new GUIContent("거리(0=자동)"));
                    }
                    EditorGUI.indentLevel--;
                }
            }

            so.ApplyModifiedProperties();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 빈 엔트리"))
                    BattleZoneAuthoring.AddEntry(wave, null, null, 0f, SpawnSide.Auto);

                if (GUILayout.Button("이 웨이브에 배치 시작"))
                {
                    _placeWave = waveIndex;
                    _placing = true;
                    SceneView.lastActiveSceneView?.Focus();
                }
            }

            SerializedObject so2 = new SerializedObject(wave);
            so2.Update();
            EditorGUILayout.PropertyField(so2.FindProperty(BattleZoneAuthoring.P_Preplaced),
                new GUIContent("선발대"), true);
            EditorGUILayout.PropertyField(so2.FindProperty(BattleZoneAuthoring.P_WakePreplaced),
                new GUIContent("웨이브 시작 시 선발대 깨우기"));
            EditorGUILayout.PropertyField(so2.FindProperty(BattleZoneAuthoring.P_SpawnOnce),
                new GUIContent("1회만 스폰"));
            so2.ApplyModifiedProperties();
        }

        private void DrawPlacement()
        {
            EditorGUILayout.LabelField("몬스터 배치", EditorStyles.boldLabel);

            List<EnemySpawner> waves = BattleZoneAuthoring.GetWaves(_zone);
            if (waves.Count == 0)
            {
                EditorGUILayout.HelpBox("웨이브를 먼저 하나 추가하세요.", MessageType.Info);
                return;
            }

            string[] waveNames = new string[waves.Count];
            for (int i = 0; i < waves.Count; i++)
                waveNames[i] = $"Wave {i + 1}  ({(waves[i] != null ? waves[i].gameObject.name : "비어 있음")})";

            _placeWave = Mathf.Clamp(_placeWave, 0, waves.Count - 1);
            _placeWave = EditorGUILayout.Popup("대상 웨이브", _placeWave, waveNames);

            EditorGUI.BeginChangeCheck();
            _placePrefab = (GameObject)EditorGUILayout.ObjectField("적 프리팹", _placePrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck()) StorePrefab();

            DrawEnemyPrefabPicker();

            _placeMode = (PlaceMode)EditorGUILayout.EnumPopup(
                new GUIContent("배치 방식",
                    "SpawnPoint: 전투 시작 후 그 자리에 생성.  Preplaced: 지금 씬에 실물을 두는 선발대."),
                _placeMode);

            if (_placeMode == PlaceMode.SpawnPoint)
            {
                _placeDelay = EditorGUILayout.FloatField("스폰 지연(초)", _placeDelay);
            }
            else
            {
                _placeStartInactive = EditorGUILayout.Toggle(
                    new GUIContent("비활성으로 배치", "웨이브가 시작될 때 등장시킨다."), _placeStartInactive);
            }

            _placeOnFlatPlane = EditorGUILayout.Toggle(
                new GUIContent("평면에 배치", "지형 콜라이더 대신 존 높이의 수평면에 찍는다."), _placeOnFlatPlane);

            using (new EditorGUI.DisabledScope(_placePrefab == null || waves[_placeWave] == null))
            {
                GUI.backgroundColor = _placing ? new Color(1f, 0.55f, 0.3f) : Color.white;
                if (GUILayout.Button(_placing ? "배치 모드 끄기 (Esc)" : "배치 모드 켜기", GUILayout.Height(26f)))
                {
                    _placing = !_placing;
                    if (_placing) SceneView.lastActiveSceneView?.Focus();
                }
                GUI.backgroundColor = Color.white;
            }

            if (_placing)
            {
                EditorGUILayout.HelpBox(
                    "씬 뷰를 클릭하면 배치됩니다.  Ctrl: 0.5 단위 스냅  ·  Esc: 종료", MessageType.None);
            }
        }

        private void DrawEnemyPrefabPicker()
        {
            if (_enemyPrefabs == null) RefreshEnemyPrefabs();

            using (new EditorGUILayout.HorizontalScope())
            {
                int current = System.Array.IndexOf(_enemyPrefabs, _placePrefab);
                int picked = EditorGUILayout.Popup("적 목록", current, _enemyPrefabNames);
                if (picked != current && picked >= 0)
                {
                    _placePrefab = _enemyPrefabs[picked];
                    StorePrefab();
                }

                if (GUILayout.Button("새로고침", EditorStyles.miniButton, GUILayout.Width(60f)))
                    RefreshEnemyPrefabs();
            }
        }

        /// <summary>Enemies 폴더에서 Character를 가진 프리팹만 모아 드롭다운을 채운다.</summary>
        private void RefreshEnemyPrefabs()
        {
            const string root = "Assets/Game/Characters/Enemies";
            List<GameObject> found = new List<GameObject>();

            if (AssetDatabase.IsValidFolder(root))
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null && go.GetComponent<Character>() != null) found.Add(go);
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            _enemyPrefabs = found.ToArray();

            _enemyPrefabNames = new string[_enemyPrefabs.Length];
            for (int i = 0; i < _enemyPrefabs.Length; i++)
                _enemyPrefabNames[i] = _enemyPrefabs[i].name;
        }

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("검증", EditorStyles.boldLabel);

            List<BattleZoneValidator.Issue> issues = BattleZoneValidator.Validate(_zone);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("문제 없음.", MessageType.Info);
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                BattleZoneValidator.Issue issue = issues[i];

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(issue.Message, issue.Type);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(issue.Context == null))
                        {
                            if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(50f)))
                                Selection.activeObject = issue.Context;
                        }

                        if (issue.Fix != null && GUILayout.Button(issue.FixLabel, EditorStyles.miniButton))
                        {
                            issue.Fix();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }


        // ─────────── 씬 뷰 ───────────

        private void OnSceneGUI(SceneView view)
        {
            if (_zone == null) return;

            BattleZoneGizmos.DrawZone(_zone, _placing ? _placeWave : -1);
            DrawBoundsHandle();

            if (!_placing) return;

            Event e = Event.current;

            // 배치 중에는 클릭이 오브젝트 선택으로 새지 않도록 기본 컨트롤을 가져온다.
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _placing = false;
                e.Use();
                Repaint();
                return;
            }

            if (!TryGetPlacePoint(e.mousePosition, out Vector3 point)) return;

            DrawPlacePreview(point);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                PlaceAt(point);
                e.Use();
                Repaint();
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();
        }

        private void DrawBoundsHandle()
        {
            if (!_editBounds) return;

            BoxCollider box = _zone.GetComponent<BoxCollider>();
            if (box == null) return;

            using (new Handles.DrawingScope(_zone.transform.localToWorldMatrix))
            {
                _boxHandle.center = box.center;
                _boxHandle.size = box.size;

                EditorGUI.BeginChangeCheck();
                _boxHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(box, "Resize Battle Zone");
                    box.center = _boxHandle.center;
                    box.size = _boxHandle.size;
                    EditorUtility.SetDirty(box);
                    Repaint();
                }
            }
        }

        /// <summary>마우스 위치를 배치 좌표로 변환. 지형 콜라이더 우선, 실패 시 존 높이의 수평면.</summary>
        private bool TryGetPlacePoint(Vector2 mousePosition, out Vector3 point)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            point = Vector3.zero;

            if (!_placeOnFlatPlane && RaycastGeometry(ray, out point))
            {
                Snap(ref point);
                return true;
            }

            Plane plane = new Plane(Vector3.up, _zone.transform.position);
            if (!plane.Raycast(ray, out float distance)) return false;

            point = ray.GetPoint(distance);
            Snap(ref point);
            return true;
        }

        /// <summary>존 자신과 트리거를 제외한 가장 가까운 콜라이더 히트.</summary>
        private bool RaycastGeometry(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;
            RaycastHit[] hits = Physics.RaycastAll(ray, 5000f);
            if (hits.Length == 0) return false;

            float best = float.MaxValue;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider.isTrigger) continue;
                if (collider.transform.IsChildOf(_zone.transform)) continue;
                if (hits[i].distance >= best) continue;

                best = hits[i].distance;
                point = hits[i].point;
                found = true;
            }

            return found;
        }

        private static void Snap(ref Vector3 point)
        {
            if (!Event.current.control) return;

            point.x = Mathf.Round(point.x * 2f) * 0.5f;
            point.z = Mathf.Round(point.z * 2f) * 0.5f;
        }

        private void DrawPlacePreview(Vector3 point)
        {
            Handles.color = BattleZoneGizmos.WaveColor(_placeWave);
            float size = HandleUtility.GetHandleSize(point) * 0.25f;

            Handles.DrawWireDisc(point, Vector3.up, size * 2f);
            Handles.DrawLine(point, point + Vector3.up * size * 6f);

            string label = _placePrefab != null ? _placePrefab.name : "(프리팹 없음)";
            string mode = _placeMode == PlaceMode.SpawnPoint ? "스폰 포인트" : "선발대";
            Handles.Label(point + Vector3.up * size * 7f, $"W{_placeWave + 1}  {label}  [{mode}]");
        }

        private void PlaceAt(Vector3 point)
        {
            List<EnemySpawner> waves = BattleZoneAuthoring.GetWaves(_zone);
            if (_placeWave < 0 || _placeWave >= waves.Count) return;

            EnemySpawner wave = waves[_placeWave];
            if (wave == null || _placePrefab == null) return;

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Place Enemy");

            if (_placeMode == PlaceMode.SpawnPoint)
            {
                Transform spawnPoint = BattleZoneAuthoring.CreateSpawnPoint(_zone, point, _placePrefab.name);
                // 스폰 포인트를 지정하면 spawnSide는 쓰이지 않으므로 기본값으로 둔다.
                BattleZoneAuthoring.AddEntry(wave, _placePrefab, spawnPoint, _placeDelay, SpawnSide.Auto);
            }
            else
            {
                BattleZoneAuthoring.PlacePreplaced(_zone, wave, _placePrefab, point, _placeStartInactive);
            }

            Undo.CollapseUndoOperations(group);
        }


        // ─────────── 프리팹 선택 유지 ───────────

        private void StorePrefab()
        {
            string path = _placePrefab != null ? AssetDatabase.GetAssetPath(_placePrefab) : string.Empty;
            EditorPrefs.SetString(PrefKeyPrefab, AssetDatabase.AssetPathToGUID(path));
        }

        private void RestorePrefab()
        {
            string guid = EditorPrefs.GetString(PrefKeyPrefab, string.Empty);
            if (string.IsNullOrEmpty(guid)) return;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
                _placePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
    }
}
