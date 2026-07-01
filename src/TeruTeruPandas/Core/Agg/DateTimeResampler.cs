using System;
using System.Collections.Generic;
using System.Linq;
using TeruTeruPandas.Core.Column;

namespace TeruTeruPandas.Core.Agg;

/// <summary>
/// 시계열 데이터 리샘플링 (D, H, Min, S 등)
/// </summary>
public class DateTimeResampler
{
    private readonly DataFrame _df;
    private readonly string _rule;
    private readonly string? _timeColumn;

    public DateTimeResampler(DataFrame df, string rule, string? timeColumn = null)
    {
        _df = df;
        _rule = rule.ToUpper();
        _timeColumn = timeColumn;
    }

    public DataFrame Mean()
    {
        return Aggregate("mean");
    }

    public DataFrame Sum()
    {
        return Aggregate("sum");
    }

    public DataFrame Count()
    {
        return Aggregate("count");
    }

    private DataFrame Aggregate(string func)
    {
        // 1. 시계열 컬럼 확보
        IColumn timeCol;
        if (_timeColumn != null)
        {
            timeCol = _df[_timeColumn];
        }
        else
        {
            // 인덱스에서 찾거나, 첫 번째 DateTime 컬럼 사용 (여기선 첫 번째 탐색)
            var dtCols = _df.Columns.Where(c => _df[c].DataType == typeof(DateTime)).ToList();
            if (dtCols.Count == 0)
                throw new InvalidOperationException("No DateTime column found for resampling");
            timeCol = _df[dtCols[0]];
        }

        if (timeCol.DataType != typeof(DateTime))
            throw new InvalidOperationException("Resampling requires a DateTime column");

        // 2. 리샘플링 버킷 생성
        var buckets = new Dictionary<DateTime, List<int>>();
        for (int i = 0; i < timeCol.Length; i++)
        {
            if (timeCol.IsNA(i)) continue;
            
            var dt = (DateTime)timeCol.GetValue(i)!;
            var bucketKey = GetBucketKey(dt);

            if (!buckets.ContainsKey(bucketKey))
                buckets[bucketKey] = new List<int>();
            buckets[bucketKey].Add(i);
        }

        // 3. 그룹별 집계 (GroupBy와 유사한 로직)
        var sortedKeys = buckets.Keys.OrderBy(k => k).ToList();
        var resultColumns = new Dictionary<string, IColumn>();
        
        // 시간축 컬럼 추가
        var timeData = sortedKeys.ToArray();
        resultColumns["index"] = new PrimitiveColumn<DateTime>(timeData);

        foreach (var colName in _df.Columns)
        {
            if (colName == _timeColumn || _df[colName].DataType == typeof(DateTime)) continue;

            var sourceCol = _df[colName];
            if (sourceCol.DataType != typeof(int) && sourceCol.DataType != typeof(double)) continue;

            var aggregatedData = new double[sortedKeys.Count];
            var naMask = new bool[sortedKeys.Count];

            for (int i = 0; i < sortedKeys.Count; i++)
            {
                var rowIndices = buckets[sortedKeys[i]];
                double result = 0;
                int validCount = 0;

                foreach (var idx in rowIndices)
                {
                    if (!sourceCol.IsNA(idx))
                    {
                        var val = Convert.ToDouble(sourceCol.GetValue(idx));
                        if (func == "mean" || func == "sum") result += val;
                        validCount++;
                    }
                }

                if (validCount == 0)
                {
                    naMask[i] = true;
                }
                else
                {
                    if (func == "mean") aggregatedData[i] = result / validCount;
                    else if (func == "sum") aggregatedData[i] = result;
                    else if (func == "count") aggregatedData[i] = validCount;
                }
            }

            resultColumns[colName] = new PrimitiveColumn<double>(aggregatedData, naMask);
        }

        return new DataFrame(resultColumns);
    }

    private DateTime GetBucketKey(DateTime dt)
    {
        return _rule switch
        {
            "D" => new DateTime(dt.Year, dt.Month, dt.Day),
            "H" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0),
            "T" or "MIN" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0),
            "S" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second),
            "M" => new DateTime(dt.Year, dt.Month, 1),
            "Y" => new DateTime(dt.Year, 1, 1),
            _ => throw new ArgumentException($"Unsupported resampling rule: {_rule}")
        };
    }
}
