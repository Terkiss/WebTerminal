using System.Diagnostics;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;
using TeruTeruPandas.Compat;

namespace TeruTeruPandas.Benchmarks;

/// <summary>
/// 성능 벤치마크 클래스
/// PRD에 정의된 성능 목표 달성 여부 검증
/// </summary>
public static class PerformanceBenchmarks
{
    /// <summary>
    /// 1,000,000행 기준 브로드캐스팅 연산 20ms 이내 테스트
    /// </summary>
    public static void BenchmarkBroadcastingOperations()
    {
        Console.WriteLine("=== Broadcasting Operations Benchmark ===");
        
        const int rowCount = 1_000_000;
        var data = Enumerable.Range(0, rowCount).ToArray();
        var series = new Series<int>(data);
        
        var stopwatch = Stopwatch.StartNew();
        
        // 브로드캐스팅 연산 시뮬레이션 (스칼라 덧셈)
        var column = new PrimitiveColumn<int>(data);
        column.VectorizedAdd(42);
        
        stopwatch.Stop();
        
        Console.WriteLine($"Broadcasting operation on {rowCount:N0} elements: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Target: 20ms or less - {(stopwatch.ElapsedMilliseconds <= 20 ? "PASS" : "FAIL")}");
    }
    
    /// <summary>
    /// 1GB CSV 파일 로드 30초 이내 테스트 (시뮬레이션)
    /// </summary>
    public static void BenchmarkCsvReading()
    {
        Console.WriteLine("\n=== CSV Reading Benchmark ===");
        
        // 테스트용 CSV 파일 생성
        var testFilePath = "benchmark_test.csv";
        CreateTestCsvFile(testFilePath, 100_000); // 10만 행으로 축소하여 테스트
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var df = Pd.ReadCsv(testFilePath);
            stopwatch.Stop();
            
            Console.WriteLine($"CSV reading ({df.RowCount:N0} rows): {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Projected time for 1M rows: {stopwatch.ElapsedMilliseconds * 10}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CSV reading failed: {ex.Message}");
        }
        finally
        {
            if (File.Exists(testFilePath))
                File.Delete(testFilePath);
        }
    }
    
    /// <summary>
    /// 100만 행, 10만 고유 키 기준 sum/mean 700ms 이내 테스트
    /// </summary>
    public static void BenchmarkGroupByAggregation()
    {
        Console.WriteLine("\n=== GroupBy Aggregation Benchmark ===");
        
        const int rowCount = 100_000; // 축소된 테스트
        const int uniqueKeys = 10_000;
        
        // 테스트 데이터 생성
        var random = new Random(42);
        var groupKeys = Enumerable.Range(0, rowCount)
            .Select(i => random.Next(0, uniqueKeys))
            .ToArray();
        var values = Enumerable.Range(0, rowCount)
            .Select(i => random.Next(1, 100))
            .ToArray();
        
        var columns = new Dictionary<string, IColumn>
        {
            ["group"] = new PrimitiveColumn<int>(groupKeys),
            ["value"] = new PrimitiveColumn<int>(values)
        };
        
        var df = new DataFrame(columns);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var groupBy = df.GroupBy("group");
            var result = groupBy.Agg(new Dictionary<string, string[]>
            {
                ["value"] = new[] { "sum", "mean" }
            });
            
            stopwatch.Stop();
            
            Console.WriteLine($"GroupBy aggregation ({rowCount:N0} rows, {uniqueKeys:N0} unique keys): {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Projected time for 1M rows: {stopwatch.ElapsedMilliseconds * 10}ms");
            Console.WriteLine($"Target: 700ms or less - {(stopwatch.ElapsedMilliseconds * 10 <= 700 ? "PASS" : "FAIL")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GroupBy aggregation failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 100만 × 100만 키 조인 2.5초 이내 테스트 (축소 버전)
    /// </summary>
    public static void BenchmarkJoinOperations()
    {
        Console.WriteLine("\n=== Join Operations Benchmark ===");
        
        const int leftRows = 10_000;  // 축소된 테스트
        const int rightRows = 5_000;
        
        // 테스트 데이터 생성
        var random = new Random(42);
        var leftKeys = Enumerable.Range(0, leftRows)
            .Select(i => random.Next(0, rightRows))
            .ToArray();
        var leftValues = Enumerable.Range(0, leftRows)
            .Select(i => $"left_value_{i}")
            .ToArray();
            
        var rightKeys = Enumerable.Range(0, rightRows).ToArray();
        var rightValues = Enumerable.Range(0, rightRows)
            .Select(i => $"right_value_{i}")
            .ToArray();
        
        var leftDf = Pd.DataFrame(new Dictionary<string, object[]>
        {
            ["key"] = leftKeys.Cast<object>().ToArray(),
            ["left_value"] = leftValues.Cast<object>().ToArray()
        });
        
        var rightDf = Pd.DataFrame(new Dictionary<string, object[]>
        {
            ["key"] = rightKeys.Cast<object>().ToArray(),
            ["right_value"] = rightValues.Cast<object>().ToArray()
        });
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var joined = leftDf.Merge(rightDf, "key", "inner");
            stopwatch.Stop();
            
            Console.WriteLine($"Join operation ({leftRows:N0} × {rightRows:N0} keys): {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Joined rows: {joined.RowCount:N0}");
            Console.WriteLine($"Projected time for 1M × 1M keys: {stopwatch.ElapsedMilliseconds * 100}ms");
            Console.WriteLine($"Target: 2500ms or less - {(stopwatch.ElapsedMilliseconds * 100 <= 2500 ? "PASS" : "FAIL")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Join operation failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 문자열 contains 검색 100만 행 300ms 이하 테스트
    /// </summary>
    public static void BenchmarkStringOperations()
    {
        Console.WriteLine("\n=== String Operations Benchmark ===");
        
        const int rowCount = 100_000; // 축소된 테스트
        var random = new Random(42);
        var testStrings = new[] { "apple", "banana", "cherry", "date", "elderberry" };
        
        var data = Enumerable.Range(0, rowCount)
            .Select(i => testStrings[random.Next(testStrings.Length)] + i.ToString())
            .ToArray();
        
        var stringColumn = new StringColumn(data);
        
        var stopwatch = Stopwatch.StartNew();
        
        var results = stringColumn.Contains("apple");
        
        stopwatch.Stop();
        
        var matchCount = results.Count(r => r);
        Console.WriteLine($"String contains search ({rowCount:N0} strings): {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Found {matchCount:N0} matches");
        Console.WriteLine($"Projected time for 1M rows: {stopwatch.ElapsedMilliseconds * 10}ms");
        Console.WriteLine($"Target: 300ms or less - {(stopwatch.ElapsedMilliseconds * 10 <= 300 ? "PASS" : "FAIL")}");
    }
    
    /// <summary>
    /// 메모리 사용량 테스트
    /// </summary>
    public static void BenchmarkMemoryUsage()
    {
        Console.WriteLine("\n=== Memory Usage Benchmark ===");
        
        var before = GC.GetTotalMemory(true);
        
        // 큰 DataFrame 생성
        const int rowCount = 100_000;
        var data = Enumerable.Range(0, rowCount).ToArray();
        var df = Pd.DataFrame(new Dictionary<string, object[]>
        {
            ["id"] = data.Cast<object>().ToArray(),
            ["value"] = data.Select(x => x * 2.0).Cast<object>().ToArray(),
            ["name"] = data.Select(x => $"item_{x}").Cast<object>().ToArray()
        });
        
        var after = GC.GetTotalMemory(false);
        var memoryUsed = after - before;
        
        Console.WriteLine($"Memory used for {rowCount:N0} rows DataFrame: {memoryUsed / 1024 / 1024:F2} MB");
        Console.WriteLine($"Memory per row: {memoryUsed / rowCount:F0} bytes");
        
        // 메모리 정리
        df = null;
        GC.Collect();
        var afterGC = GC.GetTotalMemory(true);
        var memoryFreed = after - afterGC;
        
        Console.WriteLine($"Memory freed after GC: {memoryFreed / 1024 / 1024:F2} MB");
    }
    
    /// <summary>
    /// 전체 벤치마크 실행
    /// </summary>
    public static void RunAllBenchmarks()
    {
        Console.WriteLine("Starting TeruTeruPandas Performance Benchmarks");
        Console.WriteLine("=".PadRight(50, '='));
        
        BenchmarkBroadcastingOperations();
        BenchmarkCsvReading();
        BenchmarkGroupByAggregation();
        BenchmarkJoinOperations();        // 새로 추가된 조인 벤치마크
        BenchmarkStringOperations();
        BenchmarkMemoryUsage();
        
        Console.WriteLine("\n" + "=".PadRight(50, '='));
        Console.WriteLine("Benchmarks completed!");
    }
    
    private static void CreateTestCsvFile(string filePath, int rowCount)
    {
        using var writer = new StreamWriter(filePath);
        writer.WriteLine("id,name,value,category");
        
        var random = new Random(42);
        var categories = new[] { "A", "B", "C", "D", "E" };
        
        for (int i = 0; i < rowCount; i++)
        {
            var name = $"item_{i}";
            var value = random.Next(1, 1000);
            var category = categories[random.Next(categories.Length)];
            
            writer.WriteLine($"{i},{name},{value},{category}");
        }
    }
}