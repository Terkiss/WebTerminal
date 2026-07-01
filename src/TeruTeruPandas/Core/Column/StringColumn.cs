namespace TeruTeruPandas.Core.Column;

/// <summary>
/// 문자열 데이터를 위한 컬럼 구현
/// lower, upper, replace, contains, startswith, endswith 지원
/// </summary>
public class StringColumn : IColumn, IDisposable
{
    private string?[] _data;
    private bool[] _naMask;
    private bool _isOwner;
    private bool _disposed;

    public Type DataType => typeof(string);
    public int Length { get; private set; }

    public object? this[int index]
    {
        get => GetValue(index);
        set => SetValue(index, value);
    }

    public StringColumn(int length)
    {
        Length = length;
        _data = System.Buffers.ArrayPool<string?>.Shared.Rent(length);
        _naMask = new bool[length];
        _isOwner = true;
        _disposed = false;
    }

    public StringColumn(string?[] data, bool[]? naMask = null, bool isOwner = false)
    {
        _data = data;
        Length = data.Length;
        _naMask = naMask ?? new bool[Length];
        _isOwner = isOwner;
        _disposed = false;
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_isOwner && _data != null)
        {
            Array.Clear(_data, 0, Length); // 참조 타입은 직접 스윕 필요
            System.Buffers.ArrayPool<string?>.Shared.Return(_data);
        }

