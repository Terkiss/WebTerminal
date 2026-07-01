using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Agg;
using TeruTeruPandas.IO;

using System.Linq;
namespace TeruTeruPandas.Compat;

/// <summary>
/// pandas 메서드명과 동일한 별칭 제공 (확장 메서드)
/// 상위 50개 pandas idiom 완전 대응
/// </summary>
public static class PythonicExtensions
{
    // DataFrame 확장 메서드들

    /// <summary>
    /// df.head(n) - 상위 n개 행 반환
    /// </summary>
    public static DataFrame Head(this DataFrame df, int n = 5)
    {
        return df.Head(n);
    }

    /// <summary>
    /// df.tail(n) - 하위 n개 행 반환
    /// </summary>
    public static DataFrame Tail(this DataFrame df, int n = 5)
    {
        return df.Tail(n);
    }

    /// <summary>
    /// df.dropna() - 결측치가 있는 행 제거
    /// </summary>
    public static DataFrame DropNa(this DataFrame df)
    {
        return df.DropNA();
    }

    /// <summary>
    /// df.fillna(value) - 결측치를 지정된 값으로 채움
    /// </summary>
    public static DataFrame FillNa(this DataFrame df, object value)
    {
        var newColumns = new Dictionary<string, Core.Column.IColumn>();

        foreach (var columnName in df.Columns)
        {
            var column = df[columnName];
            var newColumn = column.Clone();

            for (int i = 0; i < column.Length; i++)
            {
                if (column.IsNA(i))
                {
                    newColumn.SetValue(i, value);
                }
            }

            newColumns[columnName] = newColumn;
        }

        return new DataFrame(newColumns, df.Index);
    }

    /// <summary>
    /// df.describe() - 기술통계 요약
    /// </summary>
    public static DataFrame Describe(this DataFrame df)
    {
        var stats = new Dictionary<string, Core.Column.IColumn>();
        var statNames = new[] { "count", "mean", "std", "min", "25%", "50%", "75%", "max" };

        foreach (var columnName in df.Columns)
        {
            var column = df[columnName];
            if (column is Core.Column.PrimitiveColumn<double> doubleColumn)
            {
                stats[columnName] = CalculateNumericStats(doubleColumn);
            }
            else if (column is Core.Column.PrimitiveColumn<int> intColumn)
            {
                stats[columnName] = CalculateNumericStats(intColumn);
            }
            // 문자열 컬럼은 count만 계산
        }

        return new DataFrame(stats, new Core.Index.StringIndex(statNames));
    }

    /// <summary>
    /// df.info() - DataFrame 정보 출력
    /// </summary>
    public static string Info(this DataFrame df)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<class 'TeruTeruPandas.DataFrame'>");
        sb.AppendLine($"RangeIndex: {df.RowCount} entries, 0 to {df.RowCount - 1}");
        sb.AppendLine($"Data columns (total {df.ColumnCount} columns):");
        sb.AppendLine("Column".PadRight(20) + "Non-Null Count".PadRight(15) + "Dtype");
        sb.AppendLine(new string('-', 50));

        for (int i = 0; i < df.ColumnCount; i++)
        {
            var columnName = df.Columns[i];
            var column = df[columnName];
            var nonNullCount = 0;

            for (int j = 0; j < column.Length; j++)
            {
                if (!column.IsNA(j))
                    nonNullCount++;
            }

            sb.AppendLine($"{i}".PadRight(3) +
                         columnName.PadRight(17) +
                         $"{nonNullCount} non-null".PadRight(15) +
                         column.DataType.Name);
        }

