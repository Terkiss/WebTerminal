using System.Text;

namespace TeruTeruPandas.Core.Index;

/// <summary>
/// 다중 레벨 인덱스 지원 클래스
/// todo.yaml Phase 4에서 정의된 MultiIndex 구현
/// </summary>
public class MultiIndex : Index
{
    private readonly Index[] _levels;
    private readonly int[][] _codes;
    private readonly string[]? _names;
    private readonly object?[][] _values;

    public override int Length { get; }
    public override Type DataType => typeof(object[]);

    public Index[] Levels => _levels;
    public int[][] Codes => _codes;
    public string[]? Names => _names;

    public MultiIndex(Index[] levels, int[][] codes, string[]? names = null)
    {
        _levels = levels ?? throw new ArgumentNullException(nameof(levels));
        _codes = codes ?? throw new ArgumentNullException(nameof(codes));
        _names = names;

        if (levels.Length != codes.Length)
            throw new ArgumentException("Levels and codes must have the same length");

        if (codes.Length > 0)
        {
            Length = codes[0].Length;
            for (int i = 1; i < codes.Length; i++)
            {
                if (codes[i].Length != Length)
                    throw new ArgumentException("All code arrays must have the same length");
            }
        }
        else
        {
            Length = 0;
        }

        // 값들을 미리 계산해서 캐시
        _values = new object?[Length][];
        for (int i = 0; i < Length; i++)
        {
            _values[i] = new object?[_levels.Length];
            for (int level = 0; level < _levels.Length; level++)
            {
                var code = _codes[level][i];
                _values[i][level] = code >= 0 ? _levels[level].GetValue(code) : null;
            }
        }
    }

    /// <summary>
    /// 튜플 배열로부터 MultiIndex 생성
    /// todo.yaml에서 정의된 from_tuples 생성자
    /// </summary>
    public static MultiIndex FromTuples(IEnumerable<object[]> tuples, string[]? names = null)
    {
        var tupleList = tuples.ToList();
        if (!tupleList.Any())
            throw new ArgumentException("Tuples cannot be empty");

        var levelCount = tupleList[0].Length;
        if (tupleList.Any(t => t.Length != levelCount))
            throw new ArgumentException("All tuples must have the same length");

        // 각 레벨별로 고유값들 수집
        var levelValues = new List<object>[levelCount];
        var levelMaps = new Dictionary<object, int>[levelCount];

        for (int level = 0; level < levelCount; level++)
        {
            levelValues[level] = new List<object>();
            levelMaps[level] = new Dictionary<object, int>();

            var uniqueValues = tupleList.Select(t => t[level]).Distinct().ToList();
            for (int i = 0; i < uniqueValues.Count; i++)
            {
                levelValues[level].Add(uniqueValues[i]);
                levelMaps[level][uniqueValues[i]] = i;
            }
        }

        // Index 배열 생성
        var levels = new Index[levelCount];
        for (int level = 0; level < levelCount; level++)
        {
            levels[level] = CreateIndexFromValues(levelValues[level]);
        }

        // 코드 배열 생성
        var codes = new int[levelCount][];
        for (int level = 0; level < levelCount; level++)
        {
            codes[level] = new int[tupleList.Count];
            for (int i = 0; i < tupleList.Count; i++)
            {
                codes[level][i] = levelMaps[level][tupleList[i][level]];
            }
        }

        return new MultiIndex(levels, codes, names);
    }

    /// <summary>
    /// 배열들로부터 MultiIndex 생성
    /// todo.yaml에서 정의된 from_arrays 생성자
    /// </summary>
    public static MultiIndex FromArrays(IEnumerable<object[]> arrays, string[]? names = null)
    {
        var arrayList = arrays.ToList();
        if (!arrayList.Any())
            throw new ArgumentException("Arrays cannot be empty");

        var length = arrayList[0].Length;
        if (arrayList.Any(arr => arr.Length != length))
            throw new ArgumentException("All arrays must have the same length");

        var tuples = new object[length][];
        for (int i = 0; i < length; i++)
        {
            tuples[i] = new object[arrayList.Count];
            for (int level = 0; level < arrayList.Count; level++)
            {
                tuples[i][level] = arrayList[level][i];
            }
        }

        return FromTuples(tuples, names);
    }

    /// <summary>
    /// 레벨 순서 변경
    /// todo.yaml에서 정의된 swaplevel 메서드
    /// </summary>
    public MultiIndex SwapLevel(int i = -2, int j = -1)
    {
        // 음수 인덱스 처리
        if (i < 0) i = _levels.Length + i;
        if (j < 0) j = _levels.Length + j;

        if (i < 0 || i >= _levels.Length || j < 0 || j >= _levels.Length)
            throw new IndexOutOfRangeException("Level index out of range");

        var newLevels = (Index[])_levels.Clone();
        var newCodes = new int[_codes.Length][];
        var newNames = _names?.ToArray();

        // 레벨과 코드 교체
        (newLevels[i], newLevels[j]) = (newLevels[j], newLevels[i]);
        for (int level = 0; level < _codes.Length; level++)
        {
            newCodes[level] = (int[])_codes[level].Clone();
        }
        (newCodes[i], newCodes[j]) = (newCodes[j], newCodes[i]);

        // 이름 교체
        if (newNames != null)
        {
            (newNames[i], newNames[j]) = (newNames[j], newNames[i]);
        }

        return new MultiIndex(newLevels, newCodes, newNames);
    }

