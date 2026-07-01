namespace TeruTeruPandas.Core;

/// <summary>
/// DataFrame 병합 연산 시 엔진이 내부적으로 사용할 조인(Join) 스케줄링 전략
/// </summary>
public enum JoinStrategy
{
    /// <summary>
    /// 자동 선택: Index 존재 여부와 크기에 따라 최적 전략 선택
    /// </summary>
    Auto,

    /// <summary>
    /// Hash Join: HashMap 기반 조인 (기본 전략)
    /// </summary>
    Hash,

    /// <summary>
    /// Index Join: 기존 Index 활용
    /// </summary>
    Index,

    /// <summary>
    /// Nested Loop Join: O(N*M) fallback
    /// </summary>
    NestedLoop
}

/// <summary>
/// Join 타입
/// </summary>
public enum JoinType
{
    Inner,
    Left,
    Right,
    Outer
}
