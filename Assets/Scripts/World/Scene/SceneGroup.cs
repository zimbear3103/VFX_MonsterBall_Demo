using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Map / Light / Spline / Sub 분할 씬을 하나의 스테이지 그룹으로 묶는 데이터.
    /// 구버전(yeolhadiary)의 Aiara.SceneSystem.SceneGroup(Addressables 기반)에서
    /// 개념만 승계해 씬 이름(Build Settings 등록) 기반으로 재작성한 것.
    ///
    /// LightScene이 Active Scene이 되며, 해당 씬의 RenderSettings
    /// (Skybox / Fog / Ambient / Reflection)가 그대로 적용된다.
    /// </summary>
    [CreateAssetMenu(fileName = "SceneGroup", menuName = "Yeolha/Scene System/Scene Group")]
    public class SceneGroup : ScriptableObject
    {
        [Tooltip("그룹 식별자. 비워두면 에셋 이름을 사용.")]
        [SerializeField] private string groupId;

        [Header("Scenes")]
        [Tooltip("맵/지형/콜라이더 씬")]
        [SerializeField] private string mapSceneName;

        [Tooltip("라이팅/Volume/Skybox 씬. 그룹 로드 후 Active Scene이 되어 RenderSettings가 적용된다.")]
        [SerializeField] private string lightSceneName;

        [Tooltip("이동/카메라 기준 스플라인 씬 (StageSplineRoot 포함)")]
        [SerializeField] private string splineSceneName;

        [Tooltip("스테이지 전용 배치 씬. 없는 시퀀스는 비워두면 로드 시 자동 skip.")]
        [SerializeField] private string subSceneName;

        [Header("Entry")]
        [Tooltip("EntryKey를 지정하지 않고 로드했을 때 사용할 기본 진입점")]
        [SerializeField] private string defaultEntryKey = "Start";

        public string GroupId => string.IsNullOrEmpty(groupId) ? name : groupId;
        public string MapSceneName => mapSceneName;
        public string LightSceneName => lightSceneName;
        public string SplineSceneName => splineSceneName;
        public string SubSceneName => subSceneName;
        public string DefaultEntryKey => defaultEntryKey;

        /// <summary>유효한(비어있지 않은) 씬 이름들을 로드 순서대로 반환한다. (Map → Light → Spline → Sub)</summary>
        public IEnumerable<string> EnumerateSceneNames()
        {
            if (!string.IsNullOrEmpty(mapSceneName)) yield return mapSceneName;
            if (!string.IsNullOrEmpty(lightSceneName)) yield return lightSceneName;
            if (!string.IsNullOrEmpty(splineSceneName)) yield return splineSceneName;
            if (!string.IsNullOrEmpty(subSceneName)) yield return subSceneName;
        }
    }
}
