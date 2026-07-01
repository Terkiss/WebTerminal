using System.Text.Json;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;
using System.Text;
using System.Buffers;

namespace TeruTeruPandas.IO;

/// <summary>
/// JSON 및 JSON Lines 입출력을 지원하는 고성능 스트리밍 I/O 모듈
/// 대용량 파일 파싱 과정에서 내부 ColumnBuilder들이 `ArrayPool<T>`를 사용하여
/// 버퍼를 재사용함으로써 Zero-Allocation 파싱을 구현합니다. GC 병목이 거의 발생하지 않습니다.
/// (단일 패스, 진성 비동기, O(1) 할당 최적화 Pass 4 및 3.1 버전에 따른 풀링 로직 적용)
/// </summary>
public static class JsonIO
{
    private const int DefaultSchemaSampleSize = 100;
    private static readonly byte[] NewLineBytes = new[] { (byte)'\n' };
    private static readonly JsonWriterOptions DefaultWriterOptions = new JsonWriterOptions { Indented = false };
    private static readonly JsonWriterOptions PrettyWriterOptions = new JsonWriterOptions { Indented = true };

    public static DataFrame ReadJson(string filePath, bool isJsonLines = false)
    {
        return isJsonLines ? ReadJsonLinesStreaming(filePath) : ReadRegularJsonStreaming(filePath);
    }

    public static async Task<DataFrame> ReadJsonAsync(string filePath, bool isJsonLines = false, CancellationToken cancellationToken = default)
    {
        return isJsonLines
            ? await ReadJsonLinesStreamingAsync(filePath, cancellationToken)
            : await ReadRegularJsonStreamingAsync(filePath, cancellationToken);
    }

    public static void ToJson(DataFrame dataFrame, string filePath, bool pretty = false, bool asJsonLines = false)
    {
        var options = pretty ? PrettyWriterOptions : DefaultWriterOptions;
        if (asJsonLines) WriteJsonLinesFast(dataFrame, filePath, options);
        else WriteRegularJsonFast(dataFrame, filePath, options);
    }

    public static async Task ToJsonAsync(DataFrame dataFrame, string filePath, bool pretty = false, bool asJsonLines = false, CancellationToken cancellationToken = default)
    {
        var options = pretty ? PrettyWriterOptions : DefaultWriterOptions;
        if (asJsonLines) await WriteJsonLinesFastAsync(dataFrame, filePath, options, cancellationToken);
        else await WriteRegularJsonFastAsync(dataFrame, filePath, options, cancellationToken);
    }

    // ==========================================
    // 1. FAST ZERO-ALLOCATION WRITE
    // ==========================================

    private static void WriteJsonLinesFast(DataFrame dataFrame, string filePath, JsonWriterOptions options)
    {
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        using var writer = new Utf8JsonWriter(fileStream, options); // options 파라미터 적용

        int rowCount = dataFrame.RowCount;
        int colCount = dataFrame.ColumnCount;
        var columns = dataFrame.Columns.ToArray();
        var columnRefs = new IColumn[colCount];
        var encodedColNames = new JsonEncodedText[colCount];
        for (int c = 0; c < colCount; c++)
        {
            columnRefs[c] = dataFrame[columns[c]];
            encodedColNames[c] = JsonEncodedText.Encode(columns[c]);
        }

        for (int row = 0; row < rowCount; row++)
        {
            writer.WriteStartObject();
            for (int col = 0; col < colCount; col++)
            {
                var column = columnRefs[col];
                writer.WritePropertyName(encodedColNames[col]);

                if (column.IsNA(row)) writer.WriteNullValue();
                else WriteColumnValueFast(writer, column, row);
            }
            writer.WriteEndObject();
            writer.Flush();
            fileStream.WriteByte((byte)'\n');
        }
    }

    private static async Task WriteJsonLinesFastAsync(DataFrame dataFrame, string filePath, JsonWriterOptions options, CancellationToken cancellationToken)
    {
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true);
        var bufferWriter = new ArrayBufferWriter<byte>(65536);
        using var writer = new Utf8JsonWriter(bufferWriter, options);

