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
/// </summary>
public class DataFrame : IDisposable
{
    private readonly Dictionary<string, IColumn> _columns;
    private readonly Index.Index _index;
    private readonly List<string> _columnNames;
    private bool _disposed;

    public Index.Index Index => _index;
    public string[] Columns => _columnNames.ToArray();
    public int RowCount => _index.Length;
    public int ColumnCount => _columnNames.Count;

    // 3단계: 추후 기본 속성 추가
    public int Size => RowCount * ColumnCount;
    public bool Empty => RowCount == 0;

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

        // 모든 컬럼의 길이가 동일한지 확인
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

    // 인덱서 지원
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

    public object? this[int row, string column]
    {
        get => this[column].GetValue(row);
        set => this[column].SetValue(row, value);
    }

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

    // 불린 인덱서 지원
    public DataFrame this[BoolSeries mask]
    {
        get
        {
            if (mask.Length != RowCount)
                throw new ArgumentException("Boolean mask length must match DataFrame row count");

            var indices = mask.GetTrueIndices();
            return TakeRows(indices);
        }
    }

    private DataFrame TakeRows(int[] indices)
    {
        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            newColumns[columnName] = _columns[columnName].Reorder(indices);
        }

        var newIndex = _index.Reorder(indices);
        return new DataFrame(newColumns, newIndex);
    }

    // Loc 인덱서(pandas 스타일)
    public DataFrameLocIndexer Loc => new(this);

    // 4단계: at, iat 인덱서 구현 (todo.yaml에서 정의)
    /// <summary>
    /// 가장 빠른 접근 - 라벨 기반
    /// todo.yaml 4단계에서 정의한 at 인덱서
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
    /// 가장 빠른 접근 - 정수 위치 기반
    /// todo.yaml 4단계에서 정의한 iat 인덱서 
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
    /// 값 설정 - 라벨 기반
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
    /// 값 설정 - 정수 위치 기반
    /// </summary>
    public void SetIat(int row, int column, object value)
    {
        if (row < 0 || row >= RowCount)
            throw new IndexOutOfRangeException($"Row index {row} out of range");

        if (column < 0 || column >= ColumnCount)
            throw new IndexOutOfRangeException($"Column index {column} out of range");

        _columns[_columnNames[column]].SetValue(row, value);
    }

    // ILoc 인덱서(pandas 스타일)
    public DataFrameILocIndexer ILoc => new(this);

    // 열 추가/제거
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

    public void DropColumn(string name)
    {
        if (!_columns.ContainsKey(name))
            throw new KeyNotFoundException($"Column '{name}' not found");

        _columns.Remove(name);
        _columnNames.Remove(name);
    }

    // 기본 메서드들
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
    /// 결측치 제거 (옵션 강화)
    /// </summary>
    /// <param name="how">"any": 하나라도 NA면 등, "all": 모든 컬럼이 NA면 드랍</param>
    /// <param name="thresh">최소한 이 개수 이상의 정상 데이터가 있어야 생존 (how보다 우선순위 높음)</param>
    /// <returns></returns>
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
            else // thresh가 없으면 how 확인
            {
                if (how == "any")
                {
                    keep = naCount == 0;
                }
                else if (how == "all")
                {
                    keep = nonNaCount > 0; // 모두 NA인 경우(nonNaCount == 0)만 드랍 -> 하나라도 있으면 keep
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

        // 인덱스 유지 (중요)
        var newIndex = _index.Reorder(indices);

        return new DataFrame(newColumns, newIndex);
    }

    public DataFrame FillNA(object? value)
    {
        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            newColumns[columnName] = _columns[columnName].FillNA(value);
        }
        return new DataFrame(newColumns, _index);
    }

    public DataFrame FillNA(string method)
    {
        var newColumns = new Dictionary<string, IColumn>();
        foreach (var columnName in _columnNames)
        {
            newColumns[columnName] = _columns[columnName].FillNA(method);
        }
        return new DataFrame(newColumns, _index);
    }

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

    // 3단계: DataFrame.info() 메서드 구현
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

    // 3단계: DataFrame.describe() 메서드 구현
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

    // 3단계: 통계 함수 구현
    public Series<double> Std()
    {
        return ApplyStatisticFunction("std", values => CalculateStandardDeviation(values));
    }

    public Series<double> Var()
    {
        return ApplyStatisticFunction("var", values =>
        {
            if (values.Count() <= 1) return 0;
            var mean = values.Average();
            return values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count() - 1);
        });
    }

    public Series<double> Median()
    {
        return ApplyStatisticFunction("median", values => CalculateQuantile(values, 0.5));
    }

    public Series<double> Min()
    {
        return ApplyStatisticFunction("min", values => values.Min());
    }

    public Series<double> Max()
    {
        return ApplyStatisticFunction("max", values => values.Max());
    }

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

    // 3단계: 결측치 처리 함수
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

    // 헬퍼 메서드: 타입에 따라 적절한 컬럼 생성
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
            // 기본적으로 object 배열은 StringColumn 생성
            var stringData = data.Select(x => x?.ToString() ?? string.Empty).ToArray();
            return new StringColumn(stringData);
        }
    }

    // 4단계: 이항 연산 구현 (todo.yaml에서 정의)

    /// <summary>
    /// DataFrame 또는 스칼라와의 덧셈 연산
    /// todo.yaml 4단계에서 정의한 add 연산
    /// </summary>
    /// <summary>
    /// 모듈러 연산 (나머지)
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

    public DataFrame Add(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Add", fillValue);
        return PerformBinaryOpScalar(other, "Add", fillValue);
    }

    public DataFrame Sub(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Sub", fillValue);
        return PerformBinaryOpScalar(other, "Sub", fillValue);
    }

    public DataFrame Mul(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Mul", fillValue);
        return PerformBinaryOpScalar(other, "Mul", fillValue);
    }

    public DataFrame Div(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Div", fillValue);
        return PerformBinaryOpScalar(other, "Div", fillValue);
    }

    public DataFrame Pow(object other, int axis = 1, object? fillValue = null)
    {
        if (other is DataFrame otherDf) return PerformBinaryOp(otherDf, "Pow", fillValue);
        return PerformBinaryOpScalar(other, "Pow", fillValue);
    }

    /// <summary>
    /// 동등 비교 연산
    /// todo.yaml 4단계에서 정의한 eq 연산
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
    /// 부등 비교 연산
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
    /// 작음 비교 연산
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
    /// 작거나 같음 비교 연산
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
    /// 큼 비교 연산
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
    /// 크거나 같음 비교 연산
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

    // 이항 연산 헬퍼 메서드들

    private DataFrame BinaryOperation(DataFrame other, Func<object, object, object> operation, object fillValue)
    {
        var resultColumns = new Dictionary<string, IColumn>();
        var allColumns = _columnNames.Union(other._columnNames).ToList();

        foreach (var columnName in allColumns)
        {
            var leftColumn = _columns.ContainsKey(columnName) ? _columns[columnName] : null;
            var rightColumn = other._columns.ContainsKey(columnName) ? other._columns[columnName] : null;

            var maxLength = Math.Max(leftColumn?.Length ?? 0, rightColumn?.Length ?? 0);
            var resultValues = new object[maxLength];

            for (int i = 0; i < maxLength; i++)
            {
                var leftValue = leftColumn?.GetValue(i) ?? fillValue;
                var rightValue = rightColumn?.GetValue(i) ?? fillValue;

                resultValues[i] = operation(leftValue, rightValue);
            }

            resultColumns[columnName] = CreateColumn(resultValues, typeof(object));
        }

        var resultIndex = new RangeIndex(resultColumns.Values.First().Length);
        return new DataFrame(resultColumns, resultIndex);
    }

    private DataFrame BinaryOperationWithScalar(object scalar, Func<object, object, object> operation)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            var column = _columns[columnName];
            var resultValues = new object[column.Length];

            for (int i = 0; i < column.Length; i++)
            {
                var value = column.GetValue(i);
                resultValues[i] = operation(value ?? 0, scalar);
            }

            resultColumns[columnName] = CreateColumn(resultValues, column.DataType);
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

    // IColumn 산술 메서드를 활용한 최적화된 이항 연산

    private DataFrame PerformBinaryOp(DataFrame other, string opName, object? fillValue)
    {
        // 3단계를 위한 간단한 인덱스 검증 (최소한 동일한 인덱스 길이를 요구함)
        if (this.RowCount != other.RowCount)
        {
            // Try to align? For now throw as per plan.
            // But actually Pandas 'Broadcasting' usually means scalar broadcasting OR alignment.
            // If lengths differ, we cannot simply column-op unless we reindex.
            throw new NotSupportedException("DataFrame lengths must match for arithmetic operations in this version.");
        }

        var resultColumns = new Dictionary<string, IColumn>();
        var allColumns = _columnNames.Union(other._columnNames).ToList();

        foreach (var columnName in allColumns)
        {
            IColumn? left = _columns.ContainsKey(columnName) ? _columns[columnName] : null;
            IColumn? right = other._columns.ContainsKey(columnName) ? other._columns[columnName] : null;

            if (left == null && right == null) continue; // Should not happen

            // 하나가 누락된 경우, 결과는 모두 NA입니다 (fillValue가 지정되지 않은 한...)
            // 실제로 한쪽이 누락되면 Pandas 결과는 NaN입니다.
            // fillValue가 제공되면 누락된 쪽은 fillValue로 처리됩니다.

            if (left == null)
            {
                if (fillValue != null)
                {
                    // Treat left as a column of fillValue
                    // Need to create a scalar column or similar. 
                    // Simpler: use right and apply inverse op with scalar? 
                    // e.g. fillValue(0) - right.
                    // But I don't have 'Scalar - Column' op in IColumn.
                    // Workaround: Create a constant column.
                    left = CreateConstantColumn(right!.Length, fillValue, right.DataType);
                }
                else
                {
                    resultColumns[columnName] = CreateConstantColumn(this.RowCount, null, typeof(object)); // NA column
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
                    resultColumns[columnName] = CreateConstantColumn(this.RowCount, null, typeof(object)); // NA column
                    continue;
                }
            }

            // Apply fillValue to existing NAs if requested
            if (fillValue != null)
            {
                left = left!.FillNA(fillValue);
                right = right!.FillNA(fillValue);
            }

            // Compute
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

        return new DataFrame(resultColumns, _index); // Keeps original index
    }

    private DataFrame PerformBinaryOpScalar(object scalar, string opName, object? fillValue)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        foreach (var columnName in _columnNames)
        {
            var col = _columns[columnName];
            if (fillValue != null) col = col.FillNA(fillValue);

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

    // 상수 컬럼을 생성하기 위한 헬퍼
    private IColumn CreateConstantColumn(int length, object? value, Type hintType)
    {
        // 컬럼 타입을 선택하기 위한 간단한 휴리스틱
        if (value == null) return new PrimitiveColumn<double>(length); // NA -> double (NaN 가능) 논리적 선택? 또는 Object
                                                                       // 하지만 수치 연산의 경우 보통 double 또는 float을 사용합니다.

        var type = value.GetType();
        // Invoke generic Create
        if (type == typeof(int)) return new PrimitiveColumn<int>(Enumerable.Repeat((int)value, length).ToArray());
        if (type == typeof(double)) return new PrimitiveColumn<double>(Enumerable.Repeat((double)value, length).ToArray());
        if (type == typeof(float)) return new PrimitiveColumn<float>(Enumerable.Repeat((float)value, length).ToArray());
        if (type == typeof(long)) return new PrimitiveColumn<long>(Enumerable.Repeat((long)value, length).ToArray());
        if (type == typeof(string))
        {
            var arr = new string[length]; Array.Fill(arr, (string)value);
            return new StringColumn(arr);
        }

        // Fallback
        var objArr = new object?[length]; Array.Fill(objArr, value);
        // We lack ObjectColumn? Use StringColumn for now or error?
        // TeruTeruPandas lacks generic ObjectColumn in gap analysis!
        throw new NotSupportedException($"Constant column of type {type} not supported yet in helper.");
    }

    private static object? SubtractValues(object? a, object? b)
    {
        if (a == null || b == null) return null;

        try
        {
            return (dynamic)a - (dynamic)b;
        }
        catch
        {
            return null;
        }
    }

    private static object? MultiplyValues(object? a, object? b)
    {
        if (a == null || b == null) return null;

        try
        {
            return (dynamic)a * (dynamic)b;
        }
        catch
        {
            return null;
        }
    }

    private static object? DivideValues(object? a, object? b)
    {
        if (a == null || b == null) return null;

        try
        {
            return (dynamic)a / (dynamic)b;
        }
        catch
        {
            return null;
        }
    }

    private static object? PowerValues(object? a, object? b)
    {
        if (a == null || b == null) return null;

        try
        {
            return Math.Pow(Convert.ToDouble(a), Convert.ToDouble(b));
        }
        catch
        {
            return null;
        }
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

    public Series<double> Mean(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Mean());
    }

    public Series<double> Sum(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Sum());
    }

    public Series<double> Median(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Median());
    }

    public Series<double> Var(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Var());
    }

    public Series<double> Std(int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Std());
    }

    public Series<double> Quantile(double q, int axis = 0)
    {
        if (axis != 0) throw new NotImplementedException("Axis 1 not supported");
        return ComputeStat(c => c.Quantile(q));
    }


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

    // 4단계: 누적 연산 함수 구현 (todo.yaml에서 정의)

    /// <summary>
    /// 누적합 계산
    /// todo.yaml 4단계에서 정의한 cumsum 구현
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
                    resultColumns[columnName] = column; // 비수치형은 그대로 복사
                }
            }
        }

        // 결과가 없는 경우 원본 반환
        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 누적곱 계산
    /// todo.yaml 4단계에서 정의한 cumprod 구현
    /// </summary>
    public DataFrame Cumprod(int axis = 0, bool skipna = true)
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

        // 결과가 없는 경우 원본 반환
        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 누적 최댓값 계산
    /// todo.yaml 4단계에서 정의한 cummax 구현
    /// </summary>
    public DataFrame Cummax(int axis = 0, bool skipna = true)
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

        // 결과가 없는 경우 원본 반환
        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 누적 최솟값 계산
    /// todo.yaml 4단계에서 정의한 cummin 구현
    /// </summary>
    public DataFrame Cummin(int axis = 0, bool skipna = true)
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

        // 결과가 없는 경우 원본 반환
        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 차분 계산 (현재값 - 이전값)
    /// todo.yaml 4단계에서 정의한 diff 구현
    /// </summary>
    public DataFrame Diff(int periods = 1, int axis = 0)
    {
        var resultColumns = new Dictionary<string, IColumn>();

        if (axis == 0) // 열별 차분
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

        // 결과가 없는 경우 원본 반환
        if (resultColumns.Count == 0)
        {
            return new DataFrame(_columns, _index);
        }

        return new DataFrame(resultColumns, _index);
    }

    /// <summary>
    /// 변화율 계산 ((현재값 - 이전값) / 이전값)
    /// todo.yaml 4단계에서 정의한 pct_change 구현
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

        // 결과가 없는 경우 원본 반환
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
    /// GroupBy 연산 (SIMD 최적화)
    /// </summary>
    public SimdGroupBy GroupBy(string key)
    {
        return new SimdGroupBy(_columns, new[] { key });
    }

    /// <summary>
    /// Multi-key GroupBy 연산
    /// </summary>
    public SimdGroupBy GroupBy(string[] keys)
    {
        return new SimdGroupBy(_columns, keys);
    }

    /// <summary>
    /// 데이터를 위/아래로 이동 (periods 만큼)
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
    /// 이동 윈도우 생성
    /// </summary>
    public RollingWindow Rolling(int window, int? minPeriods = null)
    {
        return new RollingWindow(this, window, minPeriods);
    }

    /// <summary>
    /// 시계열 리샘플링
    /// </summary>
    /// <param name="rule">D(Day), H(Hour), Min(Minute), S(Second) 등</param>
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
/// .loc[] 인덱서 (라벨 기반)
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
/// .iloc[] 인덱서 (위치 기반)
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