    /// <summary>
    /// 레벨 순서 재정렬
    /// todo.yaml에서 정의된 reorder_levels 메서드
    /// </summary>
    public MultiIndex ReorderLevels(int[] order)
    {
        if (order.Length != _levels.Length)
            throw new ArgumentException("Order array must have the same length as levels");

        if (order.Distinct().Count() != order.Length)
            throw new ArgumentException("Order array must contain unique values");

        if (order.Any(o => o < 0 || o >= _levels.Length))
            throw new ArgumentException("Order values must be valid level indices");

        var newLevels = new Index[_levels.Length];
        var newCodes = new int[_codes.Length][];
        var newNames = new string[_levels.Length];

        for (int i = 0; i < order.Length; i++)
        {
            newLevels[i] = _levels[order[i]];
            newCodes[i] = (int[])_codes[order[i]].Clone();
            if (_names != null)
                newNames[i] = _names[order[i]];
        }

        return new MultiIndex(newLevels, newCodes, _names != null ? newNames : null);
    }

    public override object GetValue(int position)
    {
        if (position < 0 || position >= Length)
            throw new IndexOutOfRangeException();

        return _values[position];
    }

    public override int GetPosition(object value)
    {
        if (value is not object[] tuple)
            return -1;

        if (tuple.Length != _levels.Length)
            return -1;

        for (int pos = 0; pos < Length; pos++)
        {
            bool match = true;
            for (int level = 0; level < _levels.Length; level++)
            {
                if (!Equals(_values[pos][level], tuple[level]))
                {
                    match = false;
                    break;
                }
            }
            if (match) return pos;
        }

        return -1;
    }

    public override bool Contains(object value)
    {
        return GetPosition(value) >= 0;
    }

    public override Index Slice(int start, int length)
    {
        if (start < 0 || start >= Length)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length < 0 || start + length > Length)
            throw new ArgumentOutOfRangeException(nameof(length));

        var newCodes = new int[_codes.Length][];
        for (int level = 0; level < _codes.Length; level++)
        {
            newCodes[level] = new int[length];
            Array.Copy(_codes[level], start, newCodes[level], 0, length);
        }

        return new MultiIndex(_levels, newCodes, _names);
    }
    
    public override Index Reorder(int[] indices)
    {
        var newCodes = new int[_codes.Length][];
        for (int level = 0; level < _codes.Length; level++)
        {
            newCodes[level] = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                newCodes[level][i] = _codes[level][indices[i]];
            }
        }
        return new MultiIndex(_levels, newCodes, _names);
    }
    
    public override int[] Argsort(bool ascending = true)
    {
        var indices = Enumerable.Range(0, Length).ToArray();
        Array.Sort(indices, (a, b) =>
        {
            for (int level = 0; level < _levels.Length; level++)
            {
                var valA = _values[a][level];
                var valB = _values[b][level];
                
                int cmp = 0;
                if (valA == null && valB == null) cmp = 0;
                else if (valA == null) cmp = -1;
                else if (valB == null) cmp = 1;
                else if (valA is IComparable cA) cmp = cA.CompareTo(valB);
                else cmp = 0; // Comparable 아니면 동급 처리
                
                if (cmp != 0) return ascending ? cmp : -cmp;
            }
            return 0;
        });
        return indices;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("MultiIndex([");

        for (int i = 0; i < Math.Min(Length, 10); i++) // 최대 10개만 표시
        {
            sb.Append("  (");
            for (int level = 0; level < _levels.Length; level++)
            {
                if (level > 0) sb.Append(", ");
                sb.Append(_values[i][level]?.ToString() ?? "null");
            }
            sb.AppendLine(")");
        }

        if (Length > 10)
            sb.AppendLine("  ...");

        sb.Append("],");
        if (_names != null)
        {
            sb.Append($" names=[{string.Join(", ", _names.Select(n => $"'{n}'"))}]");
        }
        sb.Append(")");

        return sb.ToString();
    }

    /// <summary>
    /// 값들로부터 적절한 Index 타입 생성
    /// </summary>
    private static Index CreateIndexFromValues(List<object> values)
    {
        if (values.All(v => v is int))
        {
            return new StringIndex(values.Cast<int>().Select(i => i.ToString()).ToArray());
        }
        else if (values.All(v => v is string))
        {
            return new StringIndex(values.Cast<string>().ToArray());
        }
        else if (values.All(v => v is DateTime))
        {
            return new StringIndex(values.Cast<DateTime>().Select(d => d.ToString()).ToArray());
        }
        else
        {
            return new StringIndex(values.Select(v => v?.ToString() ?? "").ToArray());
        }
    }
}