using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.Core.Engine;

/// <summary>
/// DataFrame 병합을 위한 고성능 Hash Join 엔진.
/// <para>
/// 1. Build Phase: 크기가 더 작은 테이블을 선택해 HashMap(해시맵)을 메모리 상에 구축합니다. (O(N))
/// 이때 해시맵의 키는 조인 컬럼의 값이고, 밸류는 해당 값을 가진 행 인덱스들의 리스트입니다.
/// </para>
/// <para>
/// 2. Probe Phase: 반대쪽 더 큰 테이블을 스캔(Streaming)하며 O(1) 해시룩업으로 일치하는 행을 찾아 결과셋에 추가합니다. (O(M))
/// </para>
/// 전체 시간 복잡도는 O(N + M)으로 매우 효율적입니다.
/// </summary>
public class HashJoinEngine
{
    /// <summary>
    /// 두 컬럼을 기준으로 Hash Join을 수행하여 매칭된 행 인덱스 쌍의 리스트를 반환합니다.
    /// </summary>
    /// <param name="leftColumn">왼쪽 데이터프레임의 조인 키 컬럼</param>
    /// <param name="rightColumn">오른쪽 데이터프레임의 조인 키 컬럼</param>
    /// <param name="joinType">조인 방식 (Inner, Left, Right, Outer)</param>
    /// <returns>왼쪽 인덱스와 오른쪽 인덱스의 튜플 리스트</returns>
    public static List<(int leftIndex, int rightIndex)> Execute(
        IColumn leftColumn,
        IColumn rightColumn,
        JoinType joinType)
    {
        // 최적화: 메모리 사용량을 줄이기 위해 더 작은 테이블을 Build Phase 대상으로 선택합니다.
        bool buildLeft = leftColumn.Length < rightColumn.Length;

        if (buildLeft)
        {
            var hashMap = BuildHashMap(leftColumn);
            return ProbeHashMap(hashMap, rightColumn, leftColumn.Length, joinType, buildLeft: true);
        }
        else
        {
            var hashMap = BuildHashMap(rightColumn);
            return ProbeHashMap(hashMap, leftColumn, rightColumn.Length, joinType, buildLeft: false);
        }
    }

