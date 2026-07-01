using System;
using System.Linq;
using System.Collections.Generic;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core;

namespace TeruTeruPandas.Core.Agg;

/// <summary>
/// GroupBy 집계 및 연산을 위한 코어 클래스입니다.
/// df.GroupBy(keys).Agg({...}) 형식의 문법을 지원하며, 
/// 수백만 건의 데이터를 특정 키(Key) 기준으로 버킷팅 후 
/// Sum, Mean, Count, Max, Min, Std, Var 등 고속 집계를 수행합니다.
/// </summary>
public class GroupBy
{
    private readonly string[] _groupKeys;
    private readonly Dictionary<string, IColumn> _columns;
    // 그룹 키별로 해당하는 행 인덱스(Row Index)들의 리스트를 관리하는 해시맵
    private readonly Dictionary<object, List<int>> _groups;

    public GroupBy(Dictionary<string, IColumn> columns, string[] groupKeys)
    {
        _columns = columns;
        _groupKeys = groupKeys;
        _groups = CreateGroups();
    }

    /// <summary>
    /// 데이터를 스캔하여 그룹 키별로 행 인덱스들을 분류합니다. (버킷팅 과정)
    /// </summary>
    private Dictionary<object, List<int>> CreateGroups()
    {
        var groups = new Dictionary<object, List<int>>();

        if (_groupKeys.Length == 0)
            return groups;

        var firstColumn = _columns[_groupKeys[0]];
        var rowCount = firstColumn.Length;

        // 전체 행을 순회하며 그룹 키를 생성하고 해당 버킷에 인덱스 추가 (O(N))
        for (int i = 0; i < rowCount; i++)
        {
            var groupKey = CreateGroupKey(i);

            if (!groups.ContainsKey(groupKey))
            {
                groups[groupKey] = new List<int>();
            }

            groups[groupKey].Add(i);
        }

        return groups;
    }

    /// <summary>
    /// 특정 행의 그룹 키를 생성합니다. 멀티 키의 경우 문자열로 조합합니다.
    /// </summary>
    private object CreateGroupKey(int rowIndex)
    {
        if (_groupKeys.Length == 1)
        {
            var column = _columns[_groupKeys[0]];
            return column.IsNA(rowIndex) ? "NA" : column.GetValue(rowIndex) ?? "NA";
        }
        else
        {
            // 여러 컬럼을 조합하여 유니크한 키 생성 (복합 키 대응)
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
    /// 지정된 집계 규칙에 따라 데이터를 요약한 새로운 DataFrame을 생성합니다.
    /// </summary>
    /// <param name="aggregations">컬럼명과 집계 함수 목록의 맵핑 (예: {"Salary", new[] {"sum", "mean"}})</param>
    public TeruTeruPandas.Core.DataFrame Agg(Dictionary<string, string[]> aggregations)
    {
        var result = new Dictionary<string, IColumn>();

        // 1. 결과 테이블에 그룹 키 컬럼들 추가
        foreach (var groupKey in _groupKeys)
        {
            result[groupKey] = CreateGroupKeyColumn(groupKey);
        }

        // 2. 각 그룹별로 집계 함수를 적용하여 새로운 컬럼 생성
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
                result[resultColumnName] = ApplyAggregation(sourceColumn, function);
            }
        }

        return new TeruTeruPandas.Core.DataFrame(result);
    }

    /// <summary>
    /// 결과 데이터프레임의 그룹 키 컬럼을 생성합니다.
    /// </summary>
    private IColumn CreateGroupKeyColumn(string groupKeyName)
    {
        var sourceColumn = _columns[groupKeyName];
        var groupValues = new List<object?>();

        // 각 그룹의 첫 번째 행에서 키 값을 추출
        foreach (var group in _groups)
        {
            var firstRowIndex = group.Value[0];
            groupValues.Add(sourceColumn.GetValue(firstRowIndex));
        }

        // 타입별 적절한 컬럼 객체 생성
        if (sourceColumn is PrimitiveColumn<int>)
        {
            var data = groupValues.Cast<int?>().Select(x => x ?? 0).ToArray();
            return new PrimitiveColumn<int>(data);
        }
        else if (sourceColumn is PrimitiveColumn<double>)
        {
            var data = groupValues.Cast<double?>().Select(x => x ?? 0.0).ToArray();
            return new PrimitiveColumn<double>(data);
        }
        else
        {
            var data = groupValues.Cast<string>().ToArray();
            return new StringColumn(data);
        }
    }

