using System.Collections.Generic;
using System.Linq;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.Core;

/// <summary>
/// DataFrame의 차원 축소 및 형태 변형(Pivot, Melt) 연산을 지원하는 확장 클래스.
/// Pandas의 pivot_table(Long -> Wide) 및 melt(Wide -> Long) 기능을 C# 네이티브로 제공합니다.
/// </summary>
public static class DataFramePivotExtensions
{
    /// <summary>
    /// 피벗 테이블 생성
    /// </summary>
    public static DataFrame Pivot(this DataFrame df, string indexCol, string columnCol, string valueCol)
    {
        try
        {
            var indexColumn = df[indexCol];
            var pivotColumn = df[columnCol];
            var valueColumn = df[valueCol];

            // 1. 고유 값 수집 및 매핑 (O(N))
            var uniqueIndexes = new HashSet<object>();
            var uniqueColumns = new HashSet<object>();
            var valueMap = new Dictionary<(object, object), object?>();

            for (int i = 0; i < df.Index.Length; i++)
            {
                var idxVal = indexColumn.GetValue(i);
                var colVal = pivotColumn.GetValue(i);

                if (idxVal == null || colVal == null) continue;

                uniqueIndexes.Add(idxVal);
                uniqueColumns.Add(colVal);

                // 중복 발생 시 마지막 값 덮어쓰기 (pandas pivot 기본 동작)
                valueMap[(idxVal, colVal)] = valueColumn.GetValue(i);
            }

            var sortedIndexes = uniqueIndexes.Where(x => x != null).OrderBy(x => x!).ToArray();
            var sortedColumns = uniqueColumns.Where(x => x != null).OrderBy(x => x!).ToArray();

            // 2. 결과 데이터 생성
            var resultColumns = new Dictionary<string, IColumn>();

            // 인덱스 컬럼
            resultColumns[indexCol] = CreateColumnFromObjects(sortedIndexes!, indexColumn.DataType);

            // 피벗된 값 컬럼들
            foreach (var colKey in sortedColumns)
            {
                if (colKey == null) continue;
                var colName = $"{valueCol}_{colKey}";
                var colValues = new object?[sortedIndexes.Length];

                for (int i = 0; i < sortedIndexes.Length; i++)
                {
                    var idxKey = sortedIndexes[i];
                    if (idxKey != null && valueMap.TryGetValue((idxKey, colKey), out var val))
                    {
                        colValues[i] = val;
                    }
                    else
                    {
                        colValues[i] = null; // Missing data
                    }
                }

                resultColumns[colName] = CreateColumnFromObjects(colValues!, valueColumn.DataType);
            }

            return new DataFrame(resultColumns);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Pivot operation failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Melt (Wide -> Long)
    /// </summary>
    public static DataFrame Melt(this DataFrame df, string[] idVars, string[] valueVars,
                                     string varName = "variable", string valueName = "value")
    {
        try
        {
            int originalRowCount = df.Index.Length;
            int newRowCount = originalRowCount * valueVars.Length;

            var resultColumns = new Dictionary<string, IColumn>();

            // 1. ID 변수 처리 (데이터 반복)
            if (idVars != null)
            {
                foreach (var idVar in idVars)
                {
                    if (!df.Columns.Contains(idVar)) continue;

                    var sourceCol = df[idVar];
                    var newValues = new object?[newRowCount];

                    for (int i = 0; i < valueVars.Length; i++)
                    {
                        for (int j = 0; j < originalRowCount; j++)
                        {
                            newValues[i * originalRowCount + j] = sourceCol.GetValue(j);
                        }
                    }
                    resultColumns[idVar] = CreateColumnFromObjects(newValues, sourceCol.DataType);
                }
            }

            // 2. Variable/Value 컬럼 생성
            var variableValues = new string[newRowCount];
            var mainValues = new object?[newRowCount];

            // 타겟 타입 결정 (첫 번째 valueVar 기준)
            var targetType = df[valueVars[0]].DataType;

            for (int i = 0; i < valueVars.Length; i++)
            {
                var colName = valueVars[i];
                var sourceCol = df[colName];

                int offset = i * originalRowCount;

                for (int j = 0; j < originalRowCount; j++)
                {
                    variableValues[offset + j] = colName;
                    mainValues[offset + j] = sourceCol.GetValue(j);
                }
            }

            resultColumns[varName] = new StringColumn(variableValues);
            resultColumns[valueName] = CreateColumnFromObjects(mainValues, targetType);

            return new DataFrame(resultColumns);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Melt operation failed: {ex.Message}", ex);
        }
    }

    private static IColumn CreateColumnFromObjects(object?[] values, Type dataType)
    {
        if (dataType == typeof(int))
        {
            var data = values.Select(v => v == null ? 0 : (int)Convert.ChangeType(v, typeof(int))).ToArray();
            var naMask = values.Select(v => v == null).ToArray();
            return new PrimitiveColumn<int>(data, naMask);
        }
        else if (dataType == typeof(double))
        {
            var data = values.Select(v => v == null ? 0.0 : (double)Convert.ChangeType(v, typeof(double))).ToArray();
            var naMask = values.Select(v => v == null).ToArray();
            return new PrimitiveColumn<double>(data, naMask);
        }
        else if (dataType == typeof(string))
        {
            var data = values.Select(v => v?.ToString()).ToArray();
            return new StringColumn(data);
        }

        // Default to StringColumn if unknown
        return new StringColumn(values.Select(v => v?.ToString()).ToArray());
    }
}