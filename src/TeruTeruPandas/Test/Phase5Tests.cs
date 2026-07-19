using System;
using System.Collections.Generic;
using System.Linq;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;

namespace TeruTeruPandas.Test;

public static class Phase5Tests
{
    public static void Run()
    {
        Console.WriteLine("=== Phase 5: Time Series Analysis Tests ===");
        
        TestShift();
        TestRolling();
        TestResample();
        TestDateTimeProperties();
        
        Console.WriteLine("=== Phase 5 Tests Completed Successfully ===");
    }

    private static void TestShift()
    {
        Console.WriteLine("\n[1] Shifting Data");
        
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "A", new PrimitiveColumn<int>(new[] { 1, 2, 3, 4, 5 }) }
        });
        
        var shifted = df.Shift(1);
        Console.WriteLine("Original A: [1, 2, 3, 4, 5]");
        Console.WriteLine("Shifted A:  [{0}, {1}, {2}, {3}, {4}]", 
            shifted["A"].IsNA(0) ? "NA" : shifted["A"].GetValue(0),
            shifted["A"].IsNA(1) ? "NA" : shifted["A"].GetValue(1),
            shifted["A"].IsNA(2) ? "NA" : shifted["A"].GetValue(2),
            shifted["A"].IsNA(3) ? "NA" : shifted["A"].GetValue(3),
            shifted["A"].IsNA(4) ? "NA" : shifted["A"].GetValue(4));

        if (shifted["A"].IsNA(0) && (int)shifted["A"].GetValue(1)! == 1)
            Console.WriteLine("✅ Shift passed");
        else
            Console.WriteLine("❌ Shift failed");
    }

    private static void TestRolling()
    {
        Console.WriteLine("\n[2] Rolling Window Mean");
        
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "Price", new PrimitiveColumn<double>(new[] { 100.0, 110.0, 120.0, 130.0, 140.0 }) }
        });
        
        var rollingMean = df.Rolling(window: 3, minPeriods: 1).Mean();
        Console.WriteLine("Rolling Mean (window=3):\n{0}", rollingMean);

        // [100/1, (100+110)/2, (100+110+120)/3, (110+120+130)/3, (120+130+140)/3]
        // [100.0, 105.0, 110.0, 120.0, 130.0]
        double lastVal = (double)rollingMean["Price"].GetValue(4)!;
        if (Math.Abs(lastVal - 130.0) < 0.0001)
            Console.WriteLine("✅ Rolling Mean passed");
        else
            Console.WriteLine("❌ Rolling Mean failed");
    }

    private static void TestResample()
    {
        Console.WriteLine("\n[3] DateTime Resampling");
        
        var dates = new[]
        {
            new DateTime(2023, 1, 1, 10, 0, 0),
            new DateTime(2023, 1, 1, 11, 0, 0),
            new DateTime(2023, 1, 2, 10, 0, 0),
            new DateTime(2023, 1, 2, 15, 0, 0),
            new DateTime(2023, 1, 3, 08, 0, 0)
        };
        
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "Time", new PrimitiveColumn<DateTime>(dates) },
            { "Val", new PrimitiveColumn<double>(new[] { 10.0, 20.0, 30.0, 40.0, 50.0 }) }
        });
        
        var resampled = df.Resample("D", on: "Time").Mean();
        Console.WriteLine("Daily Resampled Mean:\n{0}", resampled);

        // 2023-01-01: (10+20)/2 = 15.0
        // 2023-01-02: (30+40)/2 = 35.0
        // 2023-01-03: 50.0
        if (resampled.RowCount == 3 && (double)resampled["Val"].GetValue(0)! == 15.0)
            Console.WriteLine("✅ Resampling passed");
        else
            Console.WriteLine("❌ Resampling failed");
    }

    private static void TestDateTimeProperties()
    {
        Console.WriteLine("\n[4] DateTime .dt Properties");
        
        var dates = new[] { new DateTime(2024, 2, 29), new DateTime(2023, 1, 1) };
        var s = new Series<DateTime>(dates, name: "dates");
        
        var isLeap = s.Dt.IsLeapYear;
        var isMonthStart = s.Dt.IsMonthStart;
        
        Console.WriteLine("2024 is Leap: {0}", isLeap[0]);
        Console.WriteLine("Jan 1 is Month Start: {0}", isMonthStart[1]);

        if (isLeap[0] == true && isLeap[1] == false && isMonthStart[1] == true)
             Console.WriteLine("✅ .dt Properties passed");
        else
             Console.WriteLine("❌ .dt Properties failed");
    }
}