    /// <summary>
    /// 컬럼 타입에 맞는 집계 로직을 호출합니다.
    /// </summary>
    private IColumn ApplyAggregation(IColumn sourceColumn, string function)
    {
        if (sourceColumn is PrimitiveColumn<int> intColumn)
        {
            return ApplyIntAggregation(intColumn, function);
        }
        else if (sourceColumn is PrimitiveColumn<double> doubleColumn)
        {
            return ApplyDoubleAggregation(doubleColumn, function);
        }
        else if (sourceColumn is StringColumn stringColumn)
        {
            return ApplyStringAggregation(stringColumn, function);
        }
        else
        {
            throw new NotSupportedException($"Aggregation not supported for column type: {sourceColumn.GetType()}");
        }
    }

    /// <summary>
    /// 정수형 컬럼에 대한 집계 처리를 수행합니다.
    /// </summary>
    private IColumn ApplyIntAggregation(PrimitiveColumn<int> column, string function)
    {
        var results = new List<int>();

        foreach (var group in _groups)
        {
            var groupIndices = group.Value;
            // 결측치를 제외한 실제 값들만 추출
            var values = groupIndices
                .Where(i => !column.IsNA(i))
                .Select(i => (int)column.GetValue(i)!)
                .ToList();

            if (values.Count == 0)
            {
                results.Add(0);
                continue;
            }

            var result = function.ToLower() switch
            {
                "sum" => values.Sum(),
                "mean" => (int)Math.Round(values.Average()),
                "count" => values.Count(),
                "max" => values.Max(),
                "min" => values.Min(),
                _ => throw new ArgumentException($"Unknown aggregation function: {function}")
            };

            results.Add(result);
        }

        return new PrimitiveColumn<int>(results.ToArray());
    }

    /// <summary>
    /// 실수형 컬럼에 대한 집계 처리를 수행합니다.
    /// </summary>
    private IColumn ApplyDoubleAggregation(PrimitiveColumn<double> column, string function)
    {
        var results = new List<double>();

        foreach (var group in _groups)
        {
            var groupIndices = group.Value;
            var values = groupIndices
                .Where(i => !column.IsNA(i))
                .Select(i => (double)column.GetValue(i)!)
                .ToList();

            if (values.Count == 0)
            {
                results.Add(0.0);
                continue;
            }

            var result = function.ToLower() switch
            {
                "sum" => values.Sum(),
                "mean" => values.Average(),
                "count" => (double)values.Count(),
                "max" => values.Max(),
                "min" => values.Min(),
                "std" => CalculateStandardDeviation(values),
                "var" => CalculateVariance(values),
                _ => throw new ArgumentException($"Unknown aggregation function: {function}")
            };

            results.Add(result);
        }

        return new PrimitiveColumn<double>(results.ToArray());
    }

    /// <summary>
    /// 문자열 컬럼에 대한 집계 처리를 수행합니다.
    /// </summary>
    private IColumn ApplyStringAggregation(StringColumn column, string function)
    {
        var results = new List<object?>();

        foreach (var group in _groups)
        {
            var groupIndices = group.Value;
            var values = groupIndices
                .Where(i => !column.IsNA(i))
                .Select(i => column.GetValue(i)?.ToString())
                .Where(s => s != null)
                .ToList();

            object? result = function.ToLower() switch
            {
                "count" => values.Count(),
                "first" => values.FirstOrDefault(),
                "last" => values.LastOrDefault(),
                _ => throw new ArgumentException($"String aggregation function '{function}' not supported")
            };

            results.Add(result);
        }

        if (function.ToLower() == "count")
        {
            var intResults = results.Cast<int>().ToArray();
            return new PrimitiveColumn<int>(intResults);
        }
        else
        {
            var stringResults = results.Cast<string>().ToArray();
            return new StringColumn(stringResults);
        }
    }

    private double CalculateStandardDeviation(List<double> values)
    {
        if (values.Count <= 1)
            return 0.0;

        var mean = values.Average();
        var sumOfSquaredDifferences = values
            .Select(val => Math.Pow(val - mean, 2))
            .Sum();

        var variance = sumOfSquaredDifferences / (values.Count - 1);
        return Math.Sqrt(variance);
    }

    private double CalculateVariance(List<double> values)
    {
        if (values.Count <= 1)
            return 0.0;

        var mean = values.Average();
        var sumOfSquaredDifferences = values
            .Select(val => Math.Pow(val - mean, 2))
            .Sum();

        return sumOfSquaredDifferences / (values.Count - 1);
    }
}
