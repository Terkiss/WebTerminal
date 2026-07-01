using System;
using System.Linq;
using System.Collections.Generic;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.SIMD;

namespace TeruTeruPandas.Core.Agg;

/// <summary>
/// SIMD 하드웨어 가속을 활용한 고성능 Column-wise GroupBy 집계 엔진입니다.
/// <para>
/// 1. Group Phase: 데이터를 스캔하여 그룹 키별로 행 인덱스들을 모읍니다. (O(N))
/// 이때 인덱스들은 SIMD 연산에 최적화되도록 연속된 정수 배열(int[])로 관리됩니다.
/// </para>
/// <para>
/// 2. Agg Phase: 각 그룹별로 모인 행 인덱스들에 대해 SIMD 벡터 연산을 적용하여 
/// Sum, Mean 등의 통계량을 초고속으로 계산합니다. (O(G * VectorSize))
/// </para>
/// </summary>
public class SimdGroupBy
{
    private readonly string[] _groupKeys;
    private readonly Dictionary<string, IColumn> _columns;
    // 그룹 키 → 행 인덱스 배열 (SIMD 연산을 위해 List 대신 int[] 사용)
    private readonly Dictionary<object, int[]> _groupIndices;

    public SimdGroupBy(Dictionary<string, IColumn> columns, string[] groupKeys)
    {
        _columns = columns;
        _groupKeys = groupKeys;
        _groupIndices = CreateGroupIndices();
    }

    /// <summary>
    /// Group Phase: 키를 기준으로 행 인덱스 스팬(Span)을 생성합니다.
    /// </summary>
    private Dictionary<object, int[]> CreateGroupIndices()
    {
        var groups = new Dictionary<object, List<int>>();

        if (_groupKeys.Length == 0)
            return new Dictionary<object, int[]>();

        var firstColumn = _columns[_groupKeys[0]];
        var rowCount = firstColumn.Length;

        // 그룹 인덱스 수집 과정
        for (int i = 0; i < rowCount; i++)
        {
            var groupKey = CreateGroupKey(i);

            if (!groups.ContainsKey(groupKey))
            {
                groups[groupKey] = new List<int>();
            }

            groups[groupKey].Add(i);
        }

        // List<int>를 int[]로 변환하여 메모리 지역성 및 SIMD 접근 효율 향상
        var result = new Dictionary<object, int[]>();
        foreach (var kvp in groups)
        {
            result[kvp.Key] = kvp.Value.ToArray();
        }

        return result;
    }

    private object CreateGroupKey(int rowIndex)
    {
        if (_groupKeys.Length == 1)
        {
            var column = _columns[_groupKeys[0]];
            return column.IsNA(rowIndex) ? "NA" : column.GetValue(rowIndex) ?? "NA";
        }
        else
        {
            var keyParts = new object[_groupKeys.Length];
            for (int i = 0; i < _groupKeys.Length; i++)
            {
                var column = _columns[_groupKeys[i]];
                keyParts[i] = column.IsNA(rowIndex) ? "NA" : column.GetValue(rowIndex) ?? "NA";
            }
            return string.Join("|", keyParts);
        }
    }

    /// <summary>
    /// Agg Phase: SIMD 기반의 병렬 집계 연산을 수행합니다.
    /// </summary>
    public TeruTeruPandas.Core.DataFrame Agg(Dictionary<string, string[]> aggregations)
    {
        var result = new Dictionary<string, IColumn>();

        // 1. 그룹 키 컬럼 구성
        foreach (var groupKey in _groupKeys)
        {
            result[groupKey] = CreateGroupKeyColumn(groupKey);
        }

        // 2. 집계 함수 적용 (SIMD 최적화 경로 우선 사용)
        foreach (var agg in aggregations)
        {
            var columnName = agg.Key;
            var functions = agg.Value;

            if (!_columns.ContainsKey(columnName))
                continue;

            var sourceColumn = _columns[columnName];

            foreach (var function in functions)
            {
                var resultColumnName = $"{columnName}_{function}";
                result[resultColumnName] = ApplySimdAggregation(sourceColumn, function);
            }
        }

        return new TeruTeruPandas.Core.DataFrame(result);
    }

    private IColumn CreateGroupKeyColumn(string groupKeyName)
    {
        var sourceColumn = _columns[groupKeyName];
        var groupCount = _groupIndices.Count;
        var values = new object?[groupCount];

        int idx = 0;
        foreach (var kvp in _groupIndices)
        {
            var firstRowIdx = kvp.Value[0];
            values[idx++] = sourceColumn.GetValue(firstRowIdx);
        }

        if (sourceColumn is PrimitiveColumn<int>)
        {
            var typedValues = values.Select(v => v != null ? (int)v : 0).ToArray();
            return new PrimitiveColumn<int>(typedValues);
        }
        else if (sourceColumn is PrimitiveColumn<double>)
        {
            var typedValues = values.Select(v => v != null ? (double)v : 0.0).ToArray();
            return new PrimitiveColumn<double>(typedValues);
        }
        else if (sourceColumn is StringColumn)
        {
            var typedValues = values.Select(v => v?.ToString() ?? "").ToArray();
            return new StringColumn(typedValues);
        }

        throw new NotSupportedException($"Unsupported column type: {sourceColumn.GetType()}");
    }

