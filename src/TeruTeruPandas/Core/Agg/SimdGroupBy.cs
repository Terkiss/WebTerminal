using System;
using System.Linq;
using System.Collections.Generic;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.SIMD;

namespace TeruTeruPandas.Core.Agg;

/// <summary>
/// SIMD 기반 Column-wise GroupBy 집계 엔진
/// Phase 1: Group Phase와 Agg Phase 분리
/// </summary>
public class SimdGroupBy
{
    private readonly string[] _groupKeys;
    private readonly Dictionary<string, IColumn> _columns;
    private readonly Dictionary<object, int[]> _groupIndices; // List<int> → int[] for SIMD

    public SimdGroupBy(Dictionary<string, IColumn> columns, string[] groupKeys)
    {
        _columns = columns;
        _groupKeys = groupKeys;
        _groupIndices = CreateGroupIndices();
    }

    /// <summary>
    /// Group Phase: key → RowIndexSpan 생성
    /// </summary>
    private Dictionary<object, int[]> CreateGroupIndices()
    {
        var groups = new Dictionary<object, List<int>>();

        if (_groupKeys.Length == 0)
            return new Dictionary<object, int[]>();

        var firstColumn = _columns[_groupKeys[0]];
        var rowCount = firstColumn.Length;

        // 그룹 인덱스 수집
        for (int i = 0; i < rowCount; i++)
        {
            var groupKey = CreateGroupKey(i);

            if (!groups.ContainsKey(groupKey))
            {
                groups[groupKey] = new List<int>();
            }

            groups[groupKey].Add(i);
        }

        // List<int> → int[] 변환 (연속 메모리)
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
    /// Agg Phase: SIMD 기반 집계
    /// </summary>
    public TeruTeruPandas.Core.DataFrame Agg(Dictionary<string, string[]> aggregations)
    {
        var result = new Dictionary<string, IColumn>();

        // 그룹 키 컬럼들 추가
        foreach (var groupKey in _groupKeys)
        {
            result[groupKey] = CreateGroupKeyColumn(groupKey);
        }

        // 집계 함수 적용 (SIMD 경로 사용)
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

        // 타입에 따라 적절한 컬럼 생성
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

    private IColumn ApplySimdAggregation(IColumn sourceColumn, string function)
    {
        var groupCount = _groupIndices.Count;

        // SIMD 경로: PrimitiveColumn<int> 또는 PrimitiveColumn<double>
        if (sourceColumn is PrimitiveColumn<int> intColumn)
        {
            return ApplySimdAggregationInt(intColumn, function);
        }
        else if (sourceColumn is PrimitiveColumn<double> doubleColumn)
        {
            return ApplySimdAggregationDouble(doubleColumn, function);
        }

        // Fallback: 일반 집계
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

            // 연속 메모리로 값 추출
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

            results[idx++] = function.ToLower() switch
            {
                "sum" => SimdOperations.SumInt(valueArray),
                "mean" => SimdOperations.SumInt(valueArray) / valueArray.Length,
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

            // 연속 메모리로 값 추출
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
                "count" => (double)((IList<double>)values).Count,
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

    public int GroupCount => _groupIndices.Count;

    public IEnumerable<(object key, int[] indices)> Groups =>
        _groupIndices.Select(kvp => (kvp.Key, kvp.Value));
}
