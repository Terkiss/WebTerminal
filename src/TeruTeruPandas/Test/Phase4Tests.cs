using System;
using System.Collections.Generic;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Agg;

namespace TeruTeruPandas.Test;

public static class Phase4Tests
{
    public static void Run()
    {
        try
        {
            Console.WriteLine("=== Phase 4: Advanced Data Manipulation Tests ===");
            
            TestGroupByMean();
            TestGroupByMultiColumn();
            TestPivot();
            TestMelt();
            TestMerge();
            
            Console.WriteLine("=== Phase 4 Tests Completed Successfully ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Phase 4 Tests Failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        Console.Out.Flush();
    }
    
    private static void TestGroupByMean()
    {
        Console.WriteLine("Running TestGroupByMean...");
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "Group", new StringColumn(new[] { "A", "A", "B", "B", "A" }) },
            { "Value", new PrimitiveColumn<int>(new[] { 10, 20, 30, 40, 60 }) }
        });
        
        var grouped = df.GroupBy("Group");
        var result = grouped.Agg(new Dictionary<string, string[]> { { "Value", new[] { "mean" } } });
        
        var meanCol = (PrimitiveColumn<double>)result["Value_mean"]; // Assumes double due to "mean"
        Assert(result.RowCount == 2, "Group count 2");
    }

    private static void TestGroupByMultiColumn()
    {
        Console.WriteLine("Running TestGroupByMultiColumn...");
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "Key1", new StringColumn(new[] { "A", "A", "A", "B", "B" }) },
            { "Key2", new StringColumn(new[] { "X", "X", "Y", "X", "Y" }) },
            { "Val", new PrimitiveColumn<double>(new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }) }
        });
        
        var grouped = df.GroupBy(new[] { "Key1", "Key2" });
        var result = grouped.Agg(new Dictionary<string, string[]> { { "Val", new[] { "mean" } } });
        Assert(result.RowCount == 4, "Group count 4");
    }

    private static void TestPivot()
    {
        Console.WriteLine("Running TestPivot...");
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "Date", new StringColumn(new[] { "2023-01", "2023-01", "2023-02", "2023-02" }) },
            { "City", new StringColumn(new[] { "Seoul", "Busan", "Seoul", "Busan" }) },
            { "Temp", new PrimitiveColumn<double>(new[] { -5.0, 2.0, -2.0, 5.0 }) }
        });
        
        var pivoted = df.Pivot("Date", "City", "Temp");
        Assert(pivoted.Columns.Length == 3, "Pivot columns");
    }

    private static void TestMelt()
    {
        Console.WriteLine("Running TestMelt...");
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "Name", new StringColumn(new[] { "A", "B" }) },
            { "Math", new PrimitiveColumn<int>(new[] { 90, 80 }) },
            { "Eng", new PrimitiveColumn<int>(new[] { 70, 60 }) }
        });
        
        var melted = df.Melt(new[] { "Name" }, new[] { "Math", "Eng" }, "Subject", "Score");
        Assert(melted.RowCount == 4, "Melt rows");
    }

    private static void TestMerge()
    {
        Console.WriteLine("Running TestMerge...");
        
        var dfLeft = new DataFrame(new Dictionary<string, IColumn>
        {
            { "Key", new StringColumn(new[] { "K0", "K1", "K2" }) },
            { "A", new StringColumn(new[] { "A0", "A1", "A2" }) }
        });
        
        var dfRight = new DataFrame(new Dictionary<string, IColumn>
        {
            { "Key", new StringColumn(new[] { "K0", "K1", "K3" }) },
            { "B", new StringColumn(new[] { "B0", "B1", "B3" }) }
        });
        
        // Simple Inner Join
        var merged = dfLeft.Merge(dfRight, "Key", "inner");
        Console.WriteLine($"Merged RowCount: {merged.RowCount}");
        Assert(merged.RowCount == 2, "Merged RowCount is 2");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.WriteLine($"[FAIL] {message}");
            throw new Exception($"Test Failed: {message}");
        }
        Console.WriteLine($"[PASS] {message}");
    }
}
