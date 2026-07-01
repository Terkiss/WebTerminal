namespace TeruTeruPandas.Core.Index;

/// <summary>
/// 행 인덱싱 객체, Range/Int/String/DateTime 지원
/// </summary>
public abstract class Index
{
    public abstract int Length { get; }
    public abstract Type DataType { get; }

    public abstract object GetValue(int position);
    public abstract int GetPosition(object value);
    public abstract bool Contains(object value);
    public abstract Index Slice(int start, int length);
    public abstract Index Reorder(int[] indices);
    public abstract int[] Argsort(bool ascending = true);

    public virtual object this[int position] => GetValue(position);
}

/// <summary>
/// 정수 범위 기반 인덱스 (0, 1, 2, ...)
/// </summary>
public class RangeIndex : Index
{
    private readonly int _start;
    private readonly int _step;

    public override int Length { get; }
    public override Type DataType => typeof(int);

    public RangeIndex(int length, int start = 0, int step = 1)
    {
        Length = length;
        _start = start;
        _step = step;
    }

    public override object GetValue(int position)
    {
        if (position < 0 || position >= Length)
            throw new IndexOutOfRangeException();

        return _start + position * _step;
    }

    public override int GetPosition(object value)
    {
        if (value is not int intValue)
            return -1;

        if (_step == 0)
            return -1;

        int position = (intValue - _start) / _step;
        if (position >= 0 && position < Length && _start + position * _step == intValue)
            return position;

        return -1;
    }

    public override bool Contains(object value)
    {
        return GetPosition(value) >= 0;
    }

    public override Index Slice(int start, int length)
    {
        if (start < 0 || start + length > Length)
            throw new ArgumentOutOfRangeException();

        int newStart = _start + start * _step;
        return new RangeIndex(length, newStart, _step);
    }

    public override Index Reorder(int[] indices)
    {
        var newValues = new int[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            newValues[i] = (int)GetValue(indices[i]);
        }
        return new IntIndex(newValues);
    }

    public override int[] Argsort(bool ascending = true)
    {
        var indices = Enumerable.Range(0, Length).ToArray();
        // RangeIndex는 이미 정렬되어 있는 경우가 많음
        if (_step > 0)
        {
            return ascending ? indices : Enumerable.Reverse(indices).ToArray();

        }
        else
        {
            return ascending ? Enumerable.Reverse(indices).ToArray() : indices;

        }
    }
}

/// <summary>
/// 정수 값 기반 인덱스
/// </summary>
public class IntIndex : Index
{
    private readonly int[] _values;
    private readonly Dictionary<int, int> _valueToPosition;

    public override int Length => _values.Length;
    public override Type DataType => typeof(int);

    public IntIndex(int[] values)
    {
        _values = values;
        _valueToPosition = new Dictionary<int, int>();

        for (int i = 0; i < values.Length; i++)
        {
            _valueToPosition[values[i]] = i;
        }
    }

    public override object GetValue(int position)
    {
        if (position < 0 || position >= Length)
            throw new IndexOutOfRangeException();

        return _values[position];
    }

    public override int GetPosition(object value)
    {
        if (value is int intValue && _valueToPosition.TryGetValue(intValue, out int position))
            return position;

        return -1;
    }

    public override bool Contains(object value)
    {
        return value is int intValue && _valueToPosition.ContainsKey(intValue);
    }

    public override Index Slice(int start, int length)
    {
        if (start < 0 || start + length > Length)
            throw new ArgumentOutOfRangeException();

        var slicedValues = new int[length];
        Array.Copy(_values, start, slicedValues, 0, length);

        return new IntIndex(slicedValues);
    }

    public override Index Reorder(int[] indices)
    {
        var newValues = new int[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            newValues[i] = _values[indices[i]];
        }
        return new IntIndex(newValues);
    }

    public override int[] Argsort(bool ascending = true)
    {
        var indices = Enumerable.Range(0, Length).ToArray();
        Array.Sort(indices, (a, b) =>
        {
            int cmp = _values[a].CompareTo(_values[b]);
            return ascending ? cmp : -cmp;
        });
        return indices;
    }
}

/// <summary>
/// 문자열 기반 인덱스
/// </summary>
public class StringIndex : Index
{
    private readonly string[] _values;
    private readonly Dictionary<string, int> _valueToPosition;

