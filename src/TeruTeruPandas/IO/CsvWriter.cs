using System.Text;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;

namespace TeruTeruPandas.IO;

/// <summary>
/// CSV 파일 쓰기 기능
/// UTF-8, CRLF/LF 제어, Null 대체 문자 지정.
/// 64KB 대용량 버퍼와 연속 스트림 쓰기(Zero-Allocation Flush) 아키텍처를 도입하여 
/// 디스크 I/O 병목을 제거합니다.
/// </summary>
public static class CsvWriter
{
    public static void ToCsv(DataFrame dataFrame,
        string filePath,
        bool includeHeader = true,
        char separator = ',',
        Encoding? encoding = null,
        string lineEnding = "\r\n",
        string naRep = "NaN")
    {
        encoding ??= Encoding.UTF8;

        // 64KB 대용량 버퍼 적용으로 디스크 I/O 최소화
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        using var writer = new StreamWriter(fileStream, encoding, bufferSize: 65536);

        int rowCount = dataFrame.RowCount;
        int colCount = dataFrame.ColumnCount;
        var columns = dataFrame.Columns.ToArray();

        // 잦은 Dictionary 룩업을 막기 위해 컬럼 참조 배열 미리 캐싱
        var columnRefs = new IColumn[colCount];
        for (int c = 0; c < colCount; c++)
        {
            columnRefs[c] = dataFrame[columns[c]];
        }

        // 헤더 쓰기
        if (includeHeader)
        {
            for (int col = 0; col < colCount; col++)
            {
                writer.Write(columns[col]);
                if (col < colCount - 1) writer.Write(separator);
            }
            writer.Write(lineEnding);
        }

        // 데이터 쓰기 (Zero-Allocation 방식)
        for (int row = 0; row < rowCount; row++)
        {
            for (int col = 0; col < colCount; col++)
            {
                var column = columnRefs[col];

                if (column.IsNA(row))
                {
                    writer.Write(naRep);
                }
                else
                {
                    WriteColumnValueToCsv(writer, column, row, separator);
                }

                if (col < colCount - 1) writer.Write(separator);
            }
            writer.Write(lineEnding);
        }
    }

    private static void WriteColumnValueToCsv(StreamWriter writer, IColumn column, int row, char separator)
    {
        // 최적의 성능을 위한 박싱 없는 빠른 타입 분기처리
        if (column is PrimitiveColumn<int> intCol) writer.Write(intCol.AsSpan()[row]);
        else if (column is PrimitiveColumn<long> longCol) writer.Write(longCol.AsSpan()[row]);
        else if (column is PrimitiveColumn<double> doubleCol) writer.Write(doubleCol.AsSpan()[row]);
        else if (column is PrimitiveColumn<float> floatCol) writer.Write(floatCol.AsSpan()[row]);
        else if (column is PrimitiveColumn<bool> boolCol) writer.Write(boolCol.AsSpan()[row] ? "true" : "false");
        else if (column is PrimitiveColumn<DateTime> dateCol) writer.Write(dateCol.AsSpan()[row].ToString("O"));
        else if (column is StringColumn strCol) WriteFormattedString(writer, strCol.GetValue(row) as string ?? "", separator);
        else
        {
            var value = column.GetValue(row);
            if (value == null) return;
            var stringValue = value.ToString() ?? "";
            WriteFormattedString(writer, stringValue, separator);
        }
    }

    private static void WriteFormattedString(StreamWriter writer, string stringValue, char separator)
    {
        // 구분자, 따옴표, 개행문자가 포함된 경우 따옴표로 감싸기
        if (stringValue.Contains(separator) ||
            stringValue.Contains('"') ||
            stringValue.Contains('\n') ||
            stringValue.Contains('\r'))
        {
            // 따옴표 이스케이프
            writer.Write('"');
            writer.Write(stringValue.Replace("\"", "\"\""));
            writer.Write('"');
        }
        else
        {
            writer.Write(stringValue);
        }
    }
}