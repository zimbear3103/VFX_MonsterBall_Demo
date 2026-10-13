using System;
using System.Collections.Generic;
using UnityEngine;
using MoreMountains.Feedbacks;

namespace Aiara
{
    /// <summary>
    /// 인스펙터에 등록한 (key, MMF_Player) 쌍을 활성화 시점에 FeedbackManager에 등록합니다.
    /// 비활성화/파괴 시 자동으로 해제하여 dangling 참조를 방지합니다.
    /// </summary>
    public class FeedbackRegistrar : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("FeedbackManager에서 호출할 키")]
            public string Key;

            [Tooltip("이 키로 호출했을 때 재생할 MMF_Player")]
            public MMF_Player Player;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        private void OnEnable()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e == null) continue;
                FeedbackManager.Register(e.Key, e.Player);
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e == null) continue;
                FeedbackManager.Unregister(e.Key, e.Player);
            }
        }
    }
}
