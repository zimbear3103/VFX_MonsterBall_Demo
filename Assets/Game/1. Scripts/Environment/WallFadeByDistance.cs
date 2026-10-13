using UnityEngine;

/// <summary>
/// 플레이어가 벽의 로컬 축(좌우) 기준으로 가까워질수록 벽 머티리얼의 틴트 컬러를
/// 검은색(멀 때) -> 하얀색(가까울 때)으로 부드럽게 보간한다.
///
/// 거리는 (플레이어 - 벽) 벡터를 벽의 로컬 축에 투영한 값의 절대값으로 계산한다.
/// 따라서 맵 전체가 Y축으로 회전돼 있어도, 벽이 맵과 함께 회전했다면
/// 항상 "벽 기준 좌우 거리"가 정확히 나온다.
///
/// SineVFX 등 Additive 계열 이펙트 셰이더(_TintColor)에서, 검정=투명처럼,
/// 하양=밝게(불투명처럼) 보이는 효과를 낸다.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class WallFadeByDistance : MonoBehaviour
{
    public enum LocalAxis { Right, Forward, Up }

    [Header("Target")]
    [Tooltip("기준이 되는 플레이어. 비워두면 Tag로 자동 탐색한다.")]
    public Transform Player;
    [Tooltip("Player가 비어있을 때 찾을 태그")]
    public string PlayerTag = "Player";

    [Header("Distance Axis")]
    [Tooltip("거리를 잴 벽의 로컬 축. 보통 Right(로컬 X)가 좌우 방향이다. 벽 메시 방향에 따라 Forward로 바꿀 수도 있다.")]
    public LocalAxis Axis = LocalAxis.Right;

    [Header("Color Property")]
    [Tooltip("색을 적용할 셰이더 프로퍼티 이름. 비워두면 _TintColor > _BaseColor > _Color 순으로 자동 탐색.")]
    public string ColorPropertyName = "";

    [Header("Distance Range (벽 로컬축 기준)")]
    [Tooltip("축 거리가 이 값보다 가까우면 NearColor(기본 하양)")]
    public float NearDistance = 3f;
    [Tooltip("축 거리가 이 값보다 멀면 FarColor(기본 검정)")]
    public float FarDistance = 10f;

    [Header("Colors")]
    [Tooltip("멀 때(투명처럼 보이는) 색")]
    public Color FarColor = Color.black;
    [Tooltip("가까울 때(불투명처럼 보이는) 색")]
    public Color NearColor = Color.white;

    [Header("Smoothing")]
    [Tooltip("색이 목표값으로 따라가는 속도. 0이면 즉시 적용")]
    public float FadeSpeed = 8f;

    // 우선순위 후보 (Inspector에서 ColorPropertyName 지정 시 그게 최우선)
    static readonly string[] CandidateProps = { "_TintColor", "_BaseColor", "_Color" };

    Renderer _renderer;
    Material _material;        // 이 인스턴스 전용 복제 머티리얼
    int _colorPropId;
    bool _hasProp;
    Color _currentColor;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        // 이 오브젝트 전용 머티리얼 인스턴스 확보 (다른 벽에 영향 X)
        _material = _renderer.material;

        ResolveColorProperty();

        if (_hasProp)
            _currentColor = _material.GetColor(_colorPropId);
    }

    void ResolveColorProperty()
    {
        _hasProp = false;

        // 1) 사용자가 직접 지정한 이름
        if (!string.IsNullOrEmpty(ColorPropertyName) && _material.HasProperty(ColorPropertyName))
        {
            _colorPropId = Shader.PropertyToID(ColorPropertyName);
            _hasProp = true;
            return;
        }

        // 2) 후보 자동 탐색
        for (int i = 0; i < CandidateProps.Length; i++)
        {
            if (_material.HasProperty(CandidateProps[i]))
            {
                _colorPropId = Shader.PropertyToID(CandidateProps[i]);
                _hasProp = true;
                return;
            }
        }

        Debug.LogWarning($"[WallFadeByDistance] '{_material.name}' 머티리얼에서 색 프로퍼티를 찾지 못했습니다. " +
                         $"Inspector의 ColorPropertyName에 정확한 이름을 지정하세요.", this);
    }

    void Start()
    {
        if (Player == null && !string.IsNullOrEmpty(PlayerTag))
        {
            var found = GameObject.FindGameObjectWithTag(PlayerTag);
            if (found != null) Player = found.transform;
        }
    }

    void OnEnable()
    {
        if (_material == null && _renderer != null)
        {
            _material = _renderer.material;
            ResolveColorProperty();
        }
    }

    Vector3 GetAxisVector()
    {
        switch (Axis)
        {
            case LocalAxis.Forward: return transform.forward;
            case LocalAxis.Up:      return transform.up;
            default:                return transform.right;
        }
    }

    void Update()
    {
        if (Player == null || _material == null || !_hasProp) return;

        // (플레이어 - 벽) 벡터를 벽의 로컬 축에 투영한 거리 (회전된 맵에서도 정확)
        Vector3 toPlayer = Player.position - transform.position;
        float dist = Mathf.Abs(Vector3.Dot(toPlayer, GetAxisVector()));

        // 가까우면 1(NearColor), 멀면 0(FarColor)
        float t = Mathf.InverseLerp(FarDistance, NearDistance, dist);
        Color targetColor = Color.Lerp(FarColor, NearColor, t);

        _currentColor = FadeSpeed > 0f
            ? Color.Lerp(_currentColor, targetColor, 1f - Mathf.Exp(-FadeSpeed * Time.deltaTime))
            : targetColor;

        _material.SetColor(_colorPropId, _currentColor);
    }

    void OnDrawGizmosSelected()
    {
        // 선택한 로컬 축 방향으로 선을 그려 범위를 표시
        Vector3 p = transform.position;
        Vector3 axis = GetAxisVector();
        Gizmos.color = new Color(1f, 1f, 0f, 0.6f);
        Gizmos.DrawLine(p - axis * NearDistance, p + axis * NearDistance);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawLine(p - axis * FarDistance, p + axis * FarDistance);
    }
}
