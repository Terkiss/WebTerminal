using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.Core;

/// <summary>
/// 단일 열(Column) 데이터를 다루기 위한 1차원 벡터 구조.
/// DataFrame의 파편이며 내부적으로 `PrimitiveColumn&lt;T&gt;`를 직접 포장(Wrapping)합니다.
/// ArrayPool 최적화 구조하에서 값과 인덱스를 손쉽게 조작할 수 있도록 도와주는 편의 클래스입니다.
/// </summary>
/// <typeparam name="T">데이터 타입 (struct 한정)</typeparam>
public class Series<T> where T : struct
{
    private readonly PrimitiveColumn<T> _column;
    private readonly Index.Index _index;

    /// <summary>
    /// 내부 컬럼 객체에 직접 접근합니다. (고급 사용자용)
    /// </summary>
    public IColumn InternalColumn => _column;

    /// <summary>
    /// 시리즈의 이름을 가져오거나 설정합니다.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 시리즈의 전체 길이를 반환합니다.
    /// </summary>
    public int Length => _column.Length;

    /// <summary>
    /// 데이터 타입을 반환합니다.
    /// </summary>
    public Type DataType => typeof(T);

    /// <summary>
    /// 시리즈의 인덱스를 가져옵니다.
    /// </summary>
    public Index.Index Index => _index;

    /// <summary>
    /// 배열 데이터로부터 시리즈를 생성합니다.
    /// </summary>
    public Series(T[] data, Index.Index? index = null, string? name = null)
    {
        _column = new PrimitiveColumn<T>(data);
        _index = index ?? new RangeIndex(data.Length);
        Name = name;
    }

    /// <summary>
    /// 지정된 길이의 빈 시리즈를 생성합니다.
    /// </summary>
    public Series(int length, Index.Index? index = null, string? name = null)
    {
        _column = new PrimitiveColumn<T>(length);
        _index = index ?? new RangeIndex(length);
        Name = name;
    }

    /// <summary>
    /// 기존 IColumn 객체를 포장하여 시리즈를 생성합니다.
    /// </summary>
    public Series(IColumn column, Index.Index? index = null, string? name = null)
    {
        _column = (PrimitiveColumn<T>)column; // 타입 변환 검사 수행
        _index = index ?? new RangeIndex(column.Length);
        Name = name;
    }

    /// <summary>
    /// 날짜/시간 관련 속성(Year, Month, Day 등)에 접근하기 위한 dt 접근자입니다.
    /// </summary>
    public DateTimeProperties Dt
    {
        get
        {
            if (typeof(T) != typeof(DateTime))
                throw new InvalidOperationException("Series does not contain DateTime data");

            return new DateTimeProperties(_column, _index);
        }
    }

    /// <summary>
    /// 정수 위치(Position) 기반으로 값에 접근합니다.
    /// </summary>
    public T? this[int position]
    {
        get
        {
            var value = _column.GetValue(position);
            return value as T?;
        }
        set
        {
            _column.SetValue(position, value);
        }
    }

    /// <summary>
    /// 인덱스 라벨(Key) 기반으로 값에 접근합니다.
    /// </summary>
    public T? this[object key]
    {
        get
        {
            int position = _index.GetPosition(key);
            if (position < 0)
                throw new KeyNotFoundException($"Key '{key}' not found in index");
            return this[position];
        }
        set
        {
            int position = _index.GetPosition(key);
            if (position < 0)
                throw new KeyNotFoundException($"Key '{key}' not found in index");
            this[position] = value;
        }
    }

    /// <summary>
    /// 특정 위치의 값이 결측치(NA)인지 확인합니다.
    /// </summary>
    public bool IsNA(int position) => _column.IsNA(position);

    /// <summary>
    /// 특정 위치의 값을 결측치(NA)로 설정합니다.
    /// </summary>
    public void SetNA(int position) => _column.SetNA(position);

    /// <summary>
    /// 키 라벨 기반으로 값을 가져옵니다. (동적 타입 지원)
    /// </summary>
    public object? GetValue(object key)
    {
        if (key is int intKey)
        {
            return this[intKey];
        }
        else if (key is string stringKey)
        {
            return this[stringKey];
        }
        else
        {
            return this[key];
        }
    }