        int rowCount = dataFrame.RowCount;
        int colCount = dataFrame.ColumnCount;
        var columns = dataFrame.Columns.ToArray();
        var columnRefs = new IColumn[colCount];
        var encodedColNames = new JsonEncodedText[colCount];
        for (int c = 0; c < colCount; c++)
        {
            columnRefs[c] = dataFrame[columns[c]];
            encodedColNames[c] = JsonEncodedText.Encode(columns[c]);
        }

        for (int row = 0; row < rowCount; row++)
        {
            writer.WriteStartObject();
            for (int col = 0; col < colCount; col++)
            {
                var column = columnRefs[col];
                writer.WritePropertyName(encodedColNames[col]);

                if (column.IsNA(row)) writer.WriteNullValue();
                else WriteColumnValueFast(writer, column, row);
            }
            writer.WriteEndObject();
            writer.Flush();

            var span = bufferWriter.GetSpan(1);
            span[0] = (byte)'\n';
            bufferWriter.Advance(1);

            if (bufferWriter.WrittenCount > 32768)
            {
                await fileStream.WriteAsync(bufferWriter.WrittenMemory, cancellationToken);
                bufferWriter.Clear();
            }
        }

        if (bufferWriter.WrittenCount > 0)
        {
            await fileStream.WriteAsync(bufferWriter.WrittenMemory, cancellationToken);
        }
    }

    private static void WriteRegularJsonFast(DataFrame dataFrame, string filePath, JsonWriterOptions options)
    {
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        using var writer = new Utf8JsonWriter(fileStream, options);

        writer.WriteStartArray();

        int rowCount = dataFrame.RowCount;
        int colCount = dataFrame.ColumnCount;
        var columns = dataFrame.Columns.ToArray();
        var columnRefs = new IColumn[colCount];
        var encodedColNames = new JsonEncodedText[colCount];
        for (int c = 0; c < colCount; c++)
        {
            columnRefs[c] = dataFrame[columns[c]];
            encodedColNames[c] = JsonEncodedText.Encode(columns[c]);
        }

        for (int row = 0; row < rowCount; row++)
        {
            writer.WriteStartObject();

            for (int col = 0; col < colCount; col++)
            {
                var column = columnRefs[col];
                writer.WritePropertyName(encodedColNames[col]);

                if (column.IsNA(row)) writer.WriteNullValue();
                else WriteColumnValueFast(writer, column, row);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.Flush();
    }

    private static async Task WriteRegularJsonFastAsync(DataFrame dataFrame, string filePath, JsonWriterOptions options, CancellationToken cancellationToken)
    {
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true);
        using var writer = new Utf8JsonWriter(fileStream, options);

        writer.WriteStartArray();

        int rowCount = dataFrame.RowCount;
        int colCount = dataFrame.ColumnCount;
        var columns = dataFrame.Columns.ToArray();
        var columnRefs = new IColumn[colCount];
        var encodedColNames = new JsonEncodedText[colCount];
        for (int c = 0; c < colCount; c++)
        {
            columnRefs[c] = dataFrame[columns[c]];
            encodedColNames[c] = JsonEncodedText.Encode(columns[c]);
        }

        for (int row = 0; row < rowCount; row++)
        {
            writer.WriteStartObject();

            for (int col = 0; col < colCount; col++)
            {
                var column = columnRefs[col];
                writer.WritePropertyName(encodedColNames[col]);

                if (column.IsNA(row)) writer.WriteNullValue();
                else WriteColumnValueFast(writer, column, row);
            }

            writer.WriteEndObject();

            if (writer.BytesPending > 65536)
            {
                await writer.FlushAsync(cancellationToken);
            }
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
    }

    private static void WriteColumnValueFast(Utf8JsonWriter writer, IColumn column, int row)
    {
        if (column is PrimitiveColumn<int> intCol) writer.WriteNumberValue(intCol.AsSpan()[row]);
        else if (column is PrimitiveColumn<long> longCol) writer.WriteNumberValue(longCol.AsSpan()[row]);
        else if (column is PrimitiveColumn<double> doubleCol) writer.WriteNumberValue(doubleCol.AsSpan()[row]);
        else if (column is PrimitiveColumn<bool> boolCol) writer.WriteBooleanValue(boolCol.AsSpan()[row]);
        else if (column is PrimitiveColumn<DateTime> dateCol) writer.WriteStringValue(dateCol.AsSpan()[row]);
        else if (column is StringColumn strCol) writer.WriteStringValue(strCol.GetValue(row) as string ?? "");
        else
        {
            var val = column.GetValue(row);
            if (val == null) writer.WriteNullValue();
            else writer.WriteStringValue(val.ToString());
        }
    }

    // ==========================================
    // 2. STREAMING READ (JSON LINES & REGULAR)
    // ==========================================

    private static DataFrame ReadJsonLinesStreaming(string filePath)
    {
        var sampleDocs = new List<JsonElement>();
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
        using var reader = new StreamReader(fileStream, Encoding.UTF8);
        string? line;

        while (sampleDocs.Count < DefaultSchemaSampleSize && (line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                sampleDocs.Add(doc.RootElement.Clone());
            }
            catch (JsonException) { }
        }

        if (sampleDocs.Count == 0) throw new InvalidDataException("JSON Lines file is empty or contains no valid json");

        var schema = InferSchemaFromSamples(sampleDocs.Select(doc => doc));
        var builders = InitializeColumnBuilders(schema);

        int rowIndex = 0;
        foreach (var element in sampleDocs)
        {
            ParseJsonRowToBuilders(element, builders, rowIndex++);
        }
        sampleDocs.Clear();

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                ParseJsonRowToBuilders(doc.RootElement, builders, rowIndex++);
            }
            catch (JsonException) { }
        }

        return FinalizeDataFrame(builders, rowIndex);
    }

    private static async Task<DataFrame> ReadJsonLinesStreamingAsync(string filePath, CancellationToken cancellationToken)
    {
        var sampleDocs = new List<JsonElement>();
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        using var reader = new StreamReader(fileStream, Encoding.UTF8);
        string? line;

        while (sampleDocs.Count < DefaultSchemaSampleSize && (line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                sampleDocs.Add(doc.RootElement.Clone());
            }
            catch (JsonException) { }
        }

        if (sampleDocs.Count == 0) throw new InvalidDataException("JSON Lines file is empty or contains no valid json");

        var schema = InferSchemaFromSamples(sampleDocs);
        var builders = InitializeColumnBuilders(schema);

        int rowIndex = 0;
        foreach (var element in sampleDocs)
        {
            ParseJsonRowToBuilders(element, builders, rowIndex++);
        }
        sampleDocs.Clear();

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                ParseJsonRowToBuilders(doc.RootElement, builders, rowIndex++);
            }
            catch (JsonException) { }
        }

        return FinalizeDataFrame(builders, rowIndex);
    }

    private static DataFrame ReadRegularJsonStreaming(string filePath)
    {
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536);
        using var jsonDocument = JsonDocument.Parse(fileStream);

        var root = jsonDocument.RootElement;
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid JSON format: Root must be an array.");

        using var enumerator = root.EnumerateArray().GetEnumerator();
        var sampleDocs = new List<JsonElement>();

        while (sampleDocs.Count < DefaultSchemaSampleSize && enumerator.MoveNext())
        {
            if (enumerator.Current.ValueKind == JsonValueKind.Object)
            {
                sampleDocs.Add(enumerator.Current);
            }
        }

        if (sampleDocs.Count == 0) throw new InvalidDataException("JSON array is empty or contains no objects");

        var schema = InferSchemaFromSamples(sampleDocs);
        var builders = InitializeColumnBuilders(schema);
        int rowIndex = 0;

        foreach (var element in sampleDocs)
        {
            ParseJsonRowToBuilders(element, builders, rowIndex++);
        }

        while (enumerator.MoveNext())
        {
            if (enumerator.Current.ValueKind == JsonValueKind.Object)
            {
                ParseJsonRowToBuilders(enumerator.Current, builders, rowIndex++);
            }
        }

        return FinalizeDataFrame(builders, rowIndex);
    }

    private static async Task<DataFrame> ReadRegularJsonStreamingAsync(string filePath, CancellationToken cancellationToken)
    {
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true);

        var asyncEnumerable = JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(fileStream, cancellationToken: cancellationToken);
        await using var enumerator = asyncEnumerable.GetAsyncEnumerator(cancellationToken);

        var sampleDocs = new List<JsonElement>();
        while (sampleDocs.Count < DefaultSchemaSampleSize && await enumerator.MoveNextAsync())
        {
            if (enumerator.Current.ValueKind == JsonValueKind.Object)
            {
                sampleDocs.Add(enumerator.Current.Clone());
            }
        }

        if (sampleDocs.Count == 0) throw new InvalidDataException("JSON array is empty or contains no objects");

        var schema = InferSchemaFromSamples(sampleDocs);
        var builders = InitializeColumnBuilders(schema);
        int rowIndex = 0;

        foreach (var element in sampleDocs)
        {
            ParseJsonRowToBuilders(element, builders, rowIndex++);
        }

        while (await enumerator.MoveNextAsync())
        {
            if (enumerator.Current.ValueKind == JsonValueKind.Object)
            {
                ParseJsonRowToBuilders(enumerator.Current, builders, rowIndex++);
            }
        }

        return FinalizeDataFrame(builders, rowIndex);
    }

    // ==========================================
    // 3. O(1) EXACT-ARRAY BUILDERS & INFERENCE
    // ==========================================

    private static Dictionary<string, Type> InferSchemaFromSamples(IEnumerable<JsonElement> samples)
    {
        var schema = new Dictionary<string, Type>();
        var sampleValues = new Dictionary<string, List<JsonElement>>();

        foreach (var element in samples)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (!sampleValues.TryGetValue(prop.Name, out var list))
                {
                    list = new List<JsonElement>();
                    sampleValues[prop.Name] = list;
                }
                if (prop.Value.ValueKind != JsonValueKind.Null && prop.Value.ValueKind != JsonValueKind.Undefined)
                {
                    list.Add(prop.Value);
                }
            }
        }

        foreach (var kvp in sampleValues)
        {
            string colName = kvp.Key;
            var values = kvp.Value;
            if (values.Count == 0)
            {
                schema[colName] = typeof(string);
                continue;
            }

            bool allInt = true, allLong = true, allDouble = true, allBool = true, allDate = true;

            foreach (var val in values)
            {
                if (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False)
                {
                    allInt = allLong = allDouble = allDate = false;
                }
                else if (val.ValueKind == JsonValueKind.Number)
                {
                    allBool = allDate = false;
                    bool isInt = val.TryGetInt32(out _);
                    bool isLong = val.TryGetInt64(out _);
                    bool isDouble = val.TryGetDouble(out _);
                    if (!isInt) allInt = false;
                    if (!isLong) allLong = false;
                    if (!isDouble) allDouble = false;
                }
                else if (val.ValueKind == JsonValueKind.String)
                {
                    allInt = allLong = allDouble = allBool = false;
                    var str = val.GetString();
                    if (string.IsNullOrEmpty(str) || str.Length < 10 || !str.Contains('-') || !val.TryGetDateTime(out _))
                    {
                        allDate = false;
                    }
                }
                else
                {
                    allInt = allLong = allDouble = allBool = allDate = false;
                }
            }

            if (allInt) schema[colName] = typeof(int);
            else if (allLong) schema[colName] = typeof(long);
            else if (allDouble) schema[colName] = typeof(double);
            else if (allBool) schema[colName] = typeof(bool);
            else if (allDate) schema[colName] = typeof(DateTime);
            else schema[colName] = typeof(string);
        }

        return schema;
    }

    private abstract class ColumnBuilder
    {
        public int CurrentCount { get; protected set; }
        public abstract void ParseAndSet(JsonElement element, int rowIndex);
        public abstract void CatchUpMissing(int targetRowIndex);
        public abstract IColumn Build();
    }

    private sealed class IntColumnBuilder : ColumnBuilder
    {
        private int[] _data = ArrayPool<int>.Shared.Rent(32768);
        private bool[] _naMask = ArrayPool<bool>.Shared.Rent(32768);

        private void AddValueInternal(int value, bool isNa)
        {
            if (CurrentCount == _data.Length)
            {
                int newCap = _data.Length * 2;
                var newData = ArrayPool<int>.Shared.Rent(newCap);
                var newMask = ArrayPool<bool>.Shared.Rent(newCap);
                Array.Copy(_data, newData, CurrentCount);
                Array.Copy(_naMask, newMask, CurrentCount);

                ArrayPool<int>.Shared.Return(_data);
                ArrayPool<bool>.Shared.Return(_naMask);

                _data = newData;
                _naMask = newMask;
            }
            _data[CurrentCount] = value;
            _naMask[CurrentCount] = isNa;
            CurrentCount++;
        }

        public override void CatchUpMissing(int targetRowIndex)
        {
            while (CurrentCount < targetRowIndex) AddValueInternal(default, true);
        }

        public override void ParseAndSet(JsonElement element, int rowIndex)
        {
            CatchUpMissing(rowIndex);
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
            {
                AddValueInternal(default, true);
            }
            else if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var nv)) AddValueInternal(nv, false);
            else if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var sv)) AddValueInternal(sv, false);
            else AddValueInternal(default, true);
        }

        public override IColumn Build()
        {
            var finalData = _data.AsSpan(0, CurrentCount).ToArray();
            var finalMask = _naMask.AsSpan(0, CurrentCount).ToArray();

            ArrayPool<int>.Shared.Return(_data);
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
            return new PrimitiveColumn<int>(finalData, finalMask, isOwner: false);
        }
    }

    private sealed class LongColumnBuilder : ColumnBuilder
    {
        private long[] _data = ArrayPool<long>.Shared.Rent(32768);
        private bool[] _naMask = ArrayPool<bool>.Shared.Rent(32768);

        private void AddValueInternal(long value, bool isNa)
        {
            if (CurrentCount == _data.Length)
            {
                int newCap = _data.Length * 2;
                var newData = ArrayPool<long>.Shared.Rent(newCap);
                var newMask = ArrayPool<bool>.Shared.Rent(newCap);
                Array.Copy(_data, newData, CurrentCount);
                Array.Copy(_naMask, newMask, CurrentCount);

                ArrayPool<long>.Shared.Return(_data);
                ArrayPool<bool>.Shared.Return(_naMask);

                _data = newData;
                _naMask = newMask;
            }
            _data[CurrentCount] = value;
            _naMask[CurrentCount] = isNa;
            CurrentCount++;
        }

        public override void CatchUpMissing(int targetRowIndex)
        {
            while (CurrentCount < targetRowIndex) AddValueInternal(default, true);
        }

        public override void ParseAndSet(JsonElement element, int rowIndex)
        {
            CatchUpMissing(rowIndex);
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
            {
                AddValueInternal(default, true);
            }
            else if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var nv)) AddValueInternal(nv, false);
            else if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out var sv)) AddValueInternal(sv, false);
            else AddValueInternal(default, true);
        }

        public override IColumn Build()
        {
            var finalData = _data.AsSpan(0, CurrentCount).ToArray();
            var finalMask = _naMask.AsSpan(0, CurrentCount).ToArray();

            ArrayPool<long>.Shared.Return(_data);
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
            return new PrimitiveColumn<long>(finalData, finalMask, isOwner: false);
        }
    }

    private sealed class DoubleColumnBuilder : ColumnBuilder
    {
        private double[] _data = ArrayPool<double>.Shared.Rent(32768);
        private bool[] _naMask = ArrayPool<bool>.Shared.Rent(32768);

        private void AddValueInternal(double value, bool isNa)
        {
            if (CurrentCount == _data.Length)
            {
                int newCap = _data.Length * 2;
                var newData = ArrayPool<double>.Shared.Rent(newCap);
                var newMask = ArrayPool<bool>.Shared.Rent(newCap);
                Array.Copy(_data, newData, CurrentCount);
                Array.Copy(_naMask, newMask, CurrentCount);

                ArrayPool<double>.Shared.Return(_data);
                ArrayPool<bool>.Shared.Return(_naMask);

                _data = newData;
                _naMask = newMask;
            }
            _data[CurrentCount] = value;
            _naMask[CurrentCount] = isNa;
            CurrentCount++;
        }

        public override void CatchUpMissing(int targetRowIndex)
        {
            while (CurrentCount < targetRowIndex) AddValueInternal(default, true);
        }

        public override void ParseAndSet(JsonElement element, int rowIndex)
        {
            CatchUpMissing(rowIndex);
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
            {
                AddValueInternal(default, true);
            }
            else if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var nv)) AddValueInternal(nv, false);
            else if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), out var sv)) AddValueInternal(sv, false);
            else AddValueInternal(default, true);
        }

        public override IColumn Build()
        {
            var finalData = _data.AsSpan(0, CurrentCount).ToArray();
            var finalMask = _naMask.AsSpan(0, CurrentCount).ToArray();

            ArrayPool<double>.Shared.Return(_data);
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
            return new PrimitiveColumn<double>(finalData, finalMask, isOwner: false);
        }
    }

    private sealed class BoolColumnBuilder : ColumnBuilder
    {
        private bool[] _data = ArrayPool<bool>.Shared.Rent(32768);
        private bool[] _naMask = ArrayPool<bool>.Shared.Rent(32768);

        private void AddValueInternal(bool value, bool isNa)
        {
            if (CurrentCount == _data.Length)
            {
                int newCap = _data.Length * 2;
                var newData = ArrayPool<bool>.Shared.Rent(newCap);
                var newMask = ArrayPool<bool>.Shared.Rent(newCap);
                Array.Copy(_data, newData, CurrentCount);
                Array.Copy(_naMask, newMask, CurrentCount);

                ArrayPool<bool>.Shared.Return(_data);
                ArrayPool<bool>.Shared.Return(_naMask);

                _data = newData;
                _naMask = newMask;
            }
            _data[CurrentCount] = value;
            _naMask[CurrentCount] = isNa;
            CurrentCount++;
        }

        public override void CatchUpMissing(int targetRowIndex)
        {
            while (CurrentCount < targetRowIndex) AddValueInternal(default, true);
        }

        public override void ParseAndSet(JsonElement element, int rowIndex)
        {
            CatchUpMissing(rowIndex);
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
            {
                AddValueInternal(default, true);
            }
            else if (element.ValueKind == JsonValueKind.True) AddValueInternal(true, false);
            else if (element.ValueKind == JsonValueKind.False) AddValueInternal(false, false);
            else AddValueInternal(default, true);
        }

        public override IColumn Build()
        {
            var finalData = _data.AsSpan(0, CurrentCount).ToArray();
            var finalMask = _naMask.AsSpan(0, CurrentCount).ToArray();

            ArrayPool<bool>.Shared.Return(_data);
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
            return new PrimitiveColumn<bool>(finalData, finalMask, isOwner: false);
        }
    }

    private sealed class DateTimeColumnBuilder : ColumnBuilder
    {
        private DateTime[] _data = ArrayPool<DateTime>.Shared.Rent(32768);
        private bool[] _naMask = ArrayPool<bool>.Shared.Rent(32768);

        private void AddValueInternal(DateTime value, bool isNa)
        {
            if (CurrentCount == _data.Length)
            {
                int newCap = _data.Length * 2;
                var newData = ArrayPool<DateTime>.Shared.Rent(newCap);
                var newMask = ArrayPool<bool>.Shared.Rent(newCap);
                Array.Copy(_data, newData, CurrentCount);
                Array.Copy(_naMask, newMask, CurrentCount);

                ArrayPool<DateTime>.Shared.Return(_data);
                ArrayPool<bool>.Shared.Return(_naMask);

                _data = newData;
                _naMask = newMask;
            }
            _data[CurrentCount] = value;
            _naMask[CurrentCount] = isNa;
            CurrentCount++;
        }

        public override void CatchUpMissing(int targetRowIndex)
        {
            while (CurrentCount < targetRowIndex) AddValueInternal(default, true);
        }

        public override void ParseAndSet(JsonElement element, int rowIndex)
        {
            CatchUpMissing(rowIndex);
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
            {
                AddValueInternal(default, true);
            }
            else if (element.ValueKind == JsonValueKind.String && element.TryGetDateTime(out var v)) AddValueInternal(v, false);
            else AddValueInternal(default, true);
        }

        public override IColumn Build()
        {
            var finalData = _data.AsSpan(0, CurrentCount).ToArray();
            var finalMask = _naMask.AsSpan(0, CurrentCount).ToArray();

            ArrayPool<DateTime>.Shared.Return(_data);
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
            return new PrimitiveColumn<DateTime>(finalData, finalMask, isOwner: false);
        }
    }

    private sealed class StringColumnBuilder : ColumnBuilder
    {
        private string?[] _data = ArrayPool<string?>.Shared.Rent(32768);
        private bool[] _naMask = ArrayPool<bool>.Shared.Rent(32768);

        private void AddValueInternal(string? value, bool isNa)
        {
            if (CurrentCount == _data.Length)
            {
                int newCap = _data.Length * 2;
                var newData = ArrayPool<string?>.Shared.Rent(newCap);
                var newMask = ArrayPool<bool>.Shared.Rent(newCap);
                Array.Copy(_data, newData, CurrentCount);
                Array.Copy(_naMask, newMask, CurrentCount);

                ArrayPool<string?>.Shared.Return(_data, clearArray: true);
                ArrayPool<bool>.Shared.Return(_naMask);

                _data = newData;
                _naMask = newMask;
            }
            _data[CurrentCount] = value;
            _naMask[CurrentCount] = isNa;
            CurrentCount++;
        }

        public override void CatchUpMissing(int targetRowIndex)
        {
            while (CurrentCount < targetRowIndex) AddValueInternal(null, true);
        }

        public override void ParseAndSet(JsonElement element, int rowIndex)
        {
            CatchUpMissing(rowIndex);
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
            {
                AddValueInternal(null, true);
            }
            else
            {
                AddValueInternal(element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(), false);
            }
        }

        public override IColumn Build()
        {
            var finalData = _data.AsSpan(0, CurrentCount).ToArray();
            var finalMask = _naMask.AsSpan(0, CurrentCount).ToArray();

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

    private static Dictionary<string, ColumnBuilder> InitializeColumnBuilders(Dictionary<string, Type> schema)
    {
        var builders = new Dictionary<string, ColumnBuilder>();
        foreach (var kvp in schema)
        {
            Type t = kvp.Value;
            if (t == typeof(int)) builders[kvp.Key] = new IntColumnBuilder();
            else if (t == typeof(long)) builders[kvp.Key] = new LongColumnBuilder();
            else if (t == typeof(double)) builders[kvp.Key] = new DoubleColumnBuilder();
            else if (t == typeof(DateTime)) builders[kvp.Key] = new DateTimeColumnBuilder();
            else if (t == typeof(bool)) builders[kvp.Key] = new BoolColumnBuilder();
            else builders[kvp.Key] = new StringColumnBuilder();
        }
        return builders;
    }

    private static void ParseJsonRowToBuilders(JsonElement element, Dictionary<string, ColumnBuilder> builders, int rowIndex)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (builders.TryGetValue(prop.Name, out var builder))
            {
                builder.ParseAndSet(prop.Value, rowIndex);
            }
        }
    }

    private static DataFrame FinalizeDataFrame(Dictionary<string, ColumnBuilder> builders, int totalRows)
    {
        var columns = new Dictionary<string, IColumn>();
        foreach (var kvp in builders)
        {
            var builder = kvp.Value;
            // 미처 채우지 못한 나머지 행(Missing trailing cols) 보강
            builder.CatchUpMissing(totalRows);
            columns[kvp.Key] = builder.Build();
        }
        return new DataFrame(columns, new RangeIndex(totalRows));
    }
}