    public override int Length => _values.Length;
    public override Type DataType => typeof(string);

    public StringIndex(string[] values)
    {
        _values = values;
        _valueToPosition = new Dictionary<string, int>();

        for (int i = 0; i < values.Length; i++)
        {
            _valueToPosition[values[i]] = i;
        }
    }

    public override object GetValue(int position)
    {
        if (position < 0 || position >= Length)
            throw new IndexOutOfRangeException();

        return _values[position];
    }

    public override int GetPosition(object value)
    {
        if (value is string stringValue && _valueToPosition.TryGetValue(stringValue, out int position))
            return position;

        return -1;
    }

    public override bool Contains(object value)
    {
        return value is string stringValue && _valueToPosition.ContainsKey(stringValue);
    }

    public override Index Slice(int start, int length)
    {
        if (start < 0 || start + length > Length)
            throw new ArgumentOutOfRangeException();

        var slicedValues = new string[length];
        Array.Copy(_values, start, slicedValues, 0, length);

        return new StringIndex(slicedValues);
    }

    public override Index Reorder(int[] indices)
    {
        var newValues = new string[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            newValues[i] = _values[indices[i]];
        }
        return new StringIndex(newValues);
    }

    public override int[] Argsort(bool ascending = true)
    {
        var indices = Enumerable.Range(0, Length).ToArray();
        Array.Sort(indices, (a, b) =>
        {
            int cmp = string.Compare(_values[a], _values[b]);
            return ascending ? cmp : -cmp;
        });
        return indices;
    }
}

/// <summary>
/// DateTime 기반 인덱스 (시계열 데이터용)
/// </summary>
public class DateTimeIndex : Index
{
    private readonly DateTime[] _values;
    private readonly Dictionary<DateTime, int> _valueToPosition;

    public override int Length => _values.Length;
    public override Type DataType => typeof(DateTime);

    public DateTimeIndex(DateTime[] values)
    {
        _values = values;
        _valueToPosition = new Dictionary<DateTime, int>();

        for (int i = 0; i < values.Length; i++)
        {
            _valueToPosition[values[i]] = i;
        }
    }

    public override object GetValue(int position)
    {
        if (position < 0 || position >= Length)
            throw new IndexOutOfRangeException();

        return _values[position];
    }

    public override int GetPosition(object value)
    {
        if (value is DateTime dateTimeValue && _valueToPosition.TryGetValue(dateTimeValue, out int position))
            return position;

        return -1;
    }

    public override bool Contains(object value)
    {
        return value is DateTime dateTimeValue && _valueToPosition.ContainsKey(dateTimeValue);
    }

    public override Index Slice(int start, int length)
    {
        if (start < 0 || start + length > Length)
            throw new ArgumentOutOfRangeException();

        var slicedValues = new DateTime[length];
        Array.Copy(_values, start, slicedValues, 0, length);

        return new DateTimeIndex(slicedValues);
    }

    public override Index Reorder(int[] indices)
    {
        var newValues = new DateTime[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            newValues[i] = _values[indices[i]];
        }
        return new DateTimeIndex(newValues);
    }

    public override int[] Argsort(bool ascending = true)
    {
        var indices = Enumerable.Range(0, Length).ToArray();
        Array.Sort(indices, (a, b) =>
        {
            int cmp = _values[a].CompareTo(_values[b]);
            return ascending ? cmp : -cmp;
        });
        return indices;
    }

    /// <summary>
    /// DateTimeIndex 전용 리샘플링 메서드
    /// </summary>
    public DateTimeIndex Resample(string frequency)
    {
        // 기본 구현: 일('D'), 주('W'), 월('M') 단위 리샘플링
        var timeSpan = frequency switch
        {
            "D" => TimeSpan.FromDays(1),
            "W" => TimeSpan.FromDays(7),
            "M" => TimeSpan.FromDays(30), // 간단한 구현
            _ => throw new ArgumentException($"Unsupported frequency: {frequency}")
        };

        if (_values.Length == 0)
            return new DateTimeIndex(Array.Empty<DateTime>());

        var start = _values[0];
        var end = _values[^1];
        var resampledValues = new List<DateTime>();

        for (var current = start; current <= end; current = current.Add(timeSpan))
        {
            resampledValues.Add(current);
        }

        return new DateTimeIndex(resampledValues.ToArray());
    }
}