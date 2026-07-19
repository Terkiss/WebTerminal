using System;
using System.Linq;
using System.Collections.Generic;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core;

namespace TeruTeruPandas.Core.Agg;

/// <summary>
/// GroupBy 집계 및 연산을 위한 코어 클래스.
/// df.GroupBy(keys).Agg({...}) 형식의 문법을 지원하며, 
/// 수백만 건의 데이터를 특정 키(Key) 기준으로 버킷팅 후 
/// Sum, Mean, Count, Max, Min, Std, Var 등 고속 병렬 집계를 수행합니다.
/// </summary>
public class GroupBy
{
    private readonly string[] _groupKeys;
    private readonly Dictionary<string, IColumn> _columns;
    private readonly Dictionary<object, List<int>> _groups;

    public GroupBy(Dictionary<string, IColumn> columns, string[] groupKeys)
    {
        _columns = columns;
        _groupKeys = groupKeys;
        _groups = CreateGroups();
    }

    private Dictionary<object, List<int>> CreateGroups()
    {
        var groups = new Dictionary<object, List<int>>();

        if (_groupKeys.Length == 0)
            return groups;

        var firstColumn = _columns[_groupKeys[0]];
        var rowCount = firstColumn.Length;

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

    public TeruTeruPandas.Core.DataFrame Agg(Dictionary<string, string[]> aggregations)
    {
        var result = new Dictionary<string, IColumn>();

        // 그룹 키 컬럼들 추가
        foreach (var groupKey in _groupKeys)
        {
            result[groupKey] = CreateGroupKeyColumn(groupKey);
        }

        // 집계 함수 적용
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

    private IColumn CreateGroupKeyColumn(string groupKeyName)
    {
        var sourceColumn = _columns[groupKeyName];
        var groupValues = new List<object?>();

        foreach (var group in _groups)
        {
            var firstRowIndex = group.Value[0];
            groupValues.Add(sourceColumn.GetValue(firstRowIndex));
        }

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

    private IColumn ApplyIntAggregation(PrimitiveColumn<int> column, string function)
    {
        var results = new List<int>();

        foreach (var group in _groups)
        {
            var groupIndices = group.Value;
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