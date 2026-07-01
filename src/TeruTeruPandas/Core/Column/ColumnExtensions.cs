using TeruTeruPandas.Core.Column;

namespace TeruTeruPandas.Core;

/// <summary>
/// IColumn에 대한 확장 메서드 - 비교 연산 지원
/// </summary>
public static class ColumnExtensions
{
    public static BoolSeries Gte(this IColumn column, object value)
    {
        var result = new bool[column.Length];
        for (int i = 0; i < column.Length; i++)
        {
            if (!column.IsNA(i))
            {
                var columnValue = column.GetValue(i);
                result[i] = CompareValues(columnValue, value, (a, b) => Compare(a, b) >= 0);
            }
        }
        return new BoolSeries(result);
    }
    
    public static BoolSeries Lte(this IColumn column, object value)
    {
        var result = new bool[column.Length];
        for (int i = 0; i < column.Length; i++)
        {
            if (!column.IsNA(i))
            {
                var columnValue = column.GetValue(i);
                result[i] = CompareValues(columnValue, value, (a, b) => Compare(a, b) <= 0);
            }
        }
        return new BoolSeries(result);
    }
    
    public static BoolSeries Gt(this IColumn column, object value)
    {
        var result = new bool[column.Length];
        for (int i = 0; i < column.Length; i++)
        {
            if (!column.IsNA(i))
            {
                var columnValue = column.GetValue(i);
                result[i] = CompareValues(columnValue, value, (a, b) => Compare(a, b) > 0);
            }
        }
        return new BoolSeries(result);
    }
    
    public static BoolSeries Lt(this IColumn column, object value)
    {
        var result = new bool[column.Length];
        for (int i = 0; i < column.Length; i++)
        {
            if (!column.IsNA(i))
            {
                var columnValue = column.GetValue(i);
                result[i] = CompareValues(columnValue, value, (a, b) => Compare(a, b) < 0);
            }
        }
        return new BoolSeries(result);
    }
    
    private static bool CompareValues(object? columnValue, object value, Func<object?, object, bool> comparer)
    {
        try
        {
            return comparer(columnValue, value);
        }
        catch
        {
            return false;
        }
    }
    
    private static int Compare(object? a, object? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;
        
        if (a is IComparable comparableA)
        {
            return comparableA.CompareTo(b);
        }
        
        throw new ArgumentException("Values are not comparable");
    }
    
    // DateTime Accessor
    public static DateTimeProperties Dt(this IColumn column)
    {
        // IColumn은 인덱스 정보가 없으므로 기본 RangeIndex 사용
        // 정확한 인덱스 유지를 위해선 Series를 사용해야 함
        return new DateTimeProperties(column, new Core.Index.RangeIndex(column.Length));
    }
}