    /// <summary>
    /// 정수 위치 기반으로 값을 가져옵니다.
    /// </summary>
    public object? GetValue(int position)
    {
        return this[position];
    }

    /// <summary>
    /// 결측치를 지정된 값으로 채운 새로운 시리즈를 생성합니다.
    /// </summary>
    public Series<T> FillNA(T value)
    {
        var result = new Series<T>(Length, _index, Name);
        for (int i = 0; i < Length; i++)
        {
            if (_column.IsNA(i))
            {
                result[i] = value;
            }
            else
            {
                result[i] = this[i];
            }
        }
        return result;
    }

    /// <summary>
    /// 결측치가 포함된 행을 제외한 새로운 시리즈를 생성합니다.
    /// </summary>
    public Series<T> DropNA()
    {
        var validIndices = new List<int>();
        for (int i = 0; i < Length; i++)
        {
            if (!_column.IsNA(i))
            {
                validIndices.Add(i);
            }
        }

        var newData = new T[validIndices.Count];
        for (int i = 0; i < validIndices.Count; i++)
        {
            newData[i] = this[validIndices[i]]!.Value;
        }

        return new Series<T>(newData, new RangeIndex(newData.Length), Name);
    }

    /// <summary>
    /// 시리즈의 일부를 잘라내어 새로운 시리즈를 생성합니다.
    /// </summary>
    public Series<T> Slice(int start, int length)
    {
        var slicedColumn = _column.Slice(start, length);
        var slicedIndex = _index.Slice(start, length);

        var result = new Series<T>(length, slicedIndex, Name);
        for (int i = 0; i < length; i++)
        {
            if (slicedColumn.IsNA(i))
            {
                result.SetNA(i);
            }
            else
            {
                result[i] = (T)slicedColumn.GetValue(i)!;
            }
        }

        return result;
    }

    /// <summary>
    /// 시리즈의 데이터 타입을 변환합니다.
    /// </summary>
    public Series<TOut> Astype<TOut>() where TOut : struct
    {
        var result = new Series<TOut>(Length, _index, Name);
        for (int i = 0; i < Length; i++)
        {
            if (_column.IsNA(i))
            {
                result.SetNA(i);
            }
            else
            {
                var value = this[i]!.Value;
                result[i] = (TOut)Convert.ChangeType(value, typeof(TOut));
            }
        }
        return result;
    }

    /// <summary>
    /// 내부 데이터를 Span으로 노출하여 포인터 수준의 고속 연산을 가능하게 합니다.
    /// </summary>
    public Span<T> AsSpan() => _column.AsSpan();

    /// <summary>
    /// 결측치 여부를 나타내는 불린 마스크를 가져옵니다.
    /// </summary>
    public ReadOnlySpan<bool> GetNAMask() => _column.GetNAMask();

    /// <summary>
    /// 시리즈의 합계를 계산합니다.
    /// </summary>
    public T Sum()
    {
        if (typeof(T) == typeof(int))
        {
            int sum = 0;
            for (int i = 0; i < Length; i++)
            {
                if (!_column.IsNA(i))
                {
                    sum += (int)(object)this[i]!.Value;
                }
            }
            return (T)(object)sum;
        }
        else if (typeof(T) == typeof(double))
        {
            double sum = 0.0;
            for (int i = 0; i < Length; i++)
            {
                if (!_column.IsNA(i))
                {
                    sum += (double)(object)this[i]!.Value;
                }
            }
            return (T)(object)sum;
        }
        else
        {
            throw new NotSupportedException($"Sum operation not supported for type {typeof(T)}");
        }
    }

    /// <summary>
    /// 시리즈의 평균을 계산합니다.
    /// </summary>
    public double Mean()
    {
        if (typeof(T) == typeof(int))
        {
            int sum = 0;
            int count = 0;
            for (int i = 0; i < Length; i++)
            {
                if (!_column.IsNA(i))
                {
                    sum += (int)(object)this[i]!.Value;
                    count++;
                }
            }
            return count > 0 ? (double)sum / count : 0.0;
        }
        else if (typeof(T) == typeof(double))
        {
            double sum = 0.0;
            int count = 0;
            for (int i = 0; i < Length; i++)
            {
                if (!_column.IsNA(i))
                {
                    sum += (double)(object)this[i]!.Value;
                    count++;
                }
            }
            return count > 0 ? sum / count : 0.0;
        }
        else
        {
            throw new NotSupportedException($"Mean operation not supported for type {typeof(T)}");
        }
    }

