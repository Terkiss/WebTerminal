using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.Test;

public static class Phase1CoreTests
{
    public static void Run()
    {
        Console.WriteLine("=== Phase 1 Core Tests: DateTime & Sorting ===");
        VerifyDateTime();
        VerifySorting();
        Console.WriteLine("=== Phase 1 Core Tests Complete ===");
    }

    private static void VerifyDateTime()
    {
        Console.WriteLine("\n[1] Testing DateTime Support");

        // 1. Create DataFrame with DateTime
        var dates = new DateTime[]
        {
            new DateTime(2023, 1, 1),
            new DateTime(2023, 5, 15),
            new DateTime(2024, 12, 31)
        };
        var values = new int[] { 10, 20, 30 };

        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "date", new PrimitiveColumn<DateTime>(dates) },
            { "value", new PrimitiveColumn<int>(values) }
        });

        Console.WriteLine($"DataFrame Created:\n{df}");

        // 2. Test .dt Accessor
        try 
        {
            var dateSeries = new Series<DateTime>((PrimitiveColumn<DateTime>)df["date"]);
            
            Console.WriteLine($"Year: {dateSeries.Dt.Year}");
            Console.WriteLine($"Month: {dateSeries.Dt.Month}");
            Console.WriteLine($"DayOfWeek: {dateSeries.Dt.DayOfWeek}");
        }
        catch (Exception ex)
        {
             Console.WriteLine($"❌ DateTime Test Exception: {ex.Message}");
             Console.WriteLine(ex.StackTrace);
        }
    }

    private static void VerifySorting()
    {
        Console.WriteLine("\n[2] Testing Sorting Support");

        var df = new DataFrame(new Dictionary<string, IColumn>
        {
            { "A", new PrimitiveColumn<int>(new[] { 3, 1, 2, 5, 4 }) },
            { "B", new StringColumn(new[] { "c", "a", "b", "e", "d" }) }
        }, new RangeIndex(5));

        Console.WriteLine($"Original DataFrame:\n{df}");

        // 1. SortValues
        try
        {
            var sortedByA = df.SortValues("A");
            Console.WriteLine("Sorted by 'A' (Ascending):\n{0}", sortedByA);
            
            var sortedByBDesc = df.SortValues("B", ascending: false);
            Console.WriteLine("Sorted by 'B' (Descending):\n{0}", sortedByBDesc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SortValues Test Exception: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        // 2. SortIndex
        try
        {
             var shuffledDf = new DataFrame(new Dictionary<string, IColumn>
             {
                 { "val", new PrimitiveColumn<int>(new[] { 10, 20, 30 }) }
             }, new IntIndex(new[] { 3, 1, 2 }));
             
             Console.WriteLine($"\nDataFrame with Shuffled Index:\n{shuffledDf}");

             var sortedByIndex = shuffledDf.SortIndex();
             Console.WriteLine("Sorted by Index:\n{0}", sortedByIndex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SortIndex Test Exception: {ex.Message}");
             Console.WriteLine(ex.StackTrace);
        }
    }
}