        _disposed = true;
    }

    public object? GetValue(int index)
    {
        if (index < 0 || index >= Length)
            throw new IndexOutOfRangeException();

        return _naMask[index] ? null : _data[index];
    }

    public void SetValue(int index, object? value)
    {
        if (index < 0 || index >= Length)
            throw new IndexOutOfRangeException();

        if (value == null)
        {
            SetNA(index);
        }
        else
        {
            _data[index] = value.ToString();
            _naMask[index] = false;
        }
    }

    public bool IsNA(int index)
    {
        if (index < 0 || index >= Length)
            throw new IndexOutOfRangeException();

        return _naMask[index];
    }

    public void SetNA(int index)
    {
        if (index < 0 || index >= Length)
            throw new IndexOutOfRangeException();

        _naMask[index] = true;
    }

    public IColumn Clone()
    {
        var newData = new string?[Length];
        var newNaMask = new bool[Length];

        Array.Copy(_data, newData, Length);
        Array.Copy(_naMask, newNaMask, Length);

        return new StringColumn(newData, newNaMask);
    }

    public IColumn Slice(int start, int length)
    {
        if (start < 0 || start + length > Length)
            throw new ArgumentOutOfRangeException();

        var slicedData = new string?[length];
        var slicedNaMask = new bool[length];

        Array.Copy(_data, start, slicedData, 0, length);
        Array.Copy(_naMask, start, slicedNaMask, 0, length);

        return new StringColumn(slicedData, slicedNaMask);
    }

    // 문자열 전용 메서드들
    public StringColumn Lower()
    {
        var result = new StringColumn(Length);
        for (int i = 0; i < Length; i++)
        {
            if (!_naMask[i] && _data[i] != null)
            {
                result._data[i] = _data[i]!.ToLower();
            }
            else
            {
                result._naMask[i] = true;
            }
        }
        return result;
    }

    public StringColumn Upper()
    {
        var result = new StringColumn(Length);
        for (int i = 0; i < Length; i++)
        {
            if (!_naMask[i] && _data[i] != null)
            {
                result._data[i] = _data[i]!.ToUpper();
            }
            else
            {
                result._naMask[i] = true;
            }
        }
        return result;
    }

    public StringColumn Replace(string oldValue, string newValue)
    {
        var result = new StringColumn(Length);
        for (int i = 0; i < Length; i++)
        {
            if (!_naMask[i] && _data[i] != null)
            {
                result._data[i] = _data[i]!.Replace(oldValue, newValue);
            }
            else
            {
                result._naMask[i] = true;
            }
        }
        return result;
    }

    public bool[] Contains(string value)
    {
        var result = new bool[Length];
        for (int i = 0; i < Length; i++)
        {
            result[i] = !_naMask[i] && _data[i] != null && _data[i]!.Contains(value);
        }
        return result;
    }

    public bool[] StartsWith(string value)
    {
        var result = new bool[Length];
        for (int i = 0; i < Length; i++)
        {
            result[i] = !_naMask[i] && _data[i] != null && _data[i]!.StartsWith(value);
        }
        return result;
    }

    public bool[] EndsWith(string value)
    {
        var result = new bool[Length];
        for (int i = 0; i < Length; i++)
        {
            result[i] = !_naMask[i] && _data[i] != null && _data[i]!.EndsWith(value);
        }
        return result;
    }

    public IColumn FillNA(object? value)
    {
        string fillValue = value?.ToString() ?? "";

        var newData = new string?[Length];
        var newNaMask = new bool[Length];

        for (int i = 0; i < Length; i++)
        {
            if (_naMask[i])
            {
                newData[i] = fillValue;
                // StringColumn에서 빈 문자열("")은 NA가 아님. null만 NA로 취급하는 내부 로직이 있다면 주의.
                // 여기서는 Explicit하게 _naMask를 false로 셋팅하므로 OK.
            }
            else
            {
                newData[i] = _data[i];
            }
        }

        return new StringColumn(newData, newNaMask);
    }

    public IColumn FillNA(string method)
    {
        var newData = new string?[Length];
        var newNaMask = new bool[Length];
        Array.Copy(_data, newData, Length);
        Array.Copy(_naMask, newNaMask, Length);

        if (method.ToLower() == "ffill" || method.ToLower() == "pad")
        {
            string lastValid = "";
            bool hasValid = false;

            for (int i = 0; i < Length; i++)
            {
                if (!newNaMask[i])
                {
                    lastValid = newData[i] ?? "";
                    hasValid = true;
                }
                else if (hasValid)
                {
                    newData[i] = lastValid;
                    newNaMask[i] = false;
                }
            }
        }
        else if (method.ToLower() == "bfill" || method.ToLower() == "backfill")
        {
            string lastValid = "";
            bool hasValid = false;

            for (int i = Length - 1; i >= 0; i--)
            {
                if (!newNaMask[i])
                {
                    lastValid = newData[i] ?? "";
                    hasValid = true;
                }
                else if (hasValid)
                {
                    newData[i] = lastValid;
                    newNaMask[i] = false;
                }
            }
        }
        else
        {
            throw new ArgumentException($"Unknown FillNA method: {method}");
        }

        return new StringColumn(newData, newNaMask);
    }

    public int[] Argsort(bool ascending = true)
    {
        var indices = Enumerable.Range(0, Length).ToArray();
        Array.Sort(indices, (a, b) =>
        {
            if (_naMask[a] && _naMask[b]) return 0;
            if (_naMask[a]) return 1;
            if (_naMask[b]) return -1;

            int cmp = string.Compare(_data[a], _data[b]);
            return ascending ? cmp : -cmp;
        });
        return indices;
    }

    public IColumn Reorder(int[] indices)
    {
        var newData = new string?[indices.Length];
        var newNaMask = new bool[indices.Length];

        for (int i = 0; i < indices.Length; i++)
        {
            int oldIndex = indices[i];
            newData[i] = _data[oldIndex];
            newNaMask[i] = _naMask[oldIndex];
        }

        return new StringColumn(newData, newNaMask);
    }

    public IColumn Add(IColumn other)
    {
        if (other is StringColumn otherStr)
        {
            var result = new string?[Length];
            var resultNa = new bool[Length];
            for (int i = 0; i < Length; i++)
            {
                if (_naMask[i] || otherStr.IsNA(i)) resultNa[i] = true;
                else result[i] = _data[i] + otherStr.GetValue(i);
            }
            return new StringColumn(result, resultNa);
        }
        else
        {
            // Convert other to string
            var result = new string?[Length];
            var resultNa = new bool[Length];
            for (int i = 0; i < Length; i++)
            {
                if (_naMask[i] || other.IsNA(i)) resultNa[i] = true;
                else result[i] = _data[i] + other.GetValue(i)?.ToString();
            }
            return new StringColumn(result, resultNa);
        }
    }

    public IColumn Add(object scalar)
    {
        var scalarStr = scalar?.ToString() ?? "";
        var result = new string?[Length];
        var resultNaMask = new bool[Length];

        for (int i = 0; i < Length; i++)
        {
            if (_naMask[i])
            {
                resultNaMask[i] = true;
            }
            else
            {
                result[i] = _data[i] + scalarStr;
            }
        }
        return new StringColumn(result, resultNaMask);
    }

    public IColumn Sub(IColumn other) => throw new NotSupportedException("String subtraction is not supported");
    public IColumn Sub(object scalar) => throw new NotSupportedException("String subtraction is not supported");
    public IColumn Mul(IColumn other) => throw new NotSupportedException("String multiplication is not supported"); // Python supports str * int, can implement later
    public IColumn Mul(object scalar) => throw new NotSupportedException("String multiplication is not supported");
    public IColumn Div(IColumn other) => throw new NotSupportedException("String division is not supported");
    public IColumn Div(object scalar) => throw new NotSupportedException("String division is not supported");
    public IColumn Mod(IColumn other) => throw new NotSupportedException("String modulus is not supported");
    public IColumn Mod(object scalar) => throw new NotSupportedException("String modulus is not supported");
    public IColumn Pow(IColumn other) => throw new NotSupportedException("String power is not supported");
    public IColumn Pow(object scalar) => throw new NotSupportedException("String power is not supported");

    // Aggregation
    public double Sum() => throw new NotSupportedException("String Sum not supported");
    public double Mean() => throw new NotSupportedException("String Mean not supported");
    public double Median() => throw new NotSupportedException("String Median not supported");
    public double Var() => throw new NotSupportedException("String Var not supported");
    public double Std() => throw new NotSupportedException("String Std not supported");
    public double Quantile(double q) => throw new NotSupportedException("String Quantile not supported");

    public object? Max()
    {
        string? max = null;
        for (int i = 0; i < Length; i++)
        {
            if (!_naMask[i] && _data[i] != null)
            {
                if (max == null || String.Compare(_data[i], max) > 0)
                    max = _data[i];
            }
        }
        return max;
    }

    public object? Min()
    {
        string? min = null;
        for (int i = 0; i < Length; i++)
        {
            if (!_naMask[i] && _data[i] != null)
            {
                if (min == null || String.Compare(_data[i], min) < 0)
                    min = _data[i];
            }
        }
        return min;
    }

    public IColumn Shift(int periods)
    {
        var newData = new string?[Length];
        var newNaMask = new bool[Length];

        for (int i = 0; i < Length; i++)
        {
            int sourceIdx = i - periods;
            if (sourceIdx >= 0 && sourceIdx < Length)
            {
                newData[i] = _data[sourceIdx];
                newNaMask[i] = _naMask[sourceIdx];
            }
            else
            {
                newNaMask[i] = true;
            }
        }

        return new StringColumn(newData, newNaMask);
    }
}