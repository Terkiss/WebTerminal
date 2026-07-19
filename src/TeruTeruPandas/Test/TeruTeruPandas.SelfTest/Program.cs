using System;
using System.Collections.Generic;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.SelfTest;

internal static class Program
{
    private static void Main()
    {
        RunSelfTest();
        Console.WriteLine("SELFTEST OK");
    }

    private static void RunSelfTest()
    {
        VerifyAddColumnNew();
        VerifyAddColumnReplace();
        VerifyDropColumn();
        VerifyBooleanIndexingMaskLengthMismatchThrows();
        VerifyBooleanIndexingKeepsNaAndIndex();
        VerifyDisposableContracts();
    }

    private static void VerifyAddColumnNew()
    {
        using var df = CreateSampleFrame();

        var label = new StringColumn(new string?[] { "x", "y", "z" });
        df.AddColumn("label", label);

        Ensure(df.ColumnCount == 3, "AddColumn(신규): ColumnCount가 증가해야 함");
        Ensure(df.Columns.Length == 3, "AddColumn(신규): Columns에 새 컬럼이 포함되어야 함");
        Ensure(df.Columns[0] == "id" && df.Columns[1] == "value" && df.Columns[2] == "label",
            "AddColumn(신규): 컬럼 순서가 유지되고 끝에 추가되어야 함");
        Ensure(ReferenceEquals(df["label"], label), "AddColumn(신규): 추가된 컬럼 인스턴스가 사용되어야 함");
    }

    private static void VerifyAddColumnReplace()
    {
        using var df = CreateSampleFrame();

        var replacement = new PrimitiveColumn<int>(new[] { 9, 9, 9 });
        df.AddColumn("id", replacement);

        Ensure(df.ColumnCount == 2, "AddColumn(기존): ColumnCount가 변경되지 않아야 함");
        Ensure(df.Columns.Length == 2, "AddColumn(기존): Columns가 중복되지 않아야 함");
        Ensure(df.Columns[0] == "id" && df.Columns[1] == "value", "AddColumn(기존): 컬럼 순서가 안정적으로 유지되어야 함");
        Ensure((int)df["id"].GetValue(0)! == 9, "AddColumn(기존): 교체된 컬럼이 새 값을 노출해야 함");
    }

    private static void VerifyDropColumn()
    {
        using var df = CreateSampleFrame();

        df.DropColumn("value");

        Ensure(df.ColumnCount == 1, "DropColumn: ColumnCount가 감소해야 함");
        Ensure(df.Columns.Length == 1 && df.Columns[0] == "id", "DropColumn: Columns에서 이름이 제거되어야 함");
        Ensure((int)df["id"].GetValue(0)! == 1, "DropColumn: 남은 컬럼은 계속 접근 가능해야 함");

        bool threw = false;
        try
        {
            _ = df["value"];
        }
        catch (KeyNotFoundException)
        {
            threw = true;
        }
        Ensure(threw, "DropColumn: 제거된 컬럼에 접근하면 KeyNotFoundException이 발생해야 함");
    }

    private static void VerifyBooleanIndexingMaskLengthMismatchThrows()
    {
        using var df = CreateSampleFrame();

        bool threw = false;
        try
        {
            _ = df[new BoolSeries(new[] { true, false })];
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Ensure(threw, "불린 인덱싱: mask.Length 불일치 시 ArgumentException이 발생해야 함");
    }

    private static void VerifyBooleanIndexingKeepsNaAndIndex()
    {
        var ints = new PrimitiveColumn<int>(new[] { 1, 2, 3 });
        ints.SetNA(1);
        var doubles = new PrimitiveColumn<double>(new[] { 1.0, 2.0, 3.0 });
        doubles.SetNA(2);
        var text = new StringColumn(new string?[] { "a", "b", "c" });
        text.SetNA(2);

        var columns = new Dictionary<string, IColumn>
        {
            ["ints"] = ints,
            ["doubles"] = doubles,
            ["text"] = text
        };

        // Index.Reorder의 서브셋 보존을 검증하기 위한 비-범위(Non-range) 인덱스
        var index = new IntIndex(new[] { 10, 20, 30 });
        using var df = new DataFrame(columns, index);

        // 결과에서 NA 위치가 유지되는지 확인하기 위해 1행과 2행을 유지합니다.
        var mask = new BoolSeries(new[] { false, true, true });
        var filtered = df[mask];

        Ensure(filtered.RowCount == 2, "불린 인덱싱: 필터링 후 2행이 기대됨");
        Ensure(filtered.Index is not RangeIndex, "불린 인덱싱: 인덱스가 RangeIndex로 재설정되지 않아야 함");
        Ensure(filtered.Index.Contains(20) && filtered.Index.Contains(30) && !filtered.Index.Contains(10),
            "불린 인덱싱: 인덱스는 원래의 서브셋인 {20,30}이어야 함");

        // NA 보존 확인 (IColumn.Reorder를 통해 전달되어야 함)
        Ensure(filtered["ints"].IsNA(0), "불린 인덱싱: ints의 NA가 보존되어야 함 (1행 -> 0행)");
        Ensure(filtered["doubles"].IsNA(1), "불린 인덱싱: doubles의 NA가 보존되어야 함 (2행 -> 1행)");
        Ensure(filtered["text"].IsNA(1), "불린 인덱싱: text의 NA가 보존되어야 함 (2행 -> 1행)");
    }

    private static void VerifyDisposableContracts()
    {
        var col = new PrimitiveColumn<int>(3);
        col.Dispose();
        col.Dispose();

        var df = CreateSampleFrame();
        df.Dispose();
        df.Dispose();
    }

    private static DataFrame CreateSampleFrame()
    {
        var columns = new Dictionary<string, IColumn>
        {
            ["id"] = new PrimitiveColumn<int>(new[] { 1, 2, 3 }),
            ["value"] = new PrimitiveColumn<double>(new[] { 10.0, 20.0, 30.0 })
        };

        return new DataFrame(columns);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}