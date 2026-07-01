using System.Linq;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;
using TeruTeruPandas.Core.Agg;
using System.Text;
using System;

namespace TeruTeruPandas.Core;

/// <summary>
/// 컬럼(Column)들을 모아 구축된 2차원 데이터 구조체. 
/// 열 단위(Columnar) 저장 방식을 택해 캐시 지역성(Cache Locality)을 극대화하며,
/// 내부적으로 `ArrayPool` 기반의 `PrimitiveColumn`을 사용하여 대형 데이터셋 처리 시
/// 가비지 컬렉터(GC) 부하 및 메모리 스트레스를 최소화(Zero-Allocation 지향)합니다.
/// 데이터프레임은 행(Row)보다는 열(Column) 중심 연산에 최적화되어 있습니다.
/// </summary>
public class DataFrame : IDisposable
{
    private readonly Dictionary<string, IColumn> _columns;
    private readonly Index.Index _index;
    private readonly List<string> _columnNames;
    private bool _disposed;

    /// <summary>
    /// 데이터프레임의 행 인덱스 정보를 가져옵니다.
    /// </summary>
    public Index.Index Index => _index;

    /// <summary>
    /// 모든 컬럼 이름들의 배열을 가져옵니다.
    /// </summary>
    public string[] Columns => _columnNames.ToArray();

    /// <summary>
    /// 전체 행(Row)의 개수를 반환합니다.
    /// </summary>
    public int RowCount => _index.Length;

    /// <summary>
    /// 전체 열(Column)의 개수를 반환합니다.
    /// </summary>
    public int ColumnCount => _columnNames.Count;

    /// <summary>
    /// 전체 요소의 개수 (행 * 열)
    /// </summary>
    public int Size => RowCount * ColumnCount;

    /// <summary>
    /// 데이터프레임이 비어있는지 여부를 반환합니다.
    /// </summary>
    public bool Empty => RowCount == 0;

