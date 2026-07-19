using System;
using System.Linq;
using System.Collections.Generic;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;
using TeruTeruPandas.Core.Engine;

namespace TeruTeruPandas.Core;

/// <summary>
/// DataFrame의 데이터 가공(Merge, Join, Concat)을 확장 지원하는 유틸리티 메서드들.
/// HashJoinEngine과 연계하여 Pandas의 핵심인 이종 테이블 간 결합을 제공합니다.
/// </summary>
public static class DataFrameJoinExtensions
{
    /// <summary>
    /// Inner/Left/Right/Outer 조인 지원하는 Merge 기능
    /// strategy: Auto, Hash, Index, NestedLoop
    /// </summary>
    public static TeruTeruPandas.Core.DataFrame Merge(
        this TeruTeruPandas.Core.DataFrame left,
        TeruTeruPandas.Core.DataFrame right,
        string on,
        string how = "inner",
        JoinStrategy strategy = JoinStrategy.Auto)
    {
        if (!left.Columns.Contains(on) || !right.Columns.Contains(on))
            throw new ArgumentException($"Column '{on}' not found in one or both DataFrames");

        var leftColumn = left[on];
        var rightColumn = right[on];

        // Join 타입 결정
        var joinType = how.ToLower() switch
        {
            "inner" => JoinType.Inner,
            "left" => JoinType.Left,
            "right" => JoinType.Right,
            "outer" => JoinType.Outer,
            _ => throw new ArgumentException($"Unsupported join type: {how}")
        };

        // 전략 자동 선택
        if (strategy == JoinStrategy.Auto)
        {
            strategy = SelectJoinStrategy(left, right, on);
        }

        // 조인 실행
        List<(int leftIndex, int rightIndex)> joinResults = strategy switch
        {
            JoinStrategy.Hash => HashJoinEngine.Execute(leftColumn, rightColumn, joinType),
            JoinStrategy.Index => HashJoinEngine.ExecuteWithIndex(leftColumn, left.Index, rightColumn, right.Index, joinType),
            JoinStrategy.NestedLoop => PerformNestedLoopJoin(leftColumn, rightColumn, joinType),
            _ => HashJoinEngine.Execute(leftColumn, rightColumn, joinType)
        };

        // 결과 DataFrame 생성
        return CreateJoinedDataFrame(left, right, joinResults, on);
    }

    private static DataFrame CreateJoinedDataFrame(
        DataFrame left,
        DataFrame right,
        List<(int leftIndex, int rightIndex)> joinResults,
        string? commonColumn)
    {
        var resultColumns = new Dictionary<string, IColumn>();
        var rowCount = joinResults.Count;

        // Left DataFrame 컬럼 추가
        foreach (var columnName in left.Columns)
        {
            if (commonColumn != null && columnName == commonColumn)
            {
                // 공통 컬럼은 한 번만 추가
                var values = new object?[rowCount];
                for (int i = 0; i < rowCount; i++)
                {
                    var (leftIdx, rightIdx) = joinResults[i];
                    values[i] = leftIdx >= 0 ? left[columnName].GetValue(leftIdx) :
                               (rightIdx >= 0 ? right[columnName].GetValue(rightIdx) : null);
                }
                resultColumns[columnName] = CreateColumnFromValues(values, left[columnName].DataType);
            }
            else
            {
                var values = new object?[rowCount];
                for (int i = 0; i < rowCount; i++)
                {
                    var leftIdx = joinResults[i].leftIndex;
                    values[i] = leftIdx >= 0 ? left[columnName].GetValue(leftIdx) : null;
                }
                resultColumns[columnName] = CreateColumnFromValues(values, left[columnName].DataType);
            }
        }

        // Right DataFrame 컬럼 추가
        foreach (var columnName in right.Columns)
        {
            if (commonColumn != null && columnName == commonColumn)
                continue; // 공통 컬럼은 이미 추가됨

            var newColumnName = resultColumns.ContainsKey(columnName) ? $"{columnName}_right" : columnName;
            var values = new object?[rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                var rightIdx = joinResults[i].rightIndex;
                values[i] = rightIdx >= 0 ? right[columnName].GetValue(rightIdx) : null;
            }

            resultColumns[newColumnName] = CreateColumnFromValues(values, right[columnName].DataType);
        }

        return new DataFrame(resultColumns);
    }

    /// <summary>
    /// Join 전략 자동 선택
    /// </summary>
    private static JoinStrategy SelectJoinStrategy(DataFrame left, DataFrame right, string on)
    {
        const int NESTED_LOOP_THRESHOLD = 100;

        // 작은 테이블은 Nested Loop
        if (left.Index.Length < NESTED_LOOP_THRESHOLD && right.Index.Length < NESTED_LOOP_THRESHOLD)
        {
            return JoinStrategy.NestedLoop;
        }

        // 기본: Hash Join
        return JoinStrategy.Hash;
    }

