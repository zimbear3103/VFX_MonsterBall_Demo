using System;
using System.Collections.Generic;
using UnityEngine;

public class SpriteEffect : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer = null;
    [SerializeField] private bool billboard = false;
    [SerializeField] private bool loop = false;
    [SerializeField] private bool autoPlay = true;
    [SerializeField] private List<Sprite> sprites;
    [SerializeField] private float time = 1f;
    [SerializeField] private float delay = 0f;

    private int currentSprite = 0;
    private float value = 0f;
    private float delayTime = 0f;

    private float speed = 1f;
    private bool isPlaying = false;
    private bool isInitialized = false;

    void Start()
    {
        Initialize();
    }

    private void OnEnable()
    {
        // 풀에서 다시 가져올 때 초기화
        if (isInitialized)
        {
            Reset();
        }
    }

    private void Initialize()
    {
        if (isInitialized)
            return;

        if (spriteRenderer == null)
        {
            enabled = false;
            return;
        }
        if (sprites.Count == 0)
        {
            enabled = false;
            return;
        }

        speed = (float)sprites.Count / time;
        isInitialized = true;

        if (autoPlay)
        {
            Play();
        }
    }

    void Update()
    {
        if (!isPlaying)
        {
            return;
        }
        if (delayTime < delay)
        {
            delayTime += Time.deltaTime;
            return;
        }
        spriteRenderer.enabled = true;
        value += (Time.deltaTime * speed);
        if (value < 1f)
        {
            return;
        }
        value -= 1f;
        ++currentSprite;

        if (currentSprite >= sprites.Count)
        {
            isPlaying = false;
            spriteRenderer.sprite = null;
            if (loop)
            {
                Play();
            }
            return;
        }

        spriteRenderer.sprite = sprites[currentSprite];

        if (billboard)
        {
            // 부모 체인의 스케일 반전 감지 (rotation 대입 전에 확인)
            bool mirrored = IsParentChainMirrored();

            if (mirrored)
            {
                // 반전된 부모 아래에서 빌보드를 적용하면 반전이 상쇄되므로
                // Y축 180도 회전으로 다시 뒤집어서 반전 유지
                transform.rotation = Camera.main.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            }
            else
            {
                transform.rotation = Camera.main.transform.rotation;
            }
        }
    }

    /// <summary>
    /// 부모 체인에 홀수 개의 음수 스케일 축이 있는지 확인 (거울 반전 여부)
    /// </summary>
    private bool IsParentChainMirrored()
    {
        int negCount = 0;
        Transform t = transform.parent;
        while (t != null)
        {
            Vector3 s = t.localScale;
            if (s.x < 0f) negCount++;
            if (s.y < 0f) negCount++;
            if (s.z < 0f) negCount++;
            t = t.parent;
        }
        return (negCount % 2) == 1;
    }

    public void Play()
    {
        if (isPlaying)
            return;

        value = 0f;
        delayTime = 0f;
        currentSprite = 0;
        spriteRenderer.sprite = sprites[currentSprite];
        if (delay > 0f)
        {
            spriteRenderer.enabled = false;
        }
        isPlaying = true;
    }

    /// <summary>
    /// 스프라이트 이펙트 초기화 (오브젝트 풀링용)
    /// </summary>
    public void Reset()
    {
        // 재생 중지
        isPlaying = false;

        // 모든 변수 초기화
        value = 0f;
        delayTime = 0f;
        currentSprite = 0;

        // 스프라이트 렌더러 초기화
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = null;
            spriteRenderer.enabled = true;
        }

        // autoPlay가 활성화되어 있으면 자동 재생
        if (autoPlay && sprites.Count > 0)
        {
            Play();
        }
    }
}

