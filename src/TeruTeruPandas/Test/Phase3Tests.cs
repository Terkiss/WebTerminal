using System;
using System.Collections.Generic;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Agg;

namespace TeruTeruPandas.Test;

public static class Phase3Tests
{
    public static void Run()
    {
        try
        {
            Console.WriteLine("=== Phase 3: Numerical Operations Tests ===");
            Console.Out.Flush();
            
            TestPrimitiveColumnArithmetic();
            TestTypePromotion();
            TestDataFrameArithmetic();
            TestDataFrameScalarArithmetic();
            TestPowOperations();
            TestStatisticalFunctions();
            
            Console.WriteLine("=== Phase 3 Tests Completed Successfully ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Phase 3 Tests Failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        Console.Out.Flush();
    }
    
    private static void TestPrimitiveColumnArithmetic()
    {
        Console.WriteLine("Running TestPrimitiveColumnArithmetic...");
        
        var col1 = new PrimitiveColumn<int>(new[] { 1, 2, 3, 4 });
        var col2 = new PrimitiveColumn<int>(new[] { 10, 20, 30, 40 });
        
        var sum = col1.Add(col2);
        Assert(sum.GetValue(0).Equals(11), "1+10 == 11");
        Assert(sum.GetValue(3).Equals(44), "4+40 == 44");
        Assert(sum.DataType == typeof(int), "int+int should be int");
        
        var sub = col2.Sub(col1);
        Assert(sub.GetValue(0).Equals(9), "10-1 == 9");
        
        var mul = col1.Mul(col2);
        Assert(mul.GetValue(1).Equals(40), "2*20 == 40");
        
        var div = col2.Div(col1);
        Assert(div.GetValue(1).Equals(10), "20/2 == 10");
        
        // Test NA
        var colNA = new PrimitiveColumn<int>(new[] { 1, 2, 3, 4 }, new[] { false, true, false, false }); // index 1 is NA
        var sumNA = col1.Add(colNA);
        Assert(sumNA.IsNA(1), "1+NA should be NA");
        Assert(sumNA.GetValue(0).Equals(2), "1+1=2");
    }
    
    private static void TestTypePromotion()
    {
        Console.WriteLine("Running TestTypePromotion...");
        
        var intCol = new PrimitiveColumn<int>(new[] { 1, 2, 3 });
        var doubleCol = new PrimitiveColumn<double>(new[] { 1.5, 2.5, 3.5 });
        
        var sum = intCol.Add(doubleCol);
        Assert(sum.DataType == typeof(double), "int + double -> double");
        Assert(((double)sum.GetValue(0)).Equals(2.5), "1 + 1.5 == 2.5");
    }
    
    private static void TestDataFrameArithmetic()
    {
        Console.WriteLine("Running TestDataFrameArithmetic...");
        
        var df1 = new DataFrame(new Dictionary<string, IColumn>
        {
            { "A", new PrimitiveColumn<int>(new[] { 1, 2, 3 }) },
            { "B", new PrimitiveColumn<double>(new[] { 1.1, 2.2, 3.3 }) }
        });
        
        var df2 = new DataFrame(new Dictionary<string, IColumn>
        {
            { "A", new PrimitiveColumn<int>(new[] { 10, 20, 30 }) },
            { "B", new PrimitiveColumn<double>(new[] { 0.1, 0.2, 0.3 }) }
        });
        
        var result = df1.Add(df2);
        
        Assert(result["A"].GetValue(0).Equals(11), "DF Add A[0]");
        Assert(Math.Abs((double)result["B"].GetValue(1) - 2.4) < 0.0001, "DF Add B[1]");
        
        // Test FillValue (implicitly creates col)
        var df3 = new DataFrame(new Dictionary<string, IColumn>
        {
            { "A", new PrimitiveColumn<int>(new[] { 1, 2, 3 }) } // Missing B
        });
        
        // df1 has B, df3 missing B
        var resultFill = df1.Add(df3, fillValue: 0.0);
        // df1.B + (df3.B which is missing -> 0.0) = B
        Assert(Math.Abs((double)resultFill["B"].GetValue(0) - 1.1) < 0.0001, "FillValue logic correct on missing col");
    }
    
    private static void TestDataFrameScalarArithmetic()
    {
        Console.WriteLine("Running TestDataFrameScalarArithmetic...");
        
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "A", new PrimitiveColumn<int>(new[] { 1, 2, 3 }) }
        });
        