    /// <summary>
    /// 결측치가 아닌 요소들의 개수를 반환합니다.
    /// </summary>
    public int Count()
    {
        int count = 0;
        for (int i = 0; i < Length; i++)
        {
            if (!_column.IsNA(i))
                count++;
        }
        return count;
    }

    /// <summary>
    /// 시리즈의 내용을 문자열로 요약하여 반환합니다.
    /// </summary>
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Math.Min(Length, 10); i++)
        {
            var indexValue = _index.GetValue(i);
            var value = _column.IsNA(i) ? "NaN" : this[i]?.ToString() ?? "null";
            sb.AppendLine($"{indexValue}: {value}");
        }

        if (Length > 10)
        {
            sb.AppendLine("...");
        }

        sb.AppendLine($"Name: {Name ?? "None"}, Length: {Length}, dtype: {DataType.Name}");
        return sb.ToString();
    }

    /// <summary>
    /// 특정 값과의 비교 연산을 통해 불린 시리즈를 생성합니다.
    /// </summary>
    public Series<bool> Compare<TValue>(TValue value, Func<T, TValue, bool> comparison)
    {
        var result = new bool[Length];
        for (int i = 0; i < Length; i++)
        {
            if (!_column.IsNA(i))
            {
                result[i] = comparison(this[i]!.Value, value);
            }
            else
            {
                result[i] = false;
            }
        }
        return new Series<bool>(result, _index, Name);
    }
}

/// <summary>
/// 필터링 연산에 사용되는 특수 불린 시리즈(Boolean Series)입니다.
/// 논리 연산자(&amp;, |, !)를 지원합니다.
/// </summary>
public class BoolSeries
{
    private readonly bool[] _data;
    private readonly Index.Index _index;

    /// <summary>
    /// 시리즈의 이름을 가져오거나 설정합니다.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 시리즈의 길이를 반환합니다.
    /// </summary>
    public int Length => _data.Length;

    /// <summary>
    /// 시리즈의 인덱스를 가져옵니다.
    /// </summary>
    public Index.Index Index => _index;

    public BoolSeries(bool[] data, Index.Index? index = null, string? name = null)
    {
        _data = data;
        _index = index ?? new RangeIndex(data.Length);
        Name = name;
    }

    public bool this[int position]
    {
        get => _data[position];
        set => _data[position] = value;
    }

    /// <summary>
    /// 두 불린 시리즈 간의 논리곱(AND) 연산을 수행합니다.
    /// </summary>
    public static BoolSeries operator &(BoolSeries left, BoolSeries right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Series lengths must match");

        var result = new bool[left.Length];
        for (int i = 0; i < left.Length; i++)
        {
            result[i] = left[i] && right[i];
        }
        return new BoolSeries(result, left._index);
    }

    /// <summary>
    /// 두 불린 시리즈 간의 논리합(OR) 연산을 수행합니다.
    /// </summary>
    public static BoolSeries operator |(BoolSeries left, BoolSeries right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Series lengths must match");

        var result = new bool[left.Length];
        for (int i = 0; i < left.Length; i++)
        {
            result[i] = left[i] || right[i];
        }
        return new BoolSeries(result, left._index);
    }

    /// <summary>
    /// 불린 시리즈의 논리 부정(NOT) 연산을 수행합니다.
    /// </summary>
    public static BoolSeries operator !(BoolSeries series)
    {
        var result = new bool[series.Length];
        for (int i = 0; i < series.Length; i++)
        {
            result[i] = !series[i];
        }
        return new BoolSeries(result, series._index);
    }

    /// <summary>
    /// True인 요소들의 인덱스 위치를 배열로 추출합니다.
    /// </summary>
    public int[] GetTrueIndices()
    {
        var indices = new List<int>();
        for (int i = 0; i < Length; i++)
        {
            if (_data[i])
                indices.Add(i);
        }
        return indices.ToArray();
    }
}