    /// <summary>
    /// 전체 데이터를 2차원 object 배열로 반환합니다. 
    /// (주의: 행 단위로 메모리를 재배치하므로 데이터가 클 경우 성능 저하가 있을 수 있습니다.)
    /// </summary>
    public object?[,] Values
    {
        get
        {
            var result = new object?[RowCount, ColumnCount];
            for (int row = 0; row < RowCount; row++)
            {
                for (int col = 0; col < ColumnCount; col++)
                {
                    result[row, col] = _columns[_columnNames[col]].GetValue(row);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 각 컬럼의 데이터 타입을 이름별로 맵핑한 사전을 반환합니다.
    /// </summary>
    public Dictionary<string, Type> Dtypes
    {
        get
        {
            var types = new Dictionary<string, Type>();
            foreach (var columnName in _columnNames)
            {
                types[columnName] = _columns[columnName].DataType;
            }
            return types;
        }
    }

    /// <summary>
    /// 사전(Dictionary) 파편들을 취합하여 새로운 DataFrame 객체를 구성합니다.
    /// 모든 컬럼들은 동일한 길이(RowCount)를 가져야 합니다.
    /// </summary>
    /// <param name="columns">컬럼 이름과 데이터가 맵핑된 컬렉션</param>
    /// <param name="index">옵션 인덱스. 지정하지 않으면 0부터 시작하는 순차 인덱스가 할당됩니다.</param>
    public DataFrame(Dictionary<string, IColumn> columns, Index.Index? index = null)
    {
        if (columns.Count == 0)
            throw new ArgumentException("DataFrame must have at least one column");

        var firstColumn = columns.Values.First();
        var rowCount = firstColumn.Length;

        // 모든 컬럼의 길이가 동일한지 확인 (정합성 검사)
        foreach (var column in columns.Values)
        {
            if (column.Length != rowCount)
                throw new ArgumentException("All columns must have the same length");
        }

        _columns = columns;
        _columnNames = columns.Keys.ToList();
        _index = index ?? new RangeIndex(rowCount);

        if (_index.Length != rowCount)
            throw new ArgumentException("Index length must match column length");
    }

    /// <summary>
    /// 이름으로 특정 컬럼에 접근합니다.
    /// </summary>
    public IColumn this[string columnName]
    {
        get
        {
            if (!_columns.TryGetValue(columnName, out var column))
                throw new KeyNotFoundException($"Column '{columnName}' not found");
            return column;
        }
        set
        {
            if (value.Length != RowCount)
                throw new ArgumentException("Column length must match DataFrame row count");
            _columns[columnName] = value;
        }
    }

    /// <summary>
    /// 행 위치(index)와 컬럼 이름으로 개별 셀 값에 접근합니다.
    /// </summary>
    public object? this[int row, string column]
    {
        get => this[column].GetValue(row);
        set => this[column].SetValue(row, value);
    }

    /// <summary>
    /// 행 라벨(key)과 컬럼 이름으로 개별 셀 값에 접근합니다.
    /// </summary>
    public object? this[object rowKey, string column]
    {
        get
        {
            int position = _index.GetPosition(rowKey);
            if (position < 0)
                throw new KeyNotFoundException($"Row key '{rowKey}' not found in index");
            return this[column].GetValue(position);
        }
        set
        {
            int position = _index.GetPosition(rowKey);
            if (position < 0)
                throw new KeyNotFoundException($"Row key '{rowKey}' not found in index");
            this[column].SetValue(position, value);
        }
    }

    /// <summary>
    /// 불린 시리즈(Mask)를 사용하여 조건에 맞는 행들만 필터링한 새로운 DataFrame을 생성합니다.
    /// </summary>
    public DataFrame this[BoolSeries mask]
    {
        get
        {
            if (mask.Length != RowCount)
                throw new ArgumentException("Boolean mask length must match DataFrame row count");

            // True인 인덱스 위치들을 추출
            var indices = mask.GetTrueIndices();
            return Reorder(indices);
        }
    }

    /// <summary>
    /// 주어진 인덱스 순서대로 행을 재배치하여 새로운 DataFrame을 생성합니다. (필터링 및 정렬에 사용)
    /// </summary>
    public DataFrame Reorder(int[] indices)
    {
        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            // 각 컬럼별로 고속 재배치 수행
            newColumns[columnName] = _columns[columnName].Reorder(indices);
        }

        var newIndex = _index.Reorder(indices);
        return new DataFrame(newColumns, newIndex);
    }

    /// <summary>
    /// 모든 데이터를 비운 빈 데이터프레임을 반환합니다.
    /// </summary>
    public DataFrame Clear()
    {
        return Reorder(new int[0]);
    }

    /// <summary>
    /// 라벨 기반 인덱서(Pandas 스타일)
    /// </summary>
    public DataFrameLocIndexer Loc => new(this);

    /// <summary>
    /// 라벨 기반 단일 값 접근. .loc보다 빠릅니다.
    /// </summary>
    public object? At(object rowKey, string columnName)
    {
        if (!_columns.ContainsKey(columnName))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        int rowIndex = _index.GetPosition(rowKey);
        if (rowIndex < 0)
            throw new KeyNotFoundException($"Row key '{rowKey}' not found in index");

        return _columns[columnName].GetValue(rowIndex);
    }

    /// <summary>
    /// 정수 위치 기반 단일 값 접근. .iloc보다 빠릅니다.
    /// </summary>
    public object? Iat(int row, int column)
    {
        if (row < 0 || row >= RowCount)
            throw new IndexOutOfRangeException($"Row index {row} out of range");

        if (column < 0 || column >= ColumnCount)
            throw new IndexOutOfRangeException($"Column index {column} out of range");

        return _columns[_columnNames[column]].GetValue(row);
    }

    /// <summary>
    /// 라벨 기반 단일 값 설정
    /// </summary>
    public void SetAt(object rowKey, string columnName, object value)
    {
        if (!_columns.ContainsKey(columnName))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        int rowIndex = _index.GetPosition(rowKey);
        if (rowIndex < 0)
            throw new KeyNotFoundException($"Row key '{rowKey}' not found in index");

        _columns[columnName].SetValue(rowIndex, value);
    }

    /// <summary>
    /// 정수 위치 기반 단일 값 설정
    /// </summary>
    public void SetIat(int row, int column, object value)
    {
        if (row < 0 || row >= RowCount)
            throw new IndexOutOfRangeException($"Row index {row} out of range");

        if (column < 0 || column >= ColumnCount)
            throw new IndexOutOfRangeException($"Column index {column} out of range");

        _columns[_columnNames[column]].SetValue(row, value);
    }

    /// <summary>
    /// 위치 기반 인덱서 (Pandas 스타일)
    /// </summary>
    public DataFrameILocIndexer ILoc => new(this);

    /// <summary>
    /// 새로운 컬럼을 추가합니다. 기존 컬럼과 행 길이가 같아야 합니다.
    /// </summary>
    public void AddColumn(string name, IColumn column)
    {
        if (column.Length != RowCount)
            throw new ArgumentException("Column length must match DataFrame row count");

        if (_columns.ContainsKey(name))
        {
            _columns[name] = column;
            return;
        }

        _columns[name] = column;
        _columnNames.Add(name);
    }

    /// <summary>
    /// 특정 컬럼을 제거합니다.
    /// </summary>
    public void DropColumn(string name)
    {
        if (!_columns.ContainsKey(name))
            throw new KeyNotFoundException($"Column '{name}' not found");

        _columns.Remove(name);
        _columnNames.Remove(name);
    }

    /// <summary>
    /// 상위 n개 행을 반환합니다.
    /// </summary>
    public DataFrame Head(int n = 5)
    {
        int count = Math.Min(n, RowCount);
        var slicedColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            slicedColumns[columnName] = _columns[columnName].Slice(0, count);
        }

        var slicedIndex = _index.Slice(0, count);
        return new DataFrame(slicedColumns, slicedIndex);
    }

    /// <summary>
    /// 하위 n개 행을 반환합니다.
    /// </summary>
    public DataFrame Tail(int n = 5)
    {
        int count = Math.Min(n, RowCount);
        int start = RowCount - count;
        var slicedColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            slicedColumns[columnName] = _columns[columnName].Slice(start, count);
        }

        var slicedIndex = _index.Slice(start, count);
        return new DataFrame(slicedColumns, slicedIndex);
    }

    /// <summary>
    /// 결측치(NA)가 포함된 행을 제거합니다.
    /// </summary>
    /// <param name="how">"any": 하나라도 NA면 제거, "all": 모든 컬럼이 NA면 제거</param>
    /// <param name="thresh">정상 데이터 개수가 이 값보다 작으면 제거 (how보다 우선순위가 높음)</param>
    public DataFrame DropNA(string how = "any", int? thresh = null)
    {
        var validRows = new List<int>();

        for (int i = 0; i < RowCount; i++)
        {
            int naCount = 0;
            foreach (var columnName in _columnNames)
            {
                if (_columns[columnName].IsNA(i))
                    naCount++;
            }

            int nonNaCount = ColumnCount - naCount;
            bool keep = false;

            if (thresh.HasValue)
            {
                keep = nonNaCount >= thresh.Value;
            }
            else
            {
                if (how == "any")
                {
                    keep = naCount == 0;
                }
                else if (how == "all")
                {
                    keep = nonNaCount > 0;
                }
                else
                {
                    throw new ArgumentException("how must be 'any' or 'all'");
                }
            }

            if (keep)
                validRows.Add(i);
        }

        if (validRows.Count == RowCount)
            return this;

        var indices = validRows.ToArray();
        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            newColumns[columnName] = _columns[columnName].Reorder(indices);
        }