    /// <summary>
    /// 인덱스 정보를 활용하여 Hash Join을 수행합니다.
    /// (현재는 인덱스 객체 유무와 상관없이 컬럼 데이터를 기반으로 해시맵을 생성하도록 구현됨)
    /// </summary>
    public static List<(int leftIndex, int rightIndex)> ExecuteWithIndex(
        IColumn leftColumn,
        Index.Index leftIndex,
        IColumn rightColumn,
        Index.Index rightIndex,
        JoinType joinType)
    {
        var results = new List<(int leftIndex, int rightIndex)>();

        // Build Phase: 오른쪽 컬럼으로 해시맵 구축
        var rightHashMap = new Dictionary<object, List<int>>();

        for (int i = 0; i < rightColumn.Length; i++)
        {
            if (rightColumn.IsNA(i)) continue;

            var key = rightColumn.GetValue(i);
            if (key == null) continue;

            if (!rightHashMap.ContainsKey(key))
            {
                rightHashMap[key] = new List<int>();
            }
            rightHashMap[key].Add(i);
        }

        // Probe Phase: 왼쪽 컬럼을 스캔하면서 매칭 확인
        for (int i = 0; i < leftColumn.Length; i++)
        {
            if (leftColumn.IsNA(i))
            {
                // Left Join/Outer Join 시 매칭 안된 행에 대해 -1(null) 할당
                if (joinType == JoinType.Left || joinType == JoinType.Outer)
                {
                    results.Add((i, -1));
                }
                continue;
            }

            var leftValue = leftColumn.GetValue(i);
            if (leftValue == null) continue;

            if (rightHashMap.TryGetValue(leftValue, out var rightIndices))
            {
                // 1:N 매칭 대응
                foreach (var rightIdx in rightIndices)
                {
                    results.Add((i, rightIdx));
                }
            }
            else if (joinType == JoinType.Left || joinType == JoinType.Outer)
            {
                results.Add((i, -1));
            }
        }

        // Right Join/Outer Join의 경우 매칭되지 않은 오른쪽 행들을 추가로 처리 (Full Outer 구현용)
        if (joinType == JoinType.Right || joinType == JoinType.Outer)
        {
            var matchedRightIndices = new HashSet<int>(results.Select(r => r.rightIndex));

            for (int i = 0; i < rightColumn.Length; i++)
            {
                if (!matchedRightIndices.Contains(i))
                {
                    results.Add((-1, i));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 조인 키 컬럼을 읽어 값별 인덱스 위치를 해시맵으로 구성합니다.
    /// 중복 키를 지원하기 위해 리스트 형태로 인덱스를 저장합니다.
    /// </summary>
    private static Dictionary<object, List<int>> BuildHashMap(IColumn column)
    {
        var hashMap = new Dictionary<object, List<int>>();

        for (int i = 0; i < column.Length; i++)
        {
            if (column.IsNA(i)) continue;

            var value = column.GetValue(i);
            if (value == null) continue;

            if (!hashMap.ContainsKey(value))
            {
                hashMap[value] = new List<int>();
            }
            hashMap[value].Add(i);
        }

        return hashMap;
    }

    /// <summary>
    /// 미리 구축된 해시맵을 사용하여 상대 테이블을 조회합니다.
    /// 조인 타입에 따른 결과 인덱스 쌍을 생성합니다.
    /// </summary>
    private static List<(int leftIndex, int rightIndex)> ProbeHashMap(
        Dictionary<object, List<int>> hashMap,
        IColumn probeColumn,
        int buildColumnLength,
        JoinType joinType,
        bool buildLeft)
    {
        var results = new List<(int leftIndex, int rightIndex)>();

        if (buildLeft)
        {
            // Left가 Build 되었으므로, Right를 Probe(스캔)합니다.
            for (int rightIdx = 0; rightIdx < probeColumn.Length; rightIdx++)
            {
                if (probeColumn.IsNA(rightIdx))
                {
                    if (joinType == JoinType.Right || joinType == JoinType.Outer)
                    {
                        results.Add((-1, rightIdx));
                    }
                    continue;
                }

                var probeValue = probeColumn.GetValue(rightIdx);
                if (probeValue == null) continue;

                if (hashMap.TryGetValue(probeValue, out var leftIndices))
                {
                    foreach (var leftIdx in leftIndices)
                    {
                        results.Add((leftIdx, rightIdx));
                    }
                }
                else if (joinType == JoinType.Right || joinType == JoinType.Outer)
                {
                    results.Add((-1, rightIdx));
                }
            }

            // Left Join/Outer Join 시 Build 테이블(Left) 중 매칭 안 된 행 처리
            if (joinType == JoinType.Left || joinType == JoinType.Outer)
            {
                var matchedLeftIndices = new HashSet<int>(results.Select(r => r.leftIndex));

                for (int i = 0; i < buildColumnLength; i++)
                {
                    if (!matchedLeftIndices.Contains(i))
                    {
                        results.Add((i, -1));
                    }
                }
            }
        }
        else
        {
            // Right가 Build 되었으므로, Left를 Probe(스캔)합니다.
            for (int leftIdx = 0; leftIdx < probeColumn.Length; leftIdx++)
            {
                if (probeColumn.IsNA(leftIdx))
                {
                    if (joinType == JoinType.Left || joinType == JoinType.Outer)
                    {
                        results.Add((leftIdx, -1));
                    }
                    continue;
                }

                var probeValue = probeColumn.GetValue(leftIdx);
                if (probeValue == null) continue;

                if (hashMap.TryGetValue(probeValue, out var rightIndices))
                {
                    foreach (var rightIdx in rightIndices)
                    {
                        results.Add((leftIdx, rightIdx));
                    }
                }
                else if (joinType == JoinType.Left || joinType == JoinType.Outer)
                {
                    results.Add((leftIdx, -1));
                }
            }

            // Right Join/Outer Join 시 Build 테이블(Right) 중 매칭 안 된 행 처리
            if (joinType == JoinType.Right || joinType == JoinType.Outer)
            {
                var matchedRightIndices = new HashSet<int>(results.Select(r => r.rightIndex));

                for (int i = 0; i < buildColumnLength; i++)
                {
                    if (!matchedRightIndices.Contains(i))
                    {
                        results.Add((-1, i));
                    }
                }
            }
        }

        return results;
    }
}
