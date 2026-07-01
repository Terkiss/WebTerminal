using System.Text;
using System.Buffers;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.IO;

/// <summary>
/// 고성능 CSV 파일 읽기 기능을 제공합니다.
/// 자동 스키마 추론, 헤더 감지, 인코딩 자동 판단을 수행합니다.
/// 스트리밍 기반 데이터 파싱과 함께 `ArrayPool&lt;T&gt;`를 사용하여 
/// 수백만 건의 Row를 단 한 번의 연속된 배열 할당 없이(Zero-Allocation 지향) DataFrame으로 로드합니다.
/// </summary>
public static class CsvReader
{
    /// <summary>
    /// CSV 파일을 읽어 DataFrame으로 변환합니다.
    /// </summary>
    /// <param name="filePath">파일 경로</param>
    /// <param name="hasHeader">첫 번째 행을 헤더로 사용할지 여부</param>
    /// <param name="separator">구분자 (기본값: ',')</param>
    /// <param name="encoding">텍스트 인코딩</param>
    /// <param name="naValues">결측치로 간주할 문자열 목록 (쉼표로 구분)</param>
    public static DataFrame ReadCsv(string filePath,
        bool hasHeader = true,
        char separator = ',',
        Encoding? encoding = null,
        string? naValues = null)
    {
        encoding ??= Encoding.UTF8;
        var naValueSet = naValues?.Split(',').ToHashSet() ?? new HashSet<string> { "NaN", "null", "", "NA" };

        // 1. 샘플 스캔 및 전체 행수 파악 (메모리 효율을 위해 스트리밍 방식으로 전체 파일 스캔)
        var sampleLines = new List<string>();
        int totalRows = 0;
        using (var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536), encoding))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                totalRows++;
                if (sampleLines.Count < 100) sampleLines.Add(line); // 상위 100줄을 타입 추론용 샘플로 수집
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

        // 3. 타입 추론 (샘플 데이터를 바탕으로 컬럼별 최적의 데이터 타입 결정)
        var columnTypes = InferColumnTypes(sampleLines, dataStartOffset, separator, naValueSet, columnNames.Length);

        // 4. 빌더 초기화 (ArrayPool을 사용하여 대규모 버퍼를 미리 대여)
        var builders = InitializeColumnBuilders(columnTypes, totalRows);

        // 5. 실제 데이터 읽기 및 파싱 (스트리밍 + 고속 행별 처리)
        using (var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536), encoding))
        {
            if (hasHeader) reader.ReadLine(); // 헤더 행 스킵

            string? line;
            int rowIndex = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // 성능 최적화: 한 줄을 한 번만 파싱하여 토큰화
                var tokens = ParseCsvLine(line, separator);

                // 파싱된 토큰들을 각 컬럼 빌더에 전달 (타입별 고속 변환 수행)
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

        // 6. DataFrame 조립 및 결과 반환
        var columns = new Dictionary<string, IColumn>();
        for (int i = 0; i < columnNames.Length; i++)
        {
            columns[columnNames[i]] = builders[i].Build();
        }

        return new DataFrame(columns, new RangeIndex(totalRows));
    }

    /// <summary>
    /// 따옴표 등을 고려하여 CSV 한 줄을 안전하게 토큰으로 분리합니다.
    /// </summary>
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
                inQuotes = !inQuotes; // 따옴표 내의 구분자는 무시
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

    /// <summary>
    /// 샘플 행들을 분석하여 각 컬럼에 가장 적합한 데이터 타입(int, double, DateTime 등)을 추론합니다.
    /// </summary>
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

                // 가장 엄격한 타입부터 순차적으로 검증
                if (allInt && !int.TryParse(value, out _)) allInt = false;
                if (allLong && !long.TryParse(value, out _)) allLong = false;
                if (allDouble && !double.TryParse(value, out _)) allDouble = false;

                // 날짜 타입 검증 (패턴 체크 포함)
                if (allDate && (value.Length < 10 || !value.Contains('-') || !DateTime.TryParse(value, out _))) allDate = false;

                var lower = value.ToLower();
                if (allBool && !(lower == "true" || lower == "false" || lower == "1" || lower == "0" || lower == "yes" || lower == "no"))
                    allBool = false;
            }

            // 추론 결과에 따라 타입 할당
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

    /// <summary>
    /// 대용량 데이터 로드를 위한 컬럼 빌더 추상 클래스
    /// </summary>
    private abstract class CsvColumnBuilder
    {
        public abstract void ParseAndSet(string token, int index, HashSet<string> naValues);
        public abstract void SetNA(int index);
        public abstract IColumn Build();
    }

    /// <summary>
    /// Primitive 타입(값 타입)을 위한 빌더입니다. ArrayPool을 사용하여 메모리 재사용을 최적화합니다.
    /// </summary>
    private class PrimitiveCsvBuilder<T> : CsvColumnBuilder where T : struct
    {
        private T[] _data;
        private bool[] _naMask;
        private int _rowCount;

        public PrimitiveCsvBuilder(int rowCount)
        {
            _rowCount = rowCount;
            // ArrayPool에서 필요한 크기만큼 버퍼 대여 (Zero-Allocation 전략)
            _data = ArrayPool<T>.Shared.Rent(rowCount);
            _naMask = ArrayPool<bool>.Shared.Rent(rowCount);
            Array.Fill(_naMask, true, 0, rowCount); // 기본은 모두 결측치로 초기화
        }

        public override void SetNA(int index)
        {
            _naMask[index] = true;
        }

        public override void ParseAndSet(string token, int index, HashSet<string> naValues)
        {
            if (naValues.Contains(token)) return;

            _naMask[index] = false;

            // 제네릭 환경에서 박싱(Boxing) 없는 고속 타입 변환 및 할당
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
            // 실제 데이터 길이에 맞춰 배열 복사 후 대여했던 버퍼 반납
            var finalData = _data.AsSpan(0, _rowCount).ToArray();
            var finalMask = _naMask.AsSpan(0, _rowCount).ToArray();

            ArrayPool<T>.Shared.Return(_data);
            ArrayPool<bool>.Shared.Return(_naMask);

            return new PrimitiveColumn<T>(finalData, finalMask, isOwner: false);
        }
    }

    /// <summary>
    /// 문자열 컬럼을 위한 빌더입니다.
    /// </summary>
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