        return sb.ToString();
    }

    /// <summary>
    /// df.shape - DataFrame의 차원 (rows, columns)
    /// </summary>
    public static (int rows, int columns) Shape(this DataFrame df)
    {
        return (df.RowCount, df.ColumnCount);
    }

    /// <summary>
    /// df.groupby(by) - 그룹화
    /// </summary>
    public static SimdGroupBy GroupBy(this DataFrame df, string[] by)
    {
        var columns = new Dictionary<string, Core.Column.IColumn>();
        foreach (var columnName in df.Columns)
        {
            columns[columnName] = df[columnName];
        }

        return new SimdGroupBy(columns, by);
    }

    public static SimdGroupBy GroupBy(this DataFrame df, string by)
    {
        return df.GroupBy(new[] { by });
    }

    /// <summary>
    /// df.sort_values(by) - 값으로 정렬
    /// </summary>
    public static DataFrame SortValues(this DataFrame df, string by, bool ascending = true)
    {
        if (!df.Columns.Contains(by))
            throw new ArgumentException($"Column '{by}' not found");

        var column = df[by];
        var indices = Enumerable.Range(0, df.RowCount).ToArray();

        // 정렬
        Array.Sort(indices, (i, j) =>
        {
            var valueI = column.GetValue(i);
            var valueJ = column.GetValue(j);

            if (valueI == null && valueJ == null) return 0;
            if (valueI == null) return ascending ? -1 : 1;
            if (valueJ == null) return ascending ? 1 : -1;

            var comparison = Comparer<object>.Default.Compare(valueI, valueJ);
            return ascending ? comparison : -comparison;
        });

        // 데이터 재정렬
        var newColumns = new Dictionary<string, Core.Column.IColumn>();
        foreach (var columnName in df.Columns)
        {
            newColumns[columnName] = df[columnName].Reorder(indices);
        }

        return new DataFrame(newColumns, new Core.Index.RangeIndex(df.RowCount));
    }

    /// <summary>
    /// df.drop_duplicates() - 중복 행 제거
    /// </summary>
    public static DataFrame DropDuplicates(this DataFrame df, string[]? subset = null)
    {
        var columnsToCheck = subset ?? df.Columns;
        var seen = new HashSet<string>();
        var uniqueIndices = new List<int>();

        for (int i = 0; i < df.RowCount; i++)
        {
            var rowKey = CreateRowKey(df, i, columnsToCheck);
            if (seen.Add(rowKey))
            {
                uniqueIndices.Add(i);
            }
        }

        if (uniqueIndices.Count == df.RowCount)
            return df;

        var indices = uniqueIndices.ToArray();
        var newColumns = new Dictionary<string, Core.Column.IColumn>();
        foreach (var columnName in df.Columns)
        {
            newColumns[columnName] = df[columnName].Reorder(indices);
        }

        return new DataFrame(newColumns, new Core.Index.RangeIndex(indices.Length));
    }

    /// <summary>
    /// df.query(expr) - 쿼리 표현식으로 필터링
    /// </summary>
    public static DataFrame Query(this DataFrame df, string expression)
    {
        // 간단한 쿼리 파서 (예: "column > 5")
        var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            throw new ArgumentException("Simple query format: 'column operator value'");

        var columnName = parts[0];
        var op = parts[1];
        var valueStr = parts[2];

        if (!df.Columns.Contains(columnName))
            throw new ArgumentException($"Column '{columnName}' not found");

        var column = df[columnName];
        var matchingIndices = new List<int>();

        for (int i = 0; i < df.RowCount; i++)
        {
            if (column.IsNA(i))
                continue;

            var value = column.GetValue(i);
            if (EvaluateCondition(value, op, valueStr))
            {
                matchingIndices.Add(i);
            }
        }

        var indices = matchingIndices.ToArray();
        var newColumns = new Dictionary<string, Core.Column.IColumn>();
        foreach (var colName in df.Columns)
        {
            newColumns[colName] = df[colName].Reorder(indices);
        }

        return new DataFrame(newColumns, new Core.Index.RangeIndex(indices.Length));
    }

    /// <summary>
    /// df.to_csv(path) - CSV 파일로 저장
    /// </summary>
    public static void ToCsv(this DataFrame df, string filePath, bool includeHeader = true)
    {
        CsvWriter.ToCsv(df, filePath, includeHeader);
    }

    /// <summary>
    /// df.to_json(path) - JSON 파일로 저장
    /// </summary>
    public static void ToJson(this DataFrame df, string filePath, bool pretty = false)
    {
        JsonIO.ToJson(df, filePath, pretty);
    }

    // 헬퍼 메서드들
    private static Core.Column.IColumn CalculateNumericStats<T>(Core.Column.PrimitiveColumn<T> column)
        where T : struct, System.Numerics.INumber<T>
    {
        var validValues = new List<T>();
        for (int i = 0; i < column.Length; i++)
        {
            if (!column.IsNA(i))
            {
                validValues.Add((T)column.GetValue(i)!);
            }
        }

        if (validValues.Count() == 0)
        {
            var emptyStats = new double[8];
            return new Core.Column.PrimitiveColumn<double>(emptyStats);
        }

        validValues.Sort();

        var count = validValues.Count();
        var sum = validValues.Aggregate(T.Zero, (acc, val) => acc + val);
        var mean = Convert.ToDouble(sum) / count;

        var variance = validValues
            .Select(val => Math.Pow(Convert.ToDouble(val) - mean, 2))
            .Average();
        var std = Math.Sqrt(variance);

        var min = Convert.ToDouble(validValues[0]);
        var q25 = Convert.ToDouble(validValues[count / 4]);
        var q50 = Convert.ToDouble(validValues[count / 2]);
        var q75 = Convert.ToDouble(validValues[3 * count / 4]);
        var max = Convert.ToDouble(validValues[^1]);

        var stats = new double[] { count, mean, std, min, q25, q50, q75, max };
        return new Core.Column.PrimitiveColumn<double>(stats);
    }

    private static string CreateRowKey(DataFrame df, int rowIndex, string[] columns)
    {
        var keyParts = new List<string>();
        foreach (var columnName in columns)
        {
            var column = df[columnName];
            var value = column.IsNA(rowIndex) ? "NA" : column.GetValue(rowIndex)?.ToString() ?? "NA";
            keyParts.Add(value);
        }
        return string.Join("|", keyParts);
    }

    private static bool EvaluateCondition(object? value, string op, string valueStr)
    {
        if (value == null)
            return false;

        // 숫자 비교
        if (double.TryParse(value.ToString(), out double numValue) &&
            double.TryParse(valueStr, out double targetValue))
        {
            return op switch
            {
                ">" => numValue > targetValue,
                "<" => numValue < targetValue,
                ">=" => numValue >= targetValue,
                "<=" => numValue <= targetValue,
                "==" => Math.Abs(numValue - targetValue) < 1e-10,
                "!=" => Math.Abs(numValue - targetValue) >= 1e-10,
                _ => false
            };
        }

        // 문자열 비교
        var strValue = value.ToString() ?? "";
        return op switch
        {
            "==" => strValue == valueStr,
            "!=" => strValue != valueStr,
            _ => false
        };
    }
}

