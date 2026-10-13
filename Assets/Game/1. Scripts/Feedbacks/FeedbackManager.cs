using System.Collections.Generic;
using UnityEngine;
using MoreMountains.Feedbacks;

namespace Aiara
{
    /// <summary>
    /// (key, MMF_Player) registry facade. Callers play feedback by key; the backend is being
    /// migrated from MoreMountains MMF_Player to the Feel-independent FeedbackCue engine.
    /// While UseCueEngine is false the original MMF_Player path runs (unchanged). While true, the
    /// same keys route to FeedbackCueRegistry, which plays mirrored FeedbackCue data. Every
    /// Register call also builds/refreshes a parallel FeedbackCue via the runtime bridge, so both
    /// backends stay in sync and the switch is instant and reversible.
    /// </summary>
    public static class FeedbackManager
    {
        // Migration switch: false = legacy MMF_Player playback, true = new FeedbackCue engine.
        public static bool UseCueEngine = false;

        private static readonly Dictionary<string, MMF_Player> _registry = new Dictionary<string, MMF_Player>();

        // key -> original parent (home). Captured at Register time to restore after playback.
        private static readonly Dictionary<string, Transform> _homeParents = new Dictionary<string, Transform>();

        // Resident runner for the deferred return-home coroutine (used during play).
        private static FeedbackManagerRunner _runner;

        // Ensures a clean state on play start even when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _registry.Clear();
            _homeParents.Clear();
            _runner = null;
        }

        public static void Register(string key, MMF_Player player)
        {
            if (string.IsNullOrEmpty(key) || player == null) return;

            if (_registry.TryGetValue(key, out MMF_Player existing) && existing != null && existing != player)
            {
                Debug.LogWarning($"[FeedbackManager] '{key}' 키가 이미 등록되어 있습니다. 덮어씁니다. (이전: {existing.name})", player);
            }
            _registry[key] = player;
            _homeParents[key] = player.transform.parent;

            // Runtime bridge: build/refresh a mirrored FeedbackCue for this key so the new engine
            // can be validated and switched to without hand-rebuilding data. Harmless while unused.
            FeedbackCue cue = player.GetComponent<FeedbackCue>();
            if (cue == null) cue = player.gameObject.AddComponent<FeedbackCue>();
            cue.Steps = FeedbackCueBridge.BuildSteps(player);
            FeedbackCueRegistry.Register(key, cue);
        }