        var newIndex = _index.Reorder(indices);
        return new DataFrame(newColumns, newIndex);
    }

    /// <summary>
    /// 결측치를 특정 값으로 채웁니다.
    /// </summary>
    public DataFrame FillNA(object? value)
    {
        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            newColumns[columnName] = _columns[columnName].FillNA(value);
        }
        return new DataFrame(newColumns, _index);
    }

    /// <summary>
    /// 결측치를 특정 방식(ffill, bfill 등)으로 채웁니다.
    /// </summary>
    public DataFrame FillNA(string method)
    {
        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            newColumns[columnName] = _columns[columnName].FillNA(method);
        }
        return new DataFrame(newColumns, _index);
    }

    /// <summary>
    /// 특정 컬럼의 값을 기준으로 행을 정렬합니다.
    /// </summary>
    public DataFrame SortValues(string by, bool ascending = true)
    {
        if (!_columns.ContainsKey(by))
            throw new ArgumentException($"Column '{by}' not found");

        var indices = _columns[by].Argsort(ascending);

        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            newColumns[columnName] = _columns[columnName].Reorder(indices);
        }

        var newIndex = _index.Reorder(indices);

        return new DataFrame(newColumns, newIndex);
    }

    /// <summary>
    /// 인덱스 값을 기준으로 행을 정렬합니다.
    /// </summary>
    public DataFrame SortIndex(bool ascending = true)
    {
        var indices = _index.Argsort(ascending);

        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            newColumns[columnName] = _columns[columnName].Reorder(indices);
        }

        var newIndex = _index.Reorder(indices);

        return new DataFrame(newColumns, newIndex);
    }

    /// <summary>
    /// 데이터프레임의 내용을 문자열 표 형태로 출력합니다. (디버깅용)
    /// </summary>
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();

        // 헤더
        sb.Append("Index".PadRight(10));
        foreach (var column in _columnNames)
        {
            sb.Append(column.PadRight(15));
        }
        sb.AppendLine();

        // 데이터 (최대 10행만 표시)
        int displayRows = Math.Min(RowCount, 10);
        for (int i = 0; i < displayRows; i++)
        {
            var indexValue = _index.GetValue(i).ToString();
            sb.Append(indexValue!.PadRight(10));

            foreach (var columnName in _columnNames)
            {
                var value = _columns[columnName].IsNA(i) ? "NaN" :
                           _columns[columnName].GetValue(i)?.ToString() ?? "null";
                sb.Append(value.PadRight(15));
            }
            sb.AppendLine();
        }

        if (RowCount > 10)
        {
            sb.AppendLine("...");
        }

        sb.AppendLine($"[{RowCount} rows x {ColumnCount} columns]");
        return sb.ToString();
    }

    /// <summary>
    /// 데이터프레임의 요약 정보(인덱스 타입, 컬럼 정보, 비결측치 수, 타입, 메모리 사용량)를 출력합니다.
    /// </summary>
    public void Info(StringBuilder? buffer = null)
    {
        var output = buffer ?? new StringBuilder();

        output.AppendLine($"<class 'TeruTeruPandas.DataFrame'>");
        output.AppendLine($"RangeIndex: {RowCount} entries, 0 to {RowCount - 1}");
        output.AppendLine($"Data columns (total {ColumnCount} columns):");
        output.AppendLine("#   Column  Non-Null Count  Dtype");
        output.AppendLine("---  ------  --------------  -----");

        for (int i = 0; i < ColumnCount; i++)
        {
            var columnName = _columnNames[i];
            var column = _columns[columnName];
            var nonNullCount = 0;

            for (int j = 0; j < RowCount; j++)
            {
                var value = column.GetValue(j);
                if (value != null && !value.Equals(DBNull.Value))
                    nonNullCount++;
            }

            output.AppendLine($"{i,-3} {columnName,-8} {nonNullCount} non-null      {column.DataType.Name}");
        }

        output.AppendLine($"dtypes: {GetDtypeSummary()}");
        output.AppendLine($"memory usage: {EstimateMemoryUsage()} bytes");

        if (buffer == null)
        {
            Console.WriteLine(output.ToString());
        }
    }

    private string GetDtypeSummary()
    {
        var typeCounts = new Dictionary<string, int>();
        foreach (var columnName in _columnNames)
        {
            var column = _columns[columnName];
            var typeName = column.DataType.Name;
            typeCounts[typeName] = typeCounts.GetValueOrDefault(typeName, 0) + 1;
        }

        return string.Join(", ", typeCounts.Select(kv => $"{kv.Key}({kv.Value})"));
    }

    /// <summary>
    /// 데이터프레임의 예상 메모리 점유량을 계산합니다.
    /// </summary>
    private long EstimateMemoryUsage()
    {
        long totalBytes = 0;
        foreach (var columnName in _columnNames)
        {
            totalBytes += RowCount * GetTypeSize(_columns[columnName].DataType);
        }
        return totalBytes;
    }

    private int GetTypeSize(Type type)
    {
        if (type == typeof(int)) return 4;
        if (type == typeof(double)) return 8;
        if (type == typeof(string)) return 50; // 추정치
        if (type == typeof(DateTime)) return 8;
        if (type == typeof(bool)) return 1;
        return 8; // 기본값
    }

    /// <summary>
    /// 수치형 컬럼들에 대한 요약 통계량(count, mean, std, min, 25%, 50%, 75%, max)을 계산하여 새로운 DataFrame으로 반환합니다.
    /// </summary>
    public DataFrame Describe()
    {
        var numericColumns = _columnNames
            .Where(name => IsNumericType(_columns[name].DataType))
            .ToList();

        if (!numericColumns.Any())
        {
            throw new InvalidOperationException("No numeric columns to describe");
        }

        var stats = new string[] { "count", "mean", "std", "min", "25%", "50%", "75%", "max" };
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in numericColumns)
        {
            var values = GetNumericValues(columnName);
            var columnStats = new List<object>
            {
                (double)values.Count(),
                values.Average(),
                CalculateStandardDeviation(values),
                values.Min(),
                CalculateQuantile(values, 0.25),
                CalculateQuantile(values, 0.5),
                CalculateQuantile(values, 0.75),
                values.Max()
            };

            resultColumns[columnName] = CreateColumn(columnStats.ToArray(), typeof(double));
        }

        var resultIndex = new RangeIndex(stats.Length);
        return new DataFrame(resultColumns, resultIndex);
    }

    /// <summary>
    /// 각 컬럼의 표준편차를 계산합니다.
    /// </summary>
    public Series<double> Std()
    {
        return ApplyStatisticFunction("std", values => CalculateStandardDeviation(values));
    }

    /// <summary>
    /// 각 컬럼의 분산을 계산합니다.
    /// </summary>
    public Series<double> Var()
    {
        return ApplyStatisticFunction("var", values =>
        {
            if (values.Count() <= 1) return 0;
            var mean = values.Average();
            return values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count() - 1);
        });
    }

    /// <summary>
    /// 각 컬럼의 중앙값을 계산합니다.
    /// </summary>
    public Series<double> Median()
    {
        return ApplyStatisticFunction("median", values => CalculateQuantile(values, 0.5));
    }

    /// <summary>
    /// 각 컬럼의 최솟값을 계산합니다.
    /// </summary>
    public Series<double> Min()
    {
        return ApplyStatisticFunction("min", values => values.Min());
    }

    /// <summary>
    /// 각 컬럼의 최댓값을 계산합니다.
    /// </summary>
    public Series<double> Max()
    {
        return ApplyStatisticFunction("max", values => values.Max());
    }

    /// <summary>
    /// 각 컬럼의 백분위수(Quantile)를 계산합니다. (q: 0.0 ~ 1.0)
    /// </summary>
    public Series<double> Quantile(double q)
    {
        return ApplyStatisticFunction($"quantile({q})", values => CalculateQuantile(values, q));
    }

    private Series<double> ApplyStatisticFunction(string statName, Func<List<double>, double> statFunc)
    {
        var numericColumns = _columnNames
            .Where(name => IsNumericType(_columns[name].DataType))
            .ToList();

        var resultData = new double[numericColumns.Count];
        var resultIndex = new string[numericColumns.Count];

        int i = 0;
        foreach (var columnName in numericColumns)
        {
            resultIndex[i] = columnName;
            var values = GetNumericValues(columnName);
            resultData[i] = values.Any() ? statFunc(values) : double.NaN;
            i++;
        }

        return new Series<double>(resultData, new StringIndex(resultIndex), statName);
    }

    /// <summary>
    /// 결측치 여부를 불린 값으로 반환하는 동일한 크기의 DataFrame을 생성합니다.
    /// </summary>
    public DataFrame IsNa()
    {
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            var column = _columns[columnName];
            var isNaValues = new object[RowCount];

            for (int i = 0; i < RowCount; i++)
            {
                var value = column.GetValue(i);
                isNaValues[i] = value == null || value.Equals(DBNull.Value);
            }

            resultColumns[columnName] = CreateColumn(isNaValues, typeof(bool));
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 결측치가 아닌 요소들을 불린 값으로 반환하는 동일한 크기의 DataFrame을 생성합니다.
    /// </summary>
    public DataFrame NotNa()
    {
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            var column = _columns[columnName];
            var notNaValues = new object[RowCount];

            for (int i = 0; i < RowCount; i++)
            {
                var value = column.GetValue(i);
                notNaValues[i] = value != null && !value.Equals(DBNull.Value);
            }

            resultColumns[columnName] = CreateColumn(notNaValues, typeof(bool));
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 특정 값을 다른 값으로 치환합니다.
    /// </summary>
    public DataFrame Replace(object toReplace, object value)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            var column = _columns[columnName];
            var replacedValues = new object?[RowCount];

            for (int i = 0; i < RowCount; i++)
            {
                var currentValue = column.GetValue(i);
                replacedValues[i] = Equals(currentValue, toReplace) ? value : currentValue;
            }

            resultColumns[columnName] = CreateColumn(replacedValues, column.DataType);
        }

        return new DataFrame(resultColumns, _index);
    }

    private static IColumn CreateColumn(object?[] data, Type dataType)
    {
        if (dataType == typeof(int))
        {
            var intData = data.Select(x => x == null ? 0 : Convert.ToInt32(x)).ToArray();
            return new PrimitiveColumn<int>(intData);
        }
        else if (dataType == typeof(double))
        {
            var doubleData = data.Select(x => x == null ? double.NaN : Convert.ToDouble(x)).ToArray();
            return new PrimitiveColumn<double>(doubleData);
        }
        else if (dataType == typeof(bool))
        {
            var boolData = data.Select(x => x == null ? false : Convert.ToBoolean(x)).ToArray();
            return new PrimitiveColumn<bool>(boolData);
        }
        else if (dataType == typeof(string))
        {
            var stringData = data.Select(x => x?.ToString() ?? string.Empty).ToArray();
            return new StringColumn(stringData);
        }
        else if (dataType == typeof(float))
        {
            var floatData = data.Select(x => x == null ? float.NaN : Convert.ToSingle(x)).ToArray();
            return new PrimitiveColumn<float>(floatData);
        }
        else if (dataType == typeof(long))
        {
            var longData = data.Select(x => x == null ? 0L : Convert.ToInt64(x)).ToArray();
            return new PrimitiveColumn<long>(longData);
        }
        else if (dataType == typeof(DateTime))
        {
            var dateData = data.Select(x => x == null ? default(DateTime) : Convert.ToDateTime(x)).ToArray();
            return new PrimitiveColumn<DateTime>(dateData);
        }
        else
        {
            var stringData = data.Select(x => x?.ToString() ?? string.Empty).ToArray();
            return new StringColumn(stringData);
        }
    }

    /// <summary>
    /// 나머지(Modulus) 연산을 수행합니다.
    /// </summary>
    public DataFrame Mod(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf)
        {
            return PerformBinaryOp(otherDf, "Mod", fillValue);
        }
        else
        {
            return PerformBinaryOpScalar(other, "Mod", fillValue);
        }
    }

    /// <summary>
    /// 덧셈(Addition) 연산을 수행합니다.
    /// </summary>
    public DataFrame Add(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Add", fillValue);
        return PerformBinaryOpScalar(other, "Add", fillValue);
    }

    /// <summary>
    /// 뺄셈(Subtraction) 연산을 수행합니다.
    /// </summary>
    public DataFrame Sub(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Sub", fillValue);
        return PerformBinaryOpScalar(other, "Sub", fillValue);
    }

    /// <summary>
    /// 곱셈(Multiplication) 연산을 수행합니다.
    /// </summary>
    public DataFrame Mul(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Mul", fillValue);
        return PerformBinaryOpScalar(other, "Mul", fillValue);
    }

    /// <summary>
    /// 나눗셈(Division) 연산을 수행합니다.
    /// </summary>
    public DataFrame Div(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Div", fillValue);
        return PerformBinaryOpScalar(other, "Div", fillValue);
    }

    /// <summary>
    /// 거듭제곱(Power) 연산을 수행합니다.
    /// </summary>
    public DataFrame Pow(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Pow", fillValue);
        return PerformBinaryOpScalar(other, "Pow", fillValue);
    }

    /// <summary>
    /// 동등 비교(Equal) 연산을 수행합니다.
    /// </summary>
    public DataFrame Eq(object other, int axis = 1)
    {
        if (other is DataFrame otherDf)
        {
            return ComparisonOperation(otherDf, (a, b) => Equals(a, b));
        }
        else
        {
            return ComparisonOperationWithScalar(other, (a, b) => Equals(a, b));
        }
    }

    /// <summary>
    /// 비동등 비교(Not Equal) 연산을 수행합니다.
    /// </summary>
    public DataFrame Ne(object other, int axis = 1)
    {
        if (other is DataFrame otherDf)
        {
            return ComparisonOperation(otherDf, (a, b) => !Equals(a, b));
        }
        else
        {
            return ComparisonOperationWithScalar(other, (a, b) => !Equals(a, b));
        }
    }

    /// <summary>
    /// 작음 비교(Less Than) 연산을 수행합니다.
    /// </summary>
    public DataFrame Lt(object other, int axis = 1)
    {
        if (other is DataFrame otherDf)
        {
            return ComparisonOperation(otherDf, (a, b) => CompareValues(a, b) < 0);
        }
        else
        {
            return ComparisonOperationWithScalar(other, (a, b) => CompareValues(a, b) < 0);
        }
    }

    /// <summary>
    /// 작거나 같음 비교(Less or Equal) 연산을 수행합니다.
    /// </summary>
    public DataFrame Le(object other, int axis = 1)
    {
        if (other is DataFrame otherDf)
        {
            return ComparisonOperation(otherDf, (a, b) => CompareValues(a, b) <= 0);
        }
        else
        {
            return ComparisonOperationWithScalar(other, (a, b) => CompareValues(a, b) <= 0);
        }
    }

    /// <summary>
    /// 큼 비교(Greater Than) 연산을 수행합니다.
    /// </summary>
    public DataFrame Gt(object other, int axis = 1)
    {
        if (other is DataFrame otherDf)
        {
            return ComparisonOperation(otherDf, (a, b) => CompareValues(a, b) > 0);
        }
        else
        {
            return ComparisonOperationWithScalar(other, (a, b) => CompareValues(a, b) > 0);
        }
    }

    /// <summary>
    /// 크거나 같음 비교(Greater or Equal) 연산을 수행합니다.
    /// </summary>
    public DataFrame Ge(object other, int axis = 1)
    {
        if (other is DataFrame otherDf)
        {
            return ComparisonOperation(otherDf, (a, b) => CompareValues(a, b) >= 0);
        }
        else
        {
            return ComparisonOperationWithScalar(other, (a, b) => CompareValues(a, b) >= 0);
        }
    }

    // --- 이항 연산 최적화 헬퍼 ---

    private DataFrame PerformBinaryOp(DataFrame other, string opName, object? fillValue)
    {
        // 현재 버전에서는 인덱스 정렬(Alignment) 없이 행 길이가 같아야 합니다.
        if (this.RowCount != other.RowCount)
        {
            throw new NotSupportedException("DataFrame lengths must match for arithmetic operations in this version.");
        }

        var resultColumns = new Dictionary<string, IColumn>();
        var allColumns = _columnNames.Union(other._columnNames).ToList();

        foreach (var columnName in allColumns)
        {
            IColumn? left = _columns.ContainsKey(columnName) ? _columns[columnName] : null;
            IColumn? right = other._columns.ContainsKey(columnName) ? other._columns[columnName] : null;

            if (left == null && right == null) continue;

            // 한쪽 컬럼이 없는 경우 fillValue를 상수로 채워서 연산 수행
            if (left == null)
            {
                if (fillValue != null)
                {
                    left = CreateConstantColumn(right!.Length, fillValue, right.DataType);
                }
                else
                {
                    resultColumns[columnName] = CreateConstantColumn(this.RowCount, null, typeof(object));
                    continue;
                }
            }

            if (right == null)
            {
                if (fillValue != null)
                {
                    right = CreateConstantColumn(left!.Length, fillValue, left.DataType);
                }
                else
                {
                    resultColumns[columnName] = CreateConstantColumn(this.RowCount, null, typeof(object));
                    continue;
                }
            }

            // 결측치가 있을 경우 fillValue로 대체하여 계산 (Broadcasting 유사 처리)
            if (fillValue != null)
            {
                left = left!.FillNA(fillValue);
                right = right!.FillNA(fillValue);
            }

            // IColumn 수준에서 SIMD 가속 연산 호출
            resultColumns[columnName] = opName switch
            {
                "Add" => left!.Add(right!),
                "Sub" => left!.Sub(right!),
                "Mul" => left!.Mul(right!),
                "Div" => left!.Div(right!),
                "Mod" => left!.Mod(right!),
                "Pow" => left!.Pow(right!),
                _ => throw new NotSupportedException(opName)
            };
        }

        return new DataFrame(resultColumns, _index);
    }

    private DataFrame PerformBinaryOpScalar(object scalar, string opName, object? fillValue)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            var col = _columns[columnName];
            if (fillValue != null) col = col.FillNA(fillValue);

            // 스칼라 연산은 Broadcasting에 의해 모든 행에 일괄 적용됩니다.
            resultColumns[columnName] = opName switch
            {
                "Add" => col.Add(scalar),
                "Sub" => col.Sub(scalar),
                "Mul" => col.Mul(scalar),
                "Div" => col.Div(scalar),
                "Mod" => col.Mod(scalar),
                "Pow" => col.Pow(scalar),
                _ => throw new NotSupportedException(opName)
            };
        }
        return new DataFrame(resultColumns, _index);
    }

    private DataFrame ComparisonOperation(DataFrame other, Func<object, object, bool> comparison)
    {
        var resultColumns = new Dictionary<string, IColumn>();
        var allColumns = _columnNames.Union(other._columnNames).ToList();

        foreach (var columnName in allColumns)
        {
            var leftColumn = _columns.ContainsKey(columnName) ? _columns[columnName] : null;
            var rightColumn = other._columns.ContainsKey(columnName) ? other._columns[columnName] : null;

            var maxLength = Math.Max(leftColumn?.Length ?? 0, rightColumn?.Length ?? 0);
            var resultValues = new bool[maxLength];

            for (int i = 0; i < maxLength; i++)
            {
                var leftValue = leftColumn?.GetValue(i);
                var rightValue = rightColumn?.GetValue(i);

                resultValues[i] = comparison(leftValue ?? DBNull.Value, rightValue ?? DBNull.Value);
            }

            resultColumns[columnName] = new PrimitiveColumn<bool>(resultValues);
        }

        var resultIndex = new RangeIndex(resultColumns.Values.First().Length);
        return new DataFrame(resultColumns, resultIndex);
    }

    private DataFrame ComparisonOperationWithScalar(object scalar, Func<object, object, bool> comparison)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            var column = _columns[columnName];
            var resultValues = new bool[column.Length];

            for (int i = 0; i < column.Length; i++)
            {
                var value = column.GetValue(i);
                resultValues[i] = comparison(value ?? DBNull.Value, scalar);
            }

            resultColumns[columnName] = new PrimitiveColumn<bool>(resultValues);
        }

        return new DataFrame(resultColumns, _index);
    }

    private IColumn CreateConstantColumn(int length, object? value, Type hintType)
    {
        if (value == null) return new PrimitiveColumn<double>(length);

        var type = value.GetType();
        if (type == typeof(int)) return new PrimitiveColumn<int>(Enumerable.Repeat((int)value, length).ToArray());
        if (type == typeof(double)) return new PrimitiveColumn<double>(Enumerable.Repeat((double)value, length).ToArray());
        if (type == typeof(float)) return new PrimitiveColumn<float>(Enumerable.Repeat((float)value, length).ToArray());
        if (type == typeof(long)) return new PrimitiveColumn<long>(Enumerable.Repeat((long)value, length).ToArray());
        if (type == typeof(string))
        {
            var arr = new string[length]; Array.Fill(arr, (string)value);
            return new StringColumn(arr);
        }

        throw new NotSupportedException($"Constant column of type {type} not supported yet.");
    }

    private static int CompareValues(object a, object b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        try
        {
            if (a is IComparable comparable)
                return comparable.CompareTo(b);

            return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }
        catch
        {
            return 0;
        }
    }

    // --- 통계 연산 ---

    private Series<double> ComputeStat(Func<IColumn, double> statFunc)
    {
        var resultData = new double[Columns.Length];
        var resultIndex = new string[Columns.Length];
        int i = 0;
        foreach (var colName in Columns)
        {
            resultIndex[i] = colName;
            try
            {
                resultData[i] = statFunc(_columns[colName]);
            }
            catch (NotSupportedException)
            {
                resultData[i] = double.NaN;
            }
            i++;
        }
        return new Series<double>(resultData, new StringIndex(resultIndex));
    }

    /// <summary>
    /// 열별 평균을 구합니다. (axis 1은 현재 지원되지 않음)
    /// </summary>
    public Series<double> Mean(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Mean());
    }

    /// <summary>
    /// 열별 합계를 구합니다.
    /// </summary>
    public Series<double> Sum(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Sum());
    }

    /// <summary>
    /// 열별 중앙값을 구합니다.
    /// </summary>
    public Series<double> Median(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Median());
    }

    /// <summary>
    /// 열별 분산을 구합니다.
    /// </summary>
    public Series<double> Var(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Var());
    }

    /// <summary>
    /// 열별 표준편차를 구합니다.
    /// </summary>
    public Series<double> Std(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Std());
    }

    /// <summary>
    /// 열별 백분위수를 구합니다.
    /// </summary>
    public Series<double> Quantile(double q, int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Quantile(q));
    }

    /// <summary>
    /// 열별 최댓값을 구합니다.
    /// </summary>
    public Series<double> Max(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c =>
        {
            var res = c.Max();
            if (res == null) return double.NaN;
            try { return Convert.ToDouble(res); } catch { throw new NotSupportedException(); }
        });
    }

    /// <summary>
    /// 열별 최솟값을 구합니다.
    /// </summary>
    public Series<double> Min(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c =>
        {
            var res = c.Min();
            if (res == null) return double.NaN;
            try { return Convert.ToDouble(res); } catch { throw new NotSupportedException(); }
        });
    }

    /// <summary>
    /// 누적합(Cumulative Sum)을 계산합니다.
    /// </summary>
    public DataFrame Cumsum(int axis = 0, bool skipna = true)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        if (axis == 0) // 열별 누적
        {
            foreach (var columnName in _columnNames)
            {
                var column = _columns[columnName];
                if (IsNumericType(column.DataType))
                {
                    var cumData = new double[column.Length];
                    double cumSum = 0;

                    for (int i = 0; i < column.Length; i++)
                    {
                        var value = column.GetValue(i);
                        if (value != null && !value.Equals(DBNull.Value))
                        {
                            cumSum += Convert.ToDouble(value);
                            cumData[i] = cumSum;
                        }
                        else if (skipna)
                        {
                            cumData[i] = cumSum;
                        }
                        else
                        {
                            cumData[i] = double.NaN;
                        }
                    }

                    resultColumns[columnName] = new PrimitiveColumn<double>(cumData);
                }
                else
                {
                    resultColumns[columnName] = column;
                }
            }
        }

        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 누적곱(Cumulative Product)을 계산합니다.
    /// </summary>
    public DataFrame Cumprod(int axis = 0, bool skipna = true)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        if (axis == 0)
        {
            foreach (var columnName in _columnNames)
            {
                var column = _columns[columnName];
                if (IsNumericType(column.DataType))
                {
                    var cumData = new double[column.Length];
                    double cumProd = 1;

                    for (int i = 0; i < column.Length; i++)
                    {
                        var value = column.GetValue(i);
                        if (value != null && !value.Equals(DBNull.Value))
                        {
                            cumProd *= Convert.ToDouble(value);
                            cumData[i] = cumProd;
                        }
                        else if (skipna)
                        {
                            cumData[i] = cumProd;
                        }
                        else
                        {
                            cumData[i] = double.NaN;
                        }
                    }

                    resultColumns[columnName] = new PrimitiveColumn<double>(cumData);
                }
                else
                {
                    resultColumns[columnName] = column;
                }
            }
        }

        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 누적 최댓값(Cumulative Maximum)을 계산합니다.
    /// </summary>
    public DataFrame Cummax(int axis = 0, bool skipna = true)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        if (axis == 0)
        {
            foreach (var columnName in _columnNames)
            {
                var column = _columns[columnName];
                if (IsNumericType(column.DataType))
                {
                    var cumData = new double[column.Length];
                    double cumMax = double.NegativeInfinity;

                    for (int i = 0; i < column.Length; i++)
                    {
                        var value = column.GetValue(i);
                        if (value != null && !value.Equals(DBNull.Value))
                        {
                            var doubleValue = Convert.ToDouble(value);
                            cumMax = Math.Max(cumMax, doubleValue);
                            cumData[i] = cumMax;
                        }
                        else if (skipna)
                        {
                            cumData[i] = cumMax == double.NegativeInfinity ? double.NaN : cumMax;
                        }
                        else
                        {
                            cumData[i] = double.NaN;
                        }
                    }

                    resultColumns[columnName] = new PrimitiveColumn<double>(cumData);
                }
                else
                {
                    resultColumns[columnName] = column;
                }
            }
        }

        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 누적 최솟값(Cumulative Minimum)을 계산합니다.
    /// </summary>
    public DataFrame Cummin(int axis = 0, bool skipna = true)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        if (axis == 0)
        {
            foreach (var columnName in _columnNames)
            {
                var column = _columns[columnName];
                if (IsNumericType(column.DataType))
                {
                    var cumData = new double[column.Length];
                    double cumMin = double.PositiveInfinity;

                    for (int i = 0; i < column.Length; i++)
                    {
                        var value = column.GetValue(i);
                        if (value != null && !value.Equals(DBNull.Value))
                        {
                            var doubleValue = Convert.ToDouble(value);
                            cumMin = Math.Min(cumMin, doubleValue);
                            cumData[i] = cumMin;
                        }
                        else if (skipna)
                        {
                            cumData[i] = cumMin == double.PositiveInfinity ? double.NaN : cumMin;
                        }
                        else
                        {
                            cumData[i] = double.NaN;
                        }
                    }

                    resultColumns[columnName] = new PrimitiveColumn<double>(cumData);
                }
                else
                {
                    resultColumns[columnName] = column;
                }
            }
        }

        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 차분(Difference, 현재 행 - 이전 행)을 계산합니다.
    /// </summary>
    public DataFrame Diff(int periods = 1, int axis = 0)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        if (axis == 0)
        {
            foreach (var columnName in _columnNames)
            {
                var column = _columns[columnName];
                if (IsNumericType(column.DataType))
                {
                    var diffData = new double?[column.Length];

                    for (int i = 0; i < column.Length; i++)
                    {
                        if (i >= periods)
                        {
                            var currentValue = column.GetValue(i);
                            var previousValue = column.GetValue(i - periods);

                            if (currentValue != null && previousValue != null &&
                                !currentValue.Equals(DBNull.Value) && !previousValue.Equals(DBNull.Value))
                            {
                                diffData[i] = Convert.ToDouble(currentValue) - Convert.ToDouble(previousValue);
                            }
                        }
                    }

                    var diffDataConverted = diffData.Select(x => x.HasValue ? (object)x.Value : null).ToArray();
                    resultColumns[columnName] = CreateColumn(diffDataConverted, typeof(double));
                }
                else
                {
                    resultColumns[columnName] = column;
                }
            }
        }

        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 변화율(Percentage Change)을 계산합니다.
    /// </summary>
    public DataFrame PctChange(int periods = 1, object? fillMethod = null)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            var column = _columns[columnName];
            if (IsNumericType(column.DataType))
            {
                var pctData = new double?[column.Length];

                for (int i = 0; i < column.Length; i++)
                {
                    if (i >= periods)
                    {
                        var currentValue = column.GetValue(i);
                        var previousValue = column.GetValue(i - periods);

                        if (currentValue != null && previousValue != null &&
                            !currentValue.Equals(DBNull.Value) && !previousValue.Equals(DBNull.Value))
                        {
                            var current = Convert.ToDouble(currentValue);
                            var previous = Convert.ToDouble(previousValue);

                            if (previous != 0)
                            {
                                pctData[i] = (current - previous) / previous;
                            }
                        }
                    }
                }

                var pctDataConverted = pctData.Select(x => x.HasValue ? (object)x.Value : null).ToArray();
                resultColumns[columnName] = CreateColumn(pctDataConverted, typeof(double));
            }
            else
            {
                resultColumns[columnName] = column;
            }
        }

        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    private bool IsNumericType(Type type)
    {
        return type == typeof(int) || type == typeof(double) || type == typeof(float) ||
               type == typeof(decimal) || type == typeof(long) || type == typeof(short);
    }

    private List<double> GetNumericValues(string columnName)
    {
        var values = new List<double>();
        var column = _columns[columnName];

        for (int i = 0; i < RowCount; i++)
        {
            var value = column.GetValue(i);
            if (value != null && !value.Equals(DBNull.Value))
            {
                values.Add(Convert.ToDouble(value));
            }
        }

        return values;
    }

    private double CalculateStandardDeviation(List<double> values)
    {
        if (values.Count() <= 1) return 0;

        var mean = values.Average();
        var sumSquaredDiffs = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumSquaredDiffs / (values.Count() - 1));
    }

    private double CalculateQuantile(List<double> values, double quantile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var index = quantile * (sorted.Count() - 1);

        if (index % 1 == 0)
        {
            return sorted[(int)index];
        }
        else
        {
            var lower = sorted[(int)Math.Floor(index)];
            var upper = sorted[(int)Math.Ceiling(index)];
            return lower + (upper - lower) * (index % 1);
        }
    }

    /// <summary>
    /// 그룹바이(GroupBy) 연산을 수행합니다. (SIMD 가속 지원)
    /// </summary>
    public SimdGroupBy GroupBy(string key)
    {
        return new SimdGroupBy(_columns, new[] { key });
    }

    /// <summary>
    /// 여러 키를 기준으로 그룹바이(Multi-key GroupBy) 연산을 수행합니다.
    /// </summary>
    public SimdGroupBy GroupBy(string[] keys)
    {
        return new SimdGroupBy(_columns, keys);
    }

    /// <summary>
    /// 데이터를 위/아래로 이동(Shift)시킵니다.
    /// </summary>
    public DataFrame Shift(int periods)
    {
        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            newColumns[columnName] = _columns[columnName].Shift(periods);
        }
        return new DataFrame(newColumns, Index);
    }

    /// <summary>
    /// 이동 윈도우(Rolling Window) 객체를 생성합니다.
    /// </summary>
    public RollingWindow Rolling(int window, int? minPeriods = null)
    {
        return new RollingWindow(this, window, minPeriods);
    }

    /// <summary>
    /// 시계열 데이터 리샘플링(Resampling)을 수행합니다.
    /// </summary>
    /// <param name="rule">리샘플링 규칙 (예: "D", "H", "T", "S")</param>
    /// <param name="on">기준이 될 시간 컬럼 (지정하지 않으면 인덱스 사용)</param>
    public DateTimeResampler Resample(string rule, string? on = null)
    {
        return new DateTimeResampler(this, rule, on);
    }

    /// <summary>
    /// 내부 식구들(컬럼들)이 렌트(Rent)해 온 `ArrayPool` 자원들을 즉시 회수합니다.
    /// 메모리 릭(Memory Leak)을 방지하는 아주 중요한 진입점입니다.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var columnName in _columnNames)
        {
            if (_columns[columnName] is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _disposed = true;
    }
}