    /// <summary>
    /// 컬럼 타입에 따라 SIMD 가속 여부를 판단하여 집계를 수행합니다.
    /// </summary>
    private IColumn ApplySimdAggregation(IColumn sourceColumn, string function)
    {
        // Primitive 타입(int, double)인 경우 하드웨어 가속 경로 사용
        if (sourceColumn is PrimitiveColumn<int> intColumn)
        {
            return ApplySimdAggregationInt(intColumn, function);
        }
        else if (sourceColumn is PrimitiveColumn<double> doubleColumn)
        {
            return ApplySimdAggregationDouble(doubleColumn, function);
        }

        // 지원되지 않는 타입은 일반 루프 집계로 대체(Fallback)
        return ApplyGenericAggregation(sourceColumn, function);
    }

    private IColumn ApplySimdAggregationInt(PrimitiveColumn<int> column, string function)
    {
        var groupCount = _groupIndices.Count;
        var results = new double[groupCount];

        int idx = 0;
        foreach (var kvp in _groupIndices)
        {
            var indices = kvp.Value;

            // 그룹에 해당하는 값들만 모아 연속 배열 생성
            var values = new List<int>();
            foreach (var i in indices)
            {
                if (!column.IsNA(i))
                {
                    var value = column.GetValue(i);
                    if (value != null)
                    {
                        values.Add((int)value);
                    }
                }
            }

            if (values.Count == 0)
            {
                results[idx++] = 0.0;
                continue;
            }

            var valueArray = values.ToArray();

            // SimdOperations 클래스의 벡터 가속 함수 호출
            results[idx++] = function.ToLower() switch
            {
                "sum" => SimdOperations.SumInt(valueArray),
                "mean" => SimdOperations.SumInt(valueArray) / (double)valueArray.Length,
                "count" => (double)valueArray.Length,
                "max" => valueArray.Max(),
                "min" => valueArray.Min(),
                "std" => CalculateStd(valueArray),
                "var" => CalculateVar(valueArray),
                _ => 0.0
            };
        }

        return new PrimitiveColumn<double>(results);
    }

    private IColumn ApplySimdAggregationDouble(PrimitiveColumn<double> column, string function)
    {
        var groupCount = _groupIndices.Count;
        var results = new double[groupCount];

        int idx = 0;
        foreach (var kvp in _groupIndices)
        {
            var indices = kvp.Value;

            var values = new List<double>();
            foreach (var i in indices)
            {
                if (!column.IsNA(i))
                {
                    var value = column.GetValue(i);
                    if (value != null)
                    {
                        values.Add((double)value);
                    }
                }
            }

            if (values.Count == 0)
            {
                results[idx++] = 0.0;
                continue;
            }

            var valueArray = values.ToArray();

            results[idx++] = function.ToLower() switch
            {
                "sum" => SimdOperations.SumDouble(valueArray),
                "mean" => SimdOperations.SumDouble(valueArray) / valueArray.Length,
                "count" => (double)valueArray.Length,
                "max" => valueArray.Max(),
                "min" => valueArray.Min(),
                "std" => CalculateStd(valueArray),
                "var" => CalculateVar(valueArray),
                _ => 0.0
            };
        }

        return new PrimitiveColumn<double>(results);
    }

    private IColumn ApplyGenericAggregation(IColumn sourceColumn, string function)
    {
        var groupCount = _groupIndices.Count;
        var results = new double[groupCount];

        int idx = 0;
        foreach (var kvp in _groupIndices)
        {
            var indices = kvp.Value;

            var values = new List<double>();
            foreach (var i in indices)
            {
                if (!sourceColumn.IsNA(i))
                {
                    var value = sourceColumn.GetValue(i);
                    if (value != null)
                    {
                        values.Add(Convert.ToDouble(value));
                    }
                }
            }

            if (values.Count == 0)
            {
                results[idx++] = 0.0;
                continue;
            }

            results[idx++] = function.ToLower() switch
            {
                "sum" => values.Sum(),
                "mean" => values.Average(),
                "count" => (double)values.Count,
                "max" => values.Max(),
                "min" => values.Min(),
                "std" => CalculateStd(values.ToArray()),
                "var" => CalculateVar(values.ToArray()),
                _ => 0.0
            };
        }

        return new PrimitiveColumn<double>(results);
    }

    private double CalculateStd(int[] values)
    {
        if (values.Length == 0) return 0.0;
        double mean = values.Average();
        double sumSquaredDiff = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquaredDiff / values.Length);
    }

    private double CalculateVar(int[] values)
    {
        if (values.Length == 0) return 0.0;
        double mean = values.Average();
        double sumSquaredDiff = values.Sum(v => (v - mean) * (v - mean));
        return sumSquaredDiff / values.Length;
    }

    private double CalculateStd(double[] values)
    {
        if (values.Length == 0) return 0.0;
        double mean = values.Average();
        double sumSquaredDiff = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquaredDiff / values.Length);
    }

    private double CalculateVar(double[] values)
    {
        if (values.Length == 0) return 0.0;
        double mean = values.Average();
        double sumSquaredDiff = values.Sum(v => (v - mean) * (v - mean));
        return sumSquaredDiff / values.Length;
    }

    /// <summary>
    /// 생성된 전체 그룹의 개수를 가져옵니다.
    /// </summary>
    public int GroupCount => _groupIndices.Count;

    /// <summary>
    /// 모든 그룹의 키와 인덱스 목록을 열거합니다.
    /// </summary>
    public IEnumerable<(object key, int[] indices)> Groups =>
        _groupIndices.Select(kvp => (kvp.Key, kvp.Value));
}
