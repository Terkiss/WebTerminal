using TeruTeruPandas.Core.Column;

namespace TeruTeruPandas.Core;

/// <summary>
/// Series.dt 접근자 구현 (Pandas 스타일)
/// </summary>
public class DateTimeProperties
{
    private readonly PrimitiveColumn<DateTime> _column;
    private readonly Index.Index _index;

    public DateTimeProperties(IColumn column, Index.Index index)
    {
        if (column.DataType != typeof(DateTime))
            throw new InvalidOperationException("Can only access .dt accessor on DateTime column");

        if (column is PrimitiveColumn<DateTime> primitiveCol)
        {
            _column = primitiveCol;
        }
        else
        {
             throw new InvalidOperationException("Column must be PrimitiveColumn<DateTime>");
        }
        
        _index = index;
    }

    public Series<int> Year => GetProperty(dt => dt.Year);
    public Series<int> Month => GetProperty(dt => dt.Month);
    public Series<int> Day => GetProperty(dt => dt.Day);
    public Series<int> Hour => GetProperty(dt => dt.Hour);
    public Series<int> Minute => GetProperty(dt => dt.Minute);
    public Series<int> Second => GetProperty(dt => dt.Second);
    public Series<int> DayOfWeek => GetProperty(dt => (int)dt.DayOfWeek);
    public Series<int> DayOfYear => GetProperty(dt => dt.DayOfYear);
    public Series<int> Quarter => GetProperty(dt => (dt.Month - 1) / 3 + 1);
    public Series<bool> IsLeapYear => GetBoolProperty(dt => DateTime.IsLeapYear(dt.Year));
    public Series<bool> IsMonthStart => GetBoolProperty(dt => dt.Day == 1);
    public Series<bool> IsMonthEnd => GetBoolProperty(dt => dt.Day == DateTime.DaysInMonth(dt.Year, dt.Month));

    private Series<int> GetProperty(Func<DateTime, int> selector)
    {
        var result = new int[_column.Length];
        
        for (int i = 0; i < _column.Length; i++)
        {
            if (!_column.IsNA(i))
            {
                var val = (DateTime)_column.GetValue(i)!;
                result[i] = selector(val);
            }
        }
        
        var series = new Series<int>(result, _index);
        
        for (int i = 0; i < _column.Length; i++)
        {
            if (_column.IsNA(i)) series.SetNA(i);
        }
        
        return series;
    }

    private Series<bool> GetBoolProperty(Func<DateTime, bool> selector)
    {
        var result = new bool[_column.Length];
        
        for (int i = 0; i < _column.Length; i++)
        {
            if (!_column.IsNA(i))
            {
                var val = (DateTime)_column.GetValue(i)!;
                result[i] = selector(val);
            }
        }
        
        var series = new Series<bool>(result, _index);
        
        for (int i = 0; i < _column.Length; i++)
        {
            if (_column.IsNA(i)) series.SetNA(i);
        }
        
        return series;
    }
}