/// <summary>
/// .loc[] 인덱서 (라벨 기반 접근)
/// </summary>
public class DataFrameLocIndexer
{
    private readonly DataFrame _dataFrame;

    public DataFrameLocIndexer(DataFrame dataFrame)
    {
        _dataFrame = dataFrame;
    }

    public object? this[object rowKey, string columnName]
    {
        get => _dataFrame[rowKey, columnName];
        set => _dataFrame[rowKey, columnName] = value;
    }
}

/// <summary>
/// .iloc[] 인덱서 (정수 위치 기반 접근)
/// </summary>
public class DataFrameILocIndexer
{
    private readonly DataFrame _dataFrame;

    public DataFrameILocIndexer(DataFrame dataFrame)
    {
        _dataFrame = dataFrame;
    }

    public object? this[int row, int column]
    {
        get
        {
            if (column < 0 || column >= _dataFrame.ColumnCount)
                throw new IndexOutOfRangeException();
            var columnName = _dataFrame.Columns[column];
            return _dataFrame[row, columnName];
        }
        set
        {
            if (column < 0 || column >= _dataFrame.ColumnCount)
                throw new IndexOutOfRangeException();
            var columnName = _dataFrame.Columns[column];
            _dataFrame[row, columnName] = value;
        }
    }
}
