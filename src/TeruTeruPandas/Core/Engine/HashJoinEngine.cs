using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.Core.Engine;

/// <summary>
/// DataFrame 병합을 위한 고성능 Hash Join 엔진.
/// Build Phase: 크기가 더 작은 테이블을 선택해 HashMap(해시맵)을 메모리 상에 구축합니다. (O(N))
/// Probe Phase: 반대쪽 더 큰 테이블을 스트리밍하며 O(1) 해시룩업으로 일치하는 행 인덱스 매핑 결과를 반환합니다.
/// </summary>
public class HashJoinEngine
{
    /// <summary>
    /// Hash Join 수행
    /// </summary>
    public static List<(int leftIndex, int rightIndex)> Execute(
        IColumn leftColumn,
        IColumn rightColumn,
        JoinType joinType)
    {
        // Build Phase: 작은 쪽을 선택 (Right를 기본으로)
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
    /// Index 기반 Hash Join (Index가 있는 경우)
    /// </summary>
    public static List<(int leftIndex, int rightIndex)> ExecuteWithIndex(
        IColumn leftColumn,
        Index.Index leftIndex,
        IColumn rightColumn,
        Index.Index rightIndex,
        JoinType joinType)
    {
        var results = new List<(int leftIndex, int rightIndex)>();

        // Right Index를 HashMap으로 활용
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

        // Left를 스캔하면서 Join
        for (int i = 0; i < leftColumn.Length; i++)
        {
            if (leftColumn.IsNA(i))
            {
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

        // Right Join의 경우 매칭되지 않은 Right 행 추가
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
            // Left가 Build, Right를 Probe
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

            // Left Join의 경우 매칭되지 않은 Left 행 추가
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
            // Right가 Build, Left를 Probe
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

            // Right Join의 경우 매칭되지 않은 Right 행 추가
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
