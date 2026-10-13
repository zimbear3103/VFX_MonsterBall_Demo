using System.Collections.Generic;
using UnityEngine;

public class RandomEffect : MonoBehaviour
{
    [SerializeField] private List<GameObject> effects = new List<GameObject>();
    [SerializeField] private int activeCount = 1;

    private void OnEnable()
    {
        Activate();
    }

    private void Activate()
    {
        if (effects.Count == 0) return;

        int count = Mathf.Clamp(activeCount, 0, effects.Count);

        // Fisher-Yates 셔플용 인덱스 리스트
        List<int> indices = new List<int>(effects.Count);
        for (int i = 0; i < effects.Count; i++)
            indices.Add(i);

        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        for (int i = 0; i < indices.Count; i++)
        {
            if (effects[indices[i]] != null)
                effects[indices[i]].SetActive(i < count);
        }
    }
}
