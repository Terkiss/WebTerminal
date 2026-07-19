using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.Test;

public static class Phase2Tests
{
    public static void Run()
    {
        Console.WriteLine("=== Phase 2 Tests: Missing Data Handling ===");
        
        TestFillNA_Value();
        TestFillNA_Methods();
        TestDropNA_Enhanced();
        
        Console.WriteLine("=== Phase 2 Tests Complete ===");
    }

    private static void TestFillNA_Value()
    {
        Console.WriteLine("\n[1] FillNA(value)");
        
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "A", new PrimitiveColumn<int>(new int[] { 1, 0, 3 }, new bool[] { false, true, false }) }, // 1, NA, 3
            { "B", new StringColumn(new string[] { "a", "b", "" }, new bool[] { false, false, true }) } // a, b, NA
        });
        
        Console.WriteLine("Original:\n{0}", df);
        
        var filled = df.FillNA(999);
        Console.WriteLine("Filled with 999:\n{0}", filled);
        
        var colA = (PrimitiveColumn<int>)filled["A"];
        if ((int)colA.GetValue(1)! == 999) 
            Console.WriteLine("✅ FillNA(int) passed");
        else
            Console.WriteLine("❌ FillNA(int) failed");
    }

    private static void TestFillNA_Methods()
    {
        Console.WriteLine("\n[2] FillNA(ffill/bfill)");
        
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "val", new PrimitiveColumn<int>(new int[] { 10, 0, 0, 20 }, new bool[] { false, true, true, false }) } // 10, NA, NA, 20
        });
        
        Console.WriteLine("Original:\n{0}", df);
        
        var ffill = df.FillNA("ffill");
        Console.WriteLine("FFill:\n{0}", ffill);
        
        var ffillCol = (PrimitiveColumn<int>)ffill["val"];
        if ((int)ffillCol.GetValue(1)! == 10 && (int)ffillCol.GetValue(2)! == 10)
             Console.WriteLine("✅ FFill passed");
        else
             Console.WriteLine("❌ FFill failed");

        var bfill = df.FillNA("bfill");
        Console.WriteLine("BFill:\n{0}", bfill);
        
        var bfillCol = (PrimitiveColumn<int>)bfill["val"];
        if ((int)bfillCol.GetValue(1)! == 20 && (int)bfillCol.GetValue(2)! == 20)
             Console.WriteLine("✅ BFill passed");
        else
             Console.WriteLine("❌ BFill failed");
    }

    private static void TestDropNA_Enhanced()
    {
        Console.WriteLine("\n[3] DropNA(how, thresh)");
        
        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "A", new PrimitiveColumn<int>(new int[] { 1, 0, 0, 4 }, new bool[] { false, true, true, false }) }, // 1, NA, NA, 4
            { "B", new PrimitiveColumn<int>(new int[] { 1, 2, 0, 0 }, new bool[] { false, false, true, true }) }, // 1, 2, NA, NA
            { "C", new PrimitiveColumn<int>(new int[] { 1, 0, 0, 0 }, new bool[] { false, true, true, true }) }   // 1, NA, NA, NA
        }, new IntIndex(new[] { 10, 20, 30, 40 })); // Custom Index
        
        Console.WriteLine("Original:\n{0}", df);
        
        // Row 0: No NA
        // Row 1: A=NA, B=2, C=NA (1 valid)
        // Row 2: A=NA, B=NA, C=NA (0 valid)
        // Row 3: A=4, B=NA, C=NA (1 valid)

        // DropNA(how='any') -> Only Row 0 should remain
        var dropAny = df.DropNA(how: "any");
        Console.WriteLine("DropNA(any):\n{0}", dropAny);
        if (dropAny.RowCount == 1 && (int)dropAny.Index.GetValue(0) == 10)
            Console.WriteLine("✅ DropNA(any) + Index Preservation passed");
        else
            Console.WriteLine("❌ DropNA(any) failed");
            
        // DropNA(thresh=1) -> Row 0, 1, 3 remain (Row 2 has 0 valid)
        var dropThresh1 = df.DropNA(thresh: 1);
        Console.WriteLine("DropNA(thresh=1):\n{0}", dropThresh1);
        
        if (dropThresh1.RowCount == 3 && (int)dropThresh1.Index.GetValue(1) == 20)
            Console.WriteLine("✅ DropNA(thresh=1) passed");
        else
            Console.WriteLine("❌ DropNA(thresh=1) failed");
            
        // DropNA(how='all') -> Row 0, 1, 3 remain (Row 2 is all NA)
        var dropAll = df.DropNA(how: "all");
        Console.WriteLine("DropNA(all):\n{0}", dropAll);
        if (dropAll.RowCount == 3)
             Console.WriteLine("✅ DropNA(all) passed");
        else
             Console.WriteLine("❌ DropNA(all) failed");
    }
}