        // 현재 등록된 player가 인자와 일치할 때만 제거 — 다른 인스턴스가 등록을 가로챈 경우 보존
        public static void Unregister(string key, MMF_Player player)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_registry.TryGetValue(key, out MMF_Player current) && (player == null || current == player))
            {
                _registry.Remove(key);
                _homeParents.Remove(key);
            }
            FeedbackCueRegistry.Unregister(key, player != null ? player.GetComponent<FeedbackCue>() : null);
        }

        /// <summary>
        /// 키로 등록된 MMF_Player를 baseTransform의 자식으로 재부모화 후 local TRS로 배치하고 재생.
        /// baseTransform이 null이면 부모를 변경하지 않습니다.
        /// 재생 완료 후에는 홈(등록 시점의 부모)으로 자동 복귀합니다 — 대상 캐릭터가
        /// 죽어 시체가 비활성화/파괴될 때 공유 이펙트가 함께 사라지는 것을 방지.
        /// </summary>
        public static void PlayFeedback(string key, Transform baseTransform, Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[FeedbackManager] PlayFeedback에 빈 key가 전달됨.");
                return;
            }

            if (UseCueEngine)
            {
                FeedbackCueRegistry.PlayLocal(key, baseTransform, localPosition, localEulerAngles, localScale);
                return;
            }

            if (!_registry.TryGetValue(key, out MMF_Player player) || player == null)
            {
                Debug.LogWarning($"[FeedbackManager] '{key}' 키에 해당하는 피드백이 등록되어 있지 않습니다.");
                return;
            }

            Transform t = player.transform;
            if (baseTransform != null)
            {
                t.SetParent(baseTransform, false);
            }
            t.localPosition = localPosition;
            t.localEulerAngles = localEulerAngles;
            t.localScale = localScale;

            player.PlayFeedbacks();

            // [코드 추종] 방금 생성된 이펙트에 VFXFollowTarget이 있으면 baseTransform을 추종 대상으로 주입.
            // 부모화(SetParent) 대신 코드로 위치/회전만 따라가게 해 스케일/반전 꼬임을 피한다.
            if (baseTransform != null)
            {
                AttachFollowers(player, baseTransform);
            }

            // 재생이 끝난 뒤 홈으로 복귀 예약 (재생 중에는 캐릭터를 따라가도록 유지)
            if (baseTransform != null)
            {
                ScheduleReturnHome(key, player);
            }
        }

        /// <summary>
        /// player가 방금 생성한 오브젝트(MMF_InstantiateObject) 중 VFXFollowTarget을 가진 것에
        /// baseTransform을 추종 대상으로 설정한다. 컴포넌트가 없는 이펙트(연기/폭발 등)는 무시 →
        /// 슬래시처럼 추종이 필요한 프리팹만 따라간다. 부모화 없이 코드 추종(스케일/계층 무영향).
        /// </summary>
        private static void AttachFollowers(MMF_Player player, Transform target)
        {
            if (player == null || target == null) return;
            var list = player.FeedbacksList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                MMF_InstantiateObject inst = list[i] as MMF_InstantiateObject;
                if (inst == null) continue;
                GameObject go = inst.InstantiatedGameObject;
                if (go == null) continue;
                VFXFollowTarget follower = go.GetComponent<VFXFollowTarget>();
                if (follower != null)
                {
                    follower.SetTarget(target);
                }
            }
        }

        /// <summary>키로 등록된 MMF_Player 반환(없으면 null) — 재생 전 일시 틴트 등 외부 가공용.</summary>
        public static MoreMountains.Feedbacks.MMF_Player GetPlayer(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _registry.TryGetValue(key, out MoreMountains.Feedbacks.MMF_Player p) ? p : null;
        }

        public static bool IsRegistered(string key)
        {
            if (UseCueEngine) return FeedbackCueRegistry.IsRegistered(key);
            return !string.IsNullOrEmpty(key)
                && _registry.TryGetValue(key, out MMF_Player p)
                && p != null;
        }

        /// <summary>월드 위치/회전 + 스케일 지정 재생 — 발 본 등 '본 위치'에 정확히 붙이는 이펙트용.</summary>
        public static void PlayFeedbackAtWorld(string key, Vector3 worldPosition, Quaternion worldRotation, Vector3 localScale)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (UseCueEngine)
            {
                FeedbackCueRegistry.PlayWorld(key, worldPosition, worldRotation, localScale);
                return;
            }

            if (!_registry.TryGetValue(key, out MMF_Player player) || player == null)
            {
                Debug.LogWarning($"[FeedbackManager] '{key}' 키에 해당하는 피드백이 등록되어 있지 않습니다.");
                return;
            }

            ReviveIfSwallowed(key, player);

            Transform t = player.transform;
            t.position = worldPosition;
            t.rotation = worldRotation;
            t.localScale = localScale;
            if (!player.gameObject.activeInHierarchy)
            {
                Debug.LogWarning($"[FeedbackManager] '{key}' 플레이어가 비활성 상태(부모: " +
                    $"{(t.parent != null ? t.parent.name : "없음")}) — 재생 불가", player);
            }
            player.PlayFeedbacks(worldPosition);
        }

        public static void PlayFeedbackAtWorld(string key, Vector3 worldPosition, Quaternion worldRotation)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (UseCueEngine)
            {
                FeedbackCueRegistry.PlayWorld(key, worldPosition, worldRotation, Vector3.one);
                return;
            }

            if (!_registry.TryGetValue(key, out MMF_Player player) || player == null)
            {
                Debug.LogWarning($"[FeedbackManager] '{key}' 키에 해당하는 피드백이 등록되어 있지 않습니다.");
                return;
            }

            // 이전 재부모화로 죽은/비활성 조상 밑에 삼켜져 있으면 홈으로 되돌려 되살린다
            ReviveIfSwallowed(key, player);

            Transform t = player.transform;
            t.position = worldPosition;
            t.rotation = worldRotation;
            player.PlayFeedbacks(worldPosition);
        }

        /// <summary>
        /// 조상 비활성화로 꺼져 있는(자기 자신은 activeSelf=true) 이펙트를 홈으로 복귀시켜 되살린다.
        /// 홈이 이미 파괴됐으면 최상위로 꺼내기만 한다.
        /// </summary>
        private static void ReviveIfSwallowed(string key, MMF_Player player)
        {
            GameObject go = player.gameObject;
            if (go.activeInHierarchy) return;
            if (!go.activeSelf)
            {
                // 스스로 꺼진 것은 의도된 상태일 수 있으므로 건드리지 않는다
                return;
            }

            _homeParents.TryGetValue(key, out Transform home);
            if (home != null)
            {
                player.transform.SetParent(home, false);
            }
            else
            {
                player.transform.SetParent(null, false);
            }
        }

        /// <summary>
        /// 재생 시간이 지난 뒤 이펙트를 홈 부모로 복귀시키는 예약을 건다.
        /// </summary>
        private static void ScheduleReturnHome(string key, MMF_Player player)
        {
            if (!Application.isPlaying) return;

            if (_runner == null)
            {
                GameObject runnerGo = new GameObject("[FeedbackManager] Runner");
                runnerGo.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(runnerGo);
                _runner = runnerGo.AddComponent<FeedbackManagerRunner>();
            }

            // MMF 총 재생시간 + 여유. 비정상 값(무한/음수)은 상한으로 클램프.
            float duration = player.TotalDuration;
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f) duration = 2f;
            float delay = Mathf.Clamp(duration, 0.1f, 5f) + 1f;

            _runner.ScheduleReturnHome(key, player, delay);
        }

        /// <summary>
        /// 러너 코루틴에서 호출 — 지연 후 홈 복귀 실행.
        /// 그 사이 홈/이펙트가 파괴됐거나 다른 곳으로 재등록됐으면 각각 안전하게 처리한다.
        /// </summary>
        internal static void ExecuteReturnHome(string key, MMF_Player player)
        {
            if (player == null) return;
            if (!_registry.TryGetValue(key, out MMF_Player current) || current != player)
            {
                // 다른 인스턴스가 키를 가로챘으면 이 인스턴스는 건드리지 않는다
                return;
            }

            _homeParents.TryGetValue(key, out Transform home);
            Transform t = player.transform;
            if (home != null)
            {
                if (t.parent != home)
                {
                    t.SetParent(home, true); // 월드 위치 유지 — 잔여 파티클 위치 보존
                }
            }
            else if (t.parent != null)
            {
                t.SetParent(null, true);
            }
        }
    }

    /// <summary>
    /// FeedbackManager의 지연 홈 복귀 코루틴을 실행하는 상주 러너.
    /// 같은 키에 대한 재예약은 기존 예약을 대체한다.
    /// </summary>
    internal class FeedbackManagerRunner : MonoBehaviour
    {
        private readonly Dictionary<string, Coroutine> _pending = new Dictionary<string, Coroutine>();

        public void ScheduleReturnHome(string key, MMF_Player player, float delay)
        {
            if (_pending.TryGetValue(key, out Coroutine existing) && existing != null)
            {
                StopCoroutine(existing);
            }
            _pending[key] = StartCoroutine(ReturnHomeAfter(key, player, delay));
        }

        private System.Collections.IEnumerator ReturnHomeAfter(string key, MMF_Player player, float delay)
        {
            // 히트스톱 등 timeScale 정지 중에도 복귀가 밀리지 않도록 실시간 대기
            yield return new WaitForSecondsRealtime(delay);
            _pending.Remove(key);
            FeedbackManager.ExecuteReturnHome(key, player);
        }
    }
}