    /// <summary>
    /// Nested Loop Join (fallback)
    /// </summary>
    private static List<(int leftIndex, int rightIndex)> PerformNestedLoopJoin(
        IColumn leftColumn,
        IColumn rightColumn,
        JoinType joinType)
    {
        switch (joinType)
        {
            case JoinType.Inner:
                return PerformInnerJoin(leftColumn, rightColumn);
            case JoinType.Left:
                return PerformLeftJoin(leftColumn, rightColumn);
            case JoinType.Right:
                return PerformRightJoin(leftColumn, rightColumn);
            case JoinType.Outer:
                return PerformOuterJoin(leftColumn, rightColumn);
            default:
                throw new ArgumentException($"Unsupported join type: {joinType}");
        }
    }

    /// <summary>
    /// 인덱스 기반 조인
    /// </summary>
    public static TeruTeruPandas.Core.DataFrame Join(this TeruTeruPandas.Core.DataFrame left, TeruTeruPandas.Core.DataFrame right, string how = "left")
    {
        var joinResults = new List<(int leftIndex, int rightIndex)>();

        switch (how.ToLower())
        {
            case "inner":
                for (int i = 0; i < Math.Min(left.Index.Length, right.Index.Length); i++)
                {
                    joinResults.Add((i, i));
                }
                break;
            case "left":
                for (int i = 0; i < left.Index.Length; i++)
                {
                    int rightIndex = i < right.Index.Length ? i : -1;
                    joinResults.Add((i, rightIndex));
                }
                break;
            case "right":
                for (int i = 0; i < right.Index.Length; i++)
                {
                    int leftIndex = i < left.Index.Length ? i : -1;
                    joinResults.Add((leftIndex, i));
                }
                break;
            case "outer":
                int maxRows = Math.Max(left.Index.Length, right.Index.Length);
                for (int i = 0; i < maxRows; i++)
                {
                    int leftIndex = i < left.Index.Length ? i : -1;
                    int rightIndex = i < right.Index.Length ? i : -1;
                    joinResults.Add((leftIndex, rightIndex));
                }
                break;
            default:
                throw new ArgumentException($"Unsupported join type: {how}");
        }

        return CreateJoinedDataFrame(left, right, joinResults, null);
    }

    /// <summary>
    /// 행/열 단위 연결
    /// </summary>
    public static DataFrame Concat(IEnumerable<DataFrame> dataframes, int axis = 0)
    {
        var dfList = dataframes.ToList();
        if (dfList.Count == 0)
            throw new ArgumentException("At least one DataFrame is required");

        if (axis == 0)
        {
            return ConcatRows(dfList);
        }
        else if (axis == 1)
        {
            return ConcatColumns(dfList);
        }
        else
        {
            throw new ArgumentException("Axis must be 0 (rows) or 1 (columns)");
        }
    }

    private static List<(int leftIndex, int rightIndex)> PerformInnerJoin(IColumn leftColumn, IColumn rightColumn)
    {
        var results = new List<(int leftIndex, int rightIndex)>();

        for (int i = 0; i < leftColumn.Length; i++)
        {
            if (leftColumn.IsNA(i)) continue;

            var leftValue = leftColumn.GetValue(i);

            for (int j = 0; j < rightColumn.Length; j++)
            {
                if (rightColumn.IsNA(j)) continue;

                var rightValue = rightColumn.GetValue(j);

                if (ValuesEqual(leftValue, rightValue))
                {
                    results.Add((i, j));
                }
            }
        }

        return results;
    }

