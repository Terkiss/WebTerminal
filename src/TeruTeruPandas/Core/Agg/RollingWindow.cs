using System.Collections.Generic;
using TeruTeruPandas.Core.Column;

namespace TeruTeruPandas.Core.Agg;

/// <summary>
/// 이동평균, 이동합계 등 슬라이딩 윈도우 연산 지원
/// </summary>
public class RollingWindow
{
    private readonly DataFrame _df;
    private readonly int _window;
    private readonly int _minPeriods;

    public RollingWindow(DataFrame df, int window, int? minPeriods = null)
    {
        _df = df;
        _window = window;
        _minPeriods = minPeriods ?? window;
    }

    public DataFrame Mean()
    {
        var resultColumns = new Dictionary<string, IColumn>();
        int rowCount = _df.Index.Length;

        foreach (var columnName in _df.Columns)
        {
            var column = _df[columnName];
            if (column.DataType == typeof(int) || column.DataType == typeof(double))
            {
                resultColumns[columnName] = CalculateRollingMean(column);
            }
            else
            {
                // 수치형이 아니면 결과에서 제외하거나 NA로 채움
                resultColumns[columnName] = new PrimitiveColumn<double>(rowCount).Shift(0); // All NA dummy
            }
        }

        return new DataFrame(resultColumns, _df.Index);
    }

    private IColumn CalculateRollingMean(IColumn column)
    {
        int rowCount = column.Length;
        var resultData = new double[rowCount];
        var naMask = new bool[rowCount];

        for (int i = 0; i < rowCount; i++)
        {
            double sum = 0;
            int count = 0;
            int validCount = 0;

            for (int j = i - _window + 1; j <= i; j++)
            {
                if (j >= 0 && j < rowCount)
                {
                    count++;
                    if (!column.IsNA(j))
                    {
                        sum += Convert.ToDouble(column.GetValue(j));
                        validCount++;
                    }
                }
            }

            if (validCount < _minPeriods)
            {
                naMask[i] = true;
            }
            else
            {
                resultData[i] = sum / validCount;
            }
        }

        return new PrimitiveColumn<double>(resultData, naMask);
    }
    
    public DataFrame Sum()
    {
        var resultColumns = new Dictionary<string, IColumn>();
        int rowCount = _df.Index.Length;

        foreach (var columnName in _df.Columns)
        {
            var column = _df[columnName];
            if (column.DataType == typeof(int) || column.DataType == typeof(double))
            {
                resultColumns[columnName] = CalculateRollingSum(column);
            }
            else
            {
                resultColumns[columnName] = new PrimitiveColumn<double>(rowCount).Shift(0);
            }
        }

        return new DataFrame(resultColumns, _df.Index);
    }

    private IColumn CalculateRollingSum(IColumn column)
    {
        int rowCount = column.Length;
        var resultData = new double[rowCount];
        var naMask = new bool[rowCount];

        for (int i = 0; i < rowCount; i++)
        {
            double sum = 0;
            int validCount = 0;

            for (int j = i - _window + 1; j <= i; j++)
            {
                if (j >= 0 && j < rowCount)
                {
                    if (!column.IsNA(j))
                    {
                        sum += Convert.ToDouble(column.GetValue(j));
                        validCount++;
                    }
                }
            }

            if (validCount < _minPeriods)
            {
                naMask[i] = true;
            }
            else
            {
                resultData[i] = sum;
            }
        }

        return new PrimitiveColumn<double>(resultData, naMask);
    }
}