/// <summary>
/// 정적 팩토리 메서드들 (pandas 스타일)
/// </summary>
public static class Pd
{
    /// <summary>
    /// pd.read_csv(path) - CSV 파일 읽기
    /// </summary>
    public static DataFrame ReadCsv(string filePath, bool hasHeader = true, char separator = ',')
    {
        return CsvReader.ReadCsv(filePath, hasHeader, separator);
    }

    /// <summary>
    /// pd.read_json(path) - JSON 파일 읽기
    /// </summary>
    public static DataFrame ReadJson(string filePath, bool isJsonLines = false)
    {
        return JsonIO.ReadJson(filePath, isJsonLines);
    }

    /// <summary>
    /// pd.DataFrame(data) - DataFrame 생성
    /// </summary>
    public static DataFrame DataFrame(Dictionary<string, object[]> data)
    {
        var columns = new Dictionary<string, Core.Column.IColumn>();

        foreach (var kvp in data)
        {
            var columnName = kvp.Key;
            var values = kvp.Value;

            // 첫 번째 비null 값으로 타입 판단
            var firstValue = values.FirstOrDefault(v => v != null);
            if (firstValue is int)
            {
                var intData = values.Select(x => x == null ? 0 : Convert.ToInt32(x)).ToArray();
                var naMask = values.Select(x => x == null).ToArray();
                columns[columnName] = new Core.Column.PrimitiveColumn<int>(intData, naMask);
            }
            else if (firstValue is long)
            {
                var longData = values.Select(x => x == null ? 0L : Convert.ToInt64(x)).ToArray();
                var naMask = values.Select(x => x == null).ToArray();
                columns[columnName] = new Core.Column.PrimitiveColumn<long>(longData, naMask);
            }
            else if (firstValue is double)
            {
                var doubleData = values.Select(x => x == null ? 0.0 : Convert.ToDouble(x)).ToArray();
                var naMask = values.Select(x => x == null).ToArray();
                columns[columnName] = new Core.Column.PrimitiveColumn<double>(doubleData, naMask);
            }
            else if (firstValue is DateTime)
            {
                var dateData = values.Select(x => x == null ? default(DateTime) : Convert.ToDateTime(x)).ToArray();
                var naMask = values.Select(x => x == null).ToArray();
                columns[columnName] = new Core.Column.PrimitiveColumn<DateTime>(dateData, naMask);
            }
            else if (firstValue is bool)
            {
                var boolData = values.Select(x => x == null ? false : Convert.ToBoolean(x)).ToArray();
                var naMask = values.Select(x => x == null).ToArray();
                columns[columnName] = new Core.Column.PrimitiveColumn<bool>(boolData, naMask);
            }
            else
            {
                var stringData = values.Select(x => x?.ToString()).ToArray();
                var naMask = values.Select(x => x == null).ToArray();
                columns[columnName] = new Core.Column.StringColumn(stringData, naMask);
            }
        }

        return new Core.DataFrame(columns);
    }

    public static DataFrame DataFrame(Dictionary<string, int[]> data)
    {
        var columns = new Dictionary<string, Core.Column.IColumn>();

        foreach (var kvp in data)
        {
            var columnName = kvp.Key;
            var values = kvp.Value;
            columns[columnName] = new Core.Column.PrimitiveColumn<int>(values);
        }

        return new Core.DataFrame(columns);
    }


    /// <summary>
    /// pd.concat(dataframes, axis) - DataFrame 연결
    /// </summary>
    public static DataFrame Concat(IEnumerable<DataFrame> dataframes, int axis = 0)
    {
        return DataFrameJoinExtensions.Concat(dataframes, axis);
    }
}