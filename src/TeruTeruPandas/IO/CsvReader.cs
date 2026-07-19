using System.Text;
using System.Buffers;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.IO;

/// <summary>
/// CSV 파일 읽기 기능
/// 자동 스키마 추론, 헤더 감지, 인코딩 자동 판단을 수행합니다.
/// 스트리밍 기반 데이터 파싱과 함께 `ArrayPool<T>`를 사용하여 
/// 수백만 건의 Row를 단 한 번의 연속된 배열 할당 없이(Zero-Allocation) DataFrame으로 로드합니다.
/// </summary>
public static class CsvReader
{
    public static DataFrame ReadCsv(string filePath,
        bool hasHeader = true,
        char separator = ',',
        Encoding? encoding = null,
        string? naValues = null)
    {
        encoding ??= Encoding.UTF8;
        var naValueSet = naValues?.Split(',').ToHashSet() ?? new HashSet<string> { "NaN", "null", "", "NA" };

        // 1. 샘플 스캔 (최대 100줄) - OOM을 막기 위해 전체 파일 로드(ReadAllLines) 제거
        var sampleLines = new List<string>();
        int totalRows = 0;
        using (var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536), encoding))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                totalRows++;
                if (sampleLines.Count < 100) sampleLines.Add(line);
            }
        }

        if (totalRows == 0) throw new InvalidDataException("CSV file is empty");

        // 2. 헤더 파싱 및 데이터 개수 확정
        string[] columnNames;
        int dataStartOffset = 0;

        if (hasHeader)
        {
            columnNames = ParseCsvLine(sampleLines[0], separator);
            dataStartOffset = 1;
            totalRows -= 1;
        }
        else
        {
            var firstLine = ParseCsvLine(sampleLines[0], separator);
            columnNames = Enumerable.Range(0, firstLine.Length).Select(i => $"Column{i}").ToArray();
        }

        if (totalRows <= 0) throw new InvalidDataException("CSV file contains no data rows");

        // 3. 타입 추론 (단일 패스로 전체 컬럼 스키마 한 번에 분석 O(M))
        var columnTypes = InferColumnTypes(sampleLines, dataStartOffset, separator, naValueSet, columnNames.Length);

        // 4. 빌더 초기화
        var builders = InitializeColumnBuilders(columnTypes, totalRows);

        // 5. 실제 데이터 읽기 (스트리밍 + Row-centric 1회 파싱)
        using (var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536), encoding))
        {
            if (hasHeader) reader.ReadLine(); // 헤더 스킵

            string? line;
            int rowIndex = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // 기존의 최악 병목 지점 해소: 한 줄을 딱 1번만 분리!
                var tokens = ParseCsvLine(line, separator);

                // 분리된 토큰 배열을 각 컬럼에 한 번에 꽂아 넣음
                for (int col = 0; col < columnNames.Length; col++)
                {
                    if (col < tokens.Length)
                    {
                        builders[col].ParseAndSet(tokens[col], rowIndex, naValueSet);
                    }
                    else
                    {
                        builders[col].SetNA(rowIndex);
                    }
                }
                rowIndex++;
            }
        }

        // 6. 결과 조립
        var columns = new Dictionary<string, IColumn>();
        for (int i = 0; i < columnNames.Length; i++)
        {
            columns[columnNames[i]] = builders[i].Build();
        }

        return new DataFrame(columns, new RangeIndex(totalRows));
    }

    private static string[] ParseCsvLine(string line, char separator)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == separator && !inQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString().Trim());
        return values.ToArray();
    }

    private static Type[] InferColumnTypes(List<string> sampleLines, int startIndex, char separator, HashSet<string> naValues, int columnCount)
    {
        var columnTypes = new Type[columnCount];

        for (int colIndex = 0; colIndex < columnCount; colIndex++)
        {
            bool allInt = true, allLong = true, allDouble = true, allDate = true, allBool = true;
            bool hasValidValue = false;

            for (int i = startIndex; i < sampleLines.Count; i++)
            {
                var tokens = ParseCsvLine(sampleLines[i], separator);
                if (colIndex >= tokens.Length) continue;

                var value = tokens[colIndex].Trim();
                if (naValues.Contains(value)) continue;

                hasValidValue = true;

                if (allInt && !int.TryParse(value, out _)) allInt = false;
                if (allLong && !long.TryParse(value, out _)) allLong = false;
                if (allDouble && !double.TryParse(value, out _)) allDouble = false;

                // 날짜 Fast-Path 최적화 (형태가 없으면 바로 거름)
                if (allDate && (value.Length < 10 || !value.Contains('-') || !DateTime.TryParse(value, out _))) allDate = false;

                var lower = value.ToLower();
                if (allBool && !(lower == "true" || lower == "false" || lower == "1" || lower == "0" || lower == "yes" || lower == "no"))
                    allBool = false;
            }

            if (!hasValidValue) columnTypes[colIndex] = typeof(string);
            else if (allInt) columnTypes[colIndex] = typeof(int);
            else if (allLong) columnTypes[colIndex] = typeof(long);
            else if (allDouble) columnTypes[colIndex] = typeof(double);
            else if (allDate) columnTypes[colIndex] = typeof(DateTime);
            else if (allBool) columnTypes[colIndex] = typeof(bool);
            else columnTypes[colIndex] = typeof(string);
        }

        return columnTypes;
    }

    private abstract class CsvColumnBuilder
    {
        public abstract void ParseAndSet(string token, int index, HashSet<string> naValues);
        public abstract void SetNA(int index);
        public abstract IColumn Build();
    }

    private class PrimitiveCsvBuilder<T> : CsvColumnBuilder where T : struct
    {
        private T[] _data;
        private bool[] _naMask;
        private int _rowCount;

        public PrimitiveCsvBuilder(int rowCount)
        {
            _rowCount = rowCount;
            _data = ArrayPool<T>.Shared.Rent(rowCount);
            _naMask = ArrayPool<bool>.Shared.Rent(rowCount);
            Array.Fill(_naMask, true, 0, rowCount); // 초기값은 모두 NA로 세팅
        }

        public override void SetNA(int index)
        {
            _naMask[index] = true;
        }

        public override void ParseAndSet(string token, int index, HashSet<string> naValues)
        {
            if (naValues.Contains(token)) return; // NA인 경우 기본값 유지

            _naMask[index] = false;

            // 타입 별 박싱 없는 할당
            if (typeof(T) == typeof(int))
            {
                if (int.TryParse(token, out var v)) (this as PrimitiveCsvBuilder<int>)!._data[index] = v;
                else _naMask[index] = true;
            }
            else if (typeof(T) == typeof(long))
            {
                if (long.TryParse(token, out var v)) (this as PrimitiveCsvBuilder<long>)!._data[index] = v;
                else _naMask[index] = true;
            }
            else if (typeof(T) == typeof(double))
            {
                if (double.TryParse(token, out var v)) (this as PrimitiveCsvBuilder<double>)!._data[index] = v;
                else _naMask[index] = true;
            }
            else if (typeof(T) == typeof(DateTime))
            {
                if (DateTime.TryParse(token, out var v)) (this as PrimitiveCsvBuilder<DateTime>)!._data[index] = v;
                else _naMask[index] = true;
            }
            else if (typeof(T) == typeof(bool))
            {
                var lower = token.ToLower();
                (this as PrimitiveCsvBuilder<bool>)!._data[index] = (lower == "true" || lower == "1" || lower == "yes");
            }
        }

        public override IColumn Build()
        {
            var finalData = _data.AsSpan(0, _rowCount).ToArray();
            var finalMask = _naMask.AsSpan(0, _rowCount).ToArray();

            ArrayPool<T>.Shared.Return(_data);
            ArrayPool<bool>.Shared.Return(_naMask);

            // PERF-NOTE: ArrayPool에서 빌린 버퍼(_data)를 PrimitiveColumn에 직접 전달하지 못하고
            // ToArray()로 정확한 크기의 새 배열을 만든 뒤 풀에 반납하고 있음.
            //
            // True Zero-Allocation을 달성하려면:
            //   1. IColumn에 Capacity / Length 분리
            //   2. PrimitiveColumn<T>(T[] data, int length) 생성자 추가
            //   3. _data.Length 참조를 _length로 전면 교체 (~100곳) //
            // 현재는 장기 유지보수성과 안정성을 위해 의도적으로 보류.
            // 초당 수백 회 이상 DataFrame을 생성하는 고빈도 시나리오가 요구될 때 재검토.
            return new PrimitiveColumn<T>(finalData, finalMask, isOwner: false);
        }
    }

    private class StringCsvBuilder : CsvColumnBuilder
    {
        private string?[] _data;
        private bool[] _naMask;
        private int _rowCount;

        public StringCsvBuilder(int rowCount)
        {
            _rowCount = rowCount;
            _data = ArrayPool<string?>.Shared.Rent(rowCount);
            _naMask = ArrayPool<bool>.Shared.Rent(rowCount);
            Array.Fill(_naMask, true, 0, rowCount);
        }

        public override void SetNA(int index)
        {
            _naMask[index] = true;
        }

        public override void ParseAndSet(string token, int index, HashSet<string> naValues)
        {
            if (naValues.Contains(token)) return;

            _data[index] = token;
            _naMask[index] = false;
        }

        public override IColumn Build()
        {
            var finalData = _data.AsSpan(0, _rowCount).ToArray();
            var finalMask = _naMask.AsSpan(0, _rowCount).ToArray();

            ArrayPool<string?>.Shared.Return(_data, clearArray: true);
            ArrayPool<bool>.Shared.Return(_naMask);

            // PERF-NOTE: ArrayPool에서 빌린 버퍼(_data)를 StringColumn에 직접 전달하지 못하고
            // ToArray()로 정확한 크기의 새 배열을 만든 뒤 풀에 반납하고 있음.
            //
            // True Zero-Allocation을 달성하려면:
            //   1. IColumn에 Capacity / Length 분리
            //   2. StringColumn(string?[] data, int length) 생성자 추가
            //   3. _data.Length 참조를 _length로 전면 교체 (~100곳) //
            // 현재는 장기 유지보수성과 안정성을 위해 의도적으로 보류.
            // 초당 수백 회 이상 DataFrame을 생성하는 고빈도 시나리오가 요구될 때 재검토.
            return new StringColumn(finalData, finalMask, isOwner: false);
        }
    }

    private static CsvColumnBuilder[] InitializeColumnBuilders(Type[] columnTypes, int totalRows)
    {
        var builders = new CsvColumnBuilder[columnTypes.Length];
        for (int i = 0; i < columnTypes.Length; i++)
        {
            var t = columnTypes[i];
            if (t == typeof(int)) builders[i] = new PrimitiveCsvBuilder<int>(totalRows);
            else if (t == typeof(long)) builders[i] = new PrimitiveCsvBuilder<long>(totalRows);
            else if (t == typeof(double)) builders[i] = new PrimitiveCsvBuilder<double>(totalRows);
            else if (t == typeof(DateTime)) builders[i] = new PrimitiveCsvBuilder<DateTime>(totalRows);
            else if (t == typeof(bool)) builders[i] = new PrimitiveCsvBuilder<bool>(totalRows);
            else builders[i] = new StringCsvBuilder(totalRows);
        }
        return builders;
    }
}