    private static List<(int leftIndex, int rightIndex)> PerformLeftJoin(IColumn leftColumn, IColumn rightColumn)
    {
        var results = new List<(int leftIndex, int rightIndex)>();

        for (int i = 0; i < leftColumn.Length; i++)
        {
            bool found = false;

            if (!leftColumn.IsNA(i))
            {
                var leftValue = leftColumn.GetValue(i);

                for (int j = 0; j < rightColumn.Length; j++)
                {
                    if (rightColumn.IsNA(j)) continue;

                    var rightValue = rightColumn.GetValue(j);

                    if (ValuesEqual(leftValue, rightValue))
                    {
                        results.Add((i, j));
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                results.Add((i, -1));
            }
        }

        return results;
    }

    private static List<(int leftIndex, int rightIndex)> PerformRightJoin(IColumn leftColumn, IColumn rightColumn)
    {
        var results = new List<(int leftIndex, int rightIndex)>();

        for (int j = 0; j < rightColumn.Length; j++)
        {
            bool found = false;

            if (!rightColumn.IsNA(j))
            {
                var rightValue = rightColumn.GetValue(j);

                for (int i = 0; i < leftColumn.Length; i++)
                {
                    if (leftColumn.IsNA(i)) continue;

                    var leftValue = leftColumn.GetValue(i);

                    if (ValuesEqual(leftValue, rightValue))
                    {
                        results.Add((i, j));
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                results.Add((-1, j));
            }
        }

        return results;
    }

    private static List<(int leftIndex, int rightIndex)> PerformOuterJoin(IColumn leftColumn, IColumn rightColumn)
    {
        var results = new List<(int leftIndex, int rightIndex)>();
        var rightMatched = new HashSet<int>();

        for (int i = 0; i < leftColumn.Length; i++)
        {
            bool found = false;

            if (!leftColumn.IsNA(i))
            {
                var leftValue = leftColumn.GetValue(i);

                for (int j = 0; j < rightColumn.Length; j++)
                {
                    if (rightColumn.IsNA(j)) continue;

                    var rightValue = rightColumn.GetValue(j);

                    if (ValuesEqual(leftValue, rightValue))
                    {
                        results.Add((i, j));
                        rightMatched.Add(j);
                        found = true;
                    }
                }
            }

            if (!found)
            {
                results.Add((i, -1));
            }
        }

        for (int j = 0; j < rightColumn.Length; j++)
        {
            if (!rightMatched.Contains(j))
            {
                results.Add((-1, j));
            }
        }

        return results;
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        return left.Equals(right);
    }

    private static IColumn CreateColumnFromValues(object?[] values, Type targetType)
    {
        var naMask = values.Select(v => v == null).ToArray();

        if (targetType == typeof(int))
        {
            var typedData = values.Select(v => v != null ? Convert.ToInt32(v) : 0).ToArray();
            return new PrimitiveColumn<int>(typedData, naMask);
        }
        else if (targetType == typeof(long))
        {
            var typedData = values.Select(v => v != null ? Convert.ToInt64(v) : 0L).ToArray();
            return new PrimitiveColumn<long>(typedData, naMask);
        }
        else if (targetType == typeof(double))
        {
            var typedData = values.Select(v => v != null ? Convert.ToDouble(v) : 0.0).ToArray();
            return new PrimitiveColumn<double>(typedData, naMask);
        }
        else if (targetType == typeof(bool))
        {
            var typedData = values.Select(v => v != null ? Convert.ToBoolean(v) : false).ToArray();
            return new PrimitiveColumn<bool>(typedData, naMask);
        }
        else if (targetType == typeof(DateTime))
        {
            var typedData = values.Select(v => v != null ? Convert.ToDateTime(v) : default(DateTime)).ToArray();
            return new PrimitiveColumn<DateTime>(typedData, naMask);
        }
        else if (targetType == typeof(string))
        {
            // StringColumn allows null internally for NA
            var typedValues = values.Select(v => v?.ToString()).ToArray();
            return new StringColumn(typedValues);
        }

        throw new NotSupportedException($"Type {targetType} not supported");
    }

    private static DataFrame ConcatRows(List<DataFrame> dataframes)
    {
        var allColumnNames = dataframes.SelectMany(df => df.Columns).Distinct().ToList();
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in allColumnNames)
        {
            var allValues = new List<object?>();
            var typesInColumn = new HashSet<Type>();

            foreach (var df in dataframes)
            {
                if (!df.Columns.Contains(columnName))
                {
                    for (int i = 0; i < df.RowCount; i++)
                        allValues.Add(null);
                }
                else
                {
                    var column = df[columnName];
                    typesInColumn.Add(column.DataType);
                    for (int i = 0; i < column.Length; i++)
                    {
                        allValues.Add(column.GetValue(i));
                    }
                }
            }

            // 타입 추론: 하나라도 string이면 string, 아니면 가장 넓은 타입 선택
            Type targetType = typeof(string);
            if (typesInColumn.Contains(typeof(string))) { targetType = typeof(string); }
            else if (typesInColumn.Contains(typeof(DateTime))) { targetType = typeof(DateTime); }
            else if (typesInColumn.Contains(typeof(double))) { targetType = typeof(double); }
            else if (typesInColumn.Contains(typeof(long))) { targetType = typeof(long); }
            else if (typesInColumn.Contains(typeof(int))) { targetType = typeof(int); }
            else if (typesInColumn.Contains(typeof(bool))) { targetType = typeof(bool); }
            else if (typesInColumn.Count > 0) { targetType = typesInColumn.First(); }

            resultColumns[columnName] = CreateColumnFromValues(allValues.ToArray(), targetType);
        }

        return new DataFrame(resultColumns);
    }

    private static DataFrame ConcatColumns(List<DataFrame> dataframes)
    {
        var firstDf = dataframes[0];
        var rowCount = firstDf.Index.Length;
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var df in dataframes)
        {
            if (df.Index.Length != rowCount)
                throw new ArgumentException("All DataFrames must have the same number of rows for column concatenation");

            foreach (var columnName in df.Columns)
            {
                var uniqueName = columnName;
                int suffix = 1;

                while (resultColumns.ContainsKey(uniqueName))
                {
                    uniqueName = $"{columnName}_{suffix}";
                    suffix++;
                }

                resultColumns[uniqueName] = df[columnName];
            }
        }

        return new DataFrame(resultColumns);
    }
}