        var res = df.Add(10);
        Assert(res["A"].GetValue(0).Equals(11), "DF + Scalar");
        
        var resFloated = df.Add(1.5);
        Assert(resFloated["A"].DataType == typeof(double), "DF(int) + 1.5 should promote to double");
        Assert(((double)resFloated["A"].GetValue(0)).Equals(2.5), "1 + 1.5 == 2.5");
    }

    private static void TestPowOperations()
    {
        Console.WriteLine("Running TestPowOperations...");
        
        var col = new PrimitiveColumn<int>(new[] { 2, 3, 4 });
        var pow2 = col.Pow(2);
        
        Assert(pow2.DataType == typeof(double), "int^int scalar result is double"); 
        Assert(((double)pow2.GetValue(0)).Equals(4.0), "2^2=4");
        Assert(((double)pow2.GetValue(1)).Equals(9.0), "3^2=9");
        
        var colDouble = new PrimitiveColumn<double>(new[] { 2.0, 3.0, 4.0 });
        var powCol = colDouble.Pow(new PrimitiveColumn<double>(new[] { 3.0, 2.0, 0.5 }));
        
        Assert(Math.Abs((double)powCol.GetValue(0) - 8.0) < 0.0001, "2.0^3.0 = 8.0");
        Assert(Math.Abs((double)powCol.GetValue(1) - 9.0) < 0.0001, "3.0^2.0 = 9.0");
        Assert(Math.Abs((double)powCol.GetValue(2) - 2.0) < 0.0001, "4.0^0.5 = 2.0");
        
        var colIntPowers = new PrimitiveColumn<int>(new[] { 3, 2, 1 });
        var powIntRes = col.Pow(colIntPowers);
        Assert(Math.Abs((double)powIntRes.GetValue(0) - 8.0) < 0.0001, "2^3=8 (int promoted)");
    }
    
    
    private static void TestStatisticalFunctions()
    {
        Console.WriteLine("Running TestStatisticalFunctions...");
        
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "A", new PrimitiveColumn<int>(new[] { 1, 2, 3, 4, 5 }) }, // Mean=3, Var=2.5
            { "B", new PrimitiveColumn<double>(new[] { 10.0, 20.0, 30.0, 40.0, 50.0 }) }, // Mean=30
            { "C", new PrimitiveColumn<int>(new[] { 1, 2, 100, 0, 0 }, new[] { false, false, true, true, true }) } // 1, 2, NA, NA, NA. Mean=1.5
        });
        
        var mean = df.Mean();
        Assert(Math.Abs((double)mean["A"] - 3.0) < 0.0001, "Mean A");
        Assert(Math.Abs((double)mean["B"] - 30.0) < 0.0001, "Mean B");
        Assert(Math.Abs((double)mean["C"] - 1.5) < 0.0001, "Mean C (ignore NA)");
        
        var median = df.Median();
        Assert(Math.Abs((double)median["A"] - 3.0) < 0.0001, "Median A (3)");
        // C: 1, 2 -> 1.5
        Assert(Math.Abs((double)median["C"] - 1.5) < 0.0001, "Median C");

        var var = df.Var();
        Assert(Math.Abs((double)var["A"] - 2.5) < 0.0001, "Var A");
        
        var std = df.Std();
        Assert(Math.Abs((double)std["A"] - Math.Sqrt(2.5)) < 0.0001, "Std A");
        
        var quant = df.Quantile(0.25);
        // A: 1,2,3,4,5. 0.25 * 4 = 1. Index 1 -> 2.
        Assert(Math.Abs((double)quant["A"] - 2.0) < 0.0001, "Quantile 0.25 A");
        
        var max = df.Max();
        Assert(Math.Abs((double)max["A"] - 5.0) < 0.0001, "Max A");
        
        var min = df.Min();
        Assert(Math.Abs((double)min["A"] - 1.0) < 0.0001, "Min A");
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
