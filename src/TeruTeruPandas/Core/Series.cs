using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.Core;

/// <summary>
/// 단일 열(Column) 데이터를 다루기 위한 1차원 벡터 구조.
/// DataFrame의 파편이며 내부적으로 `PrimitiveColumn<T>`를 직접 포장(Wrapping)합니다.
/// ArrayPool 최적화 구조하에서 값과 인덱스를 손쉽게 조작할 수 있도록 도와주는 편의 클래스입니다.
/// </summary>
public class Series<T> where T : struct
{
    private readonly PrimitiveColumn<T> _column;
    private readonly Index.Index _index;

    public IColumn InternalColumn => _column; // Internal Access

    public string? Name { get; set; }
    public int Length => _column.Length;
    public Type DataType => typeof(T);
    public Index.Index Index => _index;

    public Series(T[] data, Index.Index? index = null, string? name = null)
    {
        _column = new PrimitiveColumn<T>(data);
        _index = index ?? new RangeIndex(data.Length);
        Name = name;
    }

    public Series(int length, Index.Index? index = null, string? name = null)
    {
        _column = new PrimitiveColumn<T>(length);
        _index = index ?? new RangeIndex(length);
        Name = name;
    }

    public Series(IColumn column, Index.Index? index = null, string? name = null)
    {
        _column = (PrimitiveColumn<T>)column; // Cast check
        _index = index ?? new RangeIndex(column.Length);
        Name = name;
    }

    // dt 접근자 (DateTime 타입인 경우에만 동작)
    public DateTimeProperties Dt
    {
        get
        {
            if (typeof(T) != typeof(DateTime))
                throw new InvalidOperationException("Series does not contain DateTime data");

            return new DateTimeProperties(_column, _index);
        }
    }

    // 인덱싱 지원
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

    // 결측치 처리
    public bool IsNA(int position) => _column.IsNA(position);
    public void SetNA(int position) => _column.SetNA(position);

    // Phase 4 지원을 위한 GetValue 메서드 추가
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

    public object? GetValue(int position)
    {
        return this[position];
    }

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

    // 슬라이싱
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

    // 형 변환
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

    // 직접 데이터 접근 (성능 최적화)
    public Span<T> AsSpan() => _column.AsSpan();
    public ReadOnlySpan<bool> GetNAMask() => _column.GetNAMask();

    // 기본 통계
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

    // 비교 연산자 - 불린 Series 반환
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
/// 불린 Series - 필터링을 위한 특수 Series
/// </summary>
public class BoolSeries
{
    private readonly bool[] _data;
    private readonly Index.Index _index;

    public string? Name { get; set; }
    public int Length => _data.Length;
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

    // 논리 연산자
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

    public static BoolSeries operator !(BoolSeries series)
    {
        var result = new bool[series.Length];
        for (int i = 0; i < series.Length; i++)
        {
            result[i] = !series[i];
        }
        return new BoolSeries(result, series._index);
    }

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