using System.Text.Json;
using TeruTeruPandas.IO;

namespace TeruTeruPandas.Core;

/// <summary>
/// DataUniverse의 IO 기능 확장
/// 전체 Universe를 JSON, SQLite, 디렉터리 형태로 저장/로드
/// </summary>
public static class DataUniverseIO
{
    /// <summary>
    /// DataUniverse를 JSON 파일로 저장
    /// 각 테이블을 배열 형태로 저장
    /// </summary>
    public static void ToJson(this DataUniverse universe, string filePath, bool pretty = true)
    {
        var universeData = new Dictionary<string, object>
        {
            ["metadata"] = new
            {
                version = "1.0",
                created = DateTime.Now,
                tableCount = universe.TableCount
            },
            ["tables"] = new Dictionary<string, object>()
        };

        var tablesDict = (Dictionary<string, object>)universeData["tables"];
        
        foreach (var tableName in universe.TableNames)
        {
            var df = universe.GetTable(tableName);
            if (df == null) continue;

            var metadata = universe.GetMetadata(tableName);
            
            // DataFrame을 Dictionary로 변환
            var tableData = new Dictionary<string, object>
            {
                ["metadata"] = new
                {
                    description = metadata?.Description,
                    created = metadata?.CreatedAt,
                    lastModified = metadata?.LastModified,
                    rows = df.RowCount,
                    columns = df.ColumnCount
                },
                ["data"] = DataFrameToRecords(df)
            };
            
            tablesDict[tableName] = tableData;
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = pretty,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(universeData, options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// JSON 파일에서 DataUniverse 로드
    /// </summary>
    public static DataUniverse FromJson(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var universeData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        
        if (universeData == null)
            throw new InvalidDataException("Invalid universe JSON format");

        var universe = new DataUniverse();

        if (universeData.TryGetValue("tables", out var tablesElement))
        {
            var tables = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tablesElement.GetRawText());
            
            if (tables != null)
            {
                foreach (var kvp in tables)
                {
                    var tableName = kvp.Key;
                    var tableData = kvp.Value;
                    
                    // Extract metadata
                    string? description = null;
                    if (tableData.TryGetProperty("metadata", out var metadataElement))
                    {
                        if (metadataElement.TryGetProperty("description", out var descElement))
                        {
                            description = descElement.GetString();
                        }
                    }

                    // Extract data
                    if (tableData.TryGetProperty("data", out var dataElement))
                    {
                        var records = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(dataElement.GetRawText());
                        if (records != null && records.Count > 0)
                        {
                            var df = RecordsToDataFrame(records);
                            universe.AddTable(tableName, df, description);
                        }
                    }
                }
            }
        }

        return universe;
    }

    /// <summary>
    /// DataUniverse를 디렉터리에 저장 (각 테이블을 개별 CSV 파일로)
    /// </summary>
    public static void ToDirectory(this DataUniverse universe, string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        // 메타데이터 저장
        var metadataPath = Path.Combine(directoryPath, "_metadata.json");
        var metadata = new Dictionary<string, object>
        {
            ["version"] = "1.0",
            ["created"] = DateTime.Now,
            ["tableCount"] = universe.TableCount,
            ["tables"] = universe.GetAllMetadata()
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metadataPath, json);

        // 각 테이블을 CSV로 저장
        foreach (var tableName in universe.TableNames)
        {
            var df = universe.GetTable(tableName);
            if (df == null) continue;

            var csvPath = Path.Combine(directoryPath, $"{SanitizeFileName(tableName)}.csv");
            CsvWriter.ToCsv(df, csvPath);
        }
    }

    /// <summary>
    /// 디렉터리에서 DataUniverse 로드
    /// </summary>
    public static DataUniverse FromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var universe = new DataUniverse();

        // 메타데이터 로드
        var metadataPath = Path.Combine(directoryPath, "_metadata.json");
        Dictionary<string, DataFrameMetadata>? metadataMap = null;

        if (File.Exists(metadataPath))
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            
            if (metadata != null && metadata.TryGetValue("tables", out var tablesElement))
            {
                var tables = JsonSerializer.Deserialize<List<DataFrameMetadata>>(tablesElement.GetRawText());
                if (tables != null)
                {
                    metadataMap = tables.ToDictionary(t => t.TableName, t => t);
                }
            }
        }

        // CSV 파일 로드
        var csvFiles = Directory.GetFiles(directoryPath, "*.csv");
        foreach (var csvFile in csvFiles)
        {
            var tableName = Path.GetFileNameWithoutExtension(csvFile);
            var df = CsvReader.ReadCsv(csvFile);
            
            string? description = null;
            if (metadataMap != null && metadataMap.TryGetValue(tableName, out var meta))
            {
                description = meta.Description;
            }

            universe.AddTable(tableName, df, description);
        }

        return universe;
    }

    /// <summary>
    /// DataUniverse를 SQLite 데이터베이스로 저장
    /// 각 테이블이 SQLite 테이블로 저장됨
    /// </summary>
    public static void ToSqlite(this DataUniverse universe, string dbPath, bool overwrite = false)
    {
        if (overwrite && File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }

        var connectionString = $"Data Source={dbPath}";
        
        foreach (var tableName in universe.TableNames)
        {
            var df = universe.GetTable(tableName);
            if (df == null) continue;

            SqliteIO.ToSqlite(df, connectionString, tableName);
        }
    }

    /// <summary>
    /// SQLite 데이터베이스에서 DataUniverse 로드
    /// </summary>
    public static DataUniverse FromSqlite(string dbPath)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"Database file not found: {dbPath}");

        var universe = new DataUniverse();
        var tableNames = SqliteIO.GetTableNames(dbPath);

        foreach (var tableName in tableNames)
        {
            var df = SqliteIO.ReadSqliteTable(dbPath, tableName);
            universe.AddTable(tableName, df);
        }

        return universe;
    }

    /// <summary>
    /// 특정 테이블만 CSV로 저장
    /// </summary>
    public static void ExportTableToCsv(this DataUniverse universe, string tableName, string filePath)
    {
        var df = universe.GetTableOrThrow(tableName);
        CsvWriter.ToCsv(df, filePath);
    }

    /// <summary>
    /// CSV 파일에서 테이블 가져오기
    /// </summary>
    public static void ImportTableFromCsv(this DataUniverse universe, string tableName, string filePath, 
        string? description = null, bool overwrite = false)
    {
        if (!overwrite && universe.ContainsTable(tableName))
            throw new InvalidOperationException($"Table '{tableName}' already exists");

        var df = CsvReader.ReadCsv(filePath);
        
        if (overwrite)
        {
            universe.AddOrUpdateTable(tableName, df, description);
        }
        else
        {
            universe.AddTable(tableName, df, description);
        }
    }

    /// <summary>
    /// 특정 테이블만 JSON으로 저장
    /// </summary>
    public static void ExportTableToJson(this DataUniverse universe, string tableName, string filePath, bool pretty = true)
    {
        var df = universe.GetTableOrThrow(tableName);
        JsonIO.ToJson(df, filePath, pretty);
    }

    /// <summary>
    /// JSON 파일에서 테이블 가져오기
    /// </summary>
    public static void ImportTableFromJson(this DataUniverse universe, string tableName, string filePath, 
        string? description = null, bool overwrite = false)
    {
        if (!overwrite && universe.ContainsTable(tableName))
            throw new InvalidOperationException($"Table '{tableName}' already exists");

        var df = JsonIO.ReadJson(filePath);
        
        if (overwrite)
        {
            universe.AddOrUpdateTable(tableName, df, description);
        }
        else
        {
            universe.AddTable(tableName, df, description);
        }
    }

    // Helper Methods
    private static List<Dictionary<string, object?>> DataFrameToRecords(DataFrame df)
    {
        var records = new List<Dictionary<string, object?>>();
        
        for (int i = 0; i < df.RowCount; i++)
        {
            var record = new Dictionary<string, object?>();
            foreach (var col in df.Columns)
            {
                var column = df[col];
                record[col] = column.IsNA(i) ? null : column.GetValue(i);
            }
            records.Add(record);
        }
        
        return records;
    }

    private static DataFrame RecordsToDataFrame(List<Dictionary<string, JsonElement>> records)
    {
        if (records.Count == 0)
            throw new ArgumentException("No records to convert");

        var firstRecord = records[0];
        var columnNames = firstRecord.Keys.ToArray();
        var rowCount = records.Count;

        // 컬럼별로 데이터 수집 및 타입 추론
        var columns = new Dictionary<string, Core.Column.IColumn>();

        foreach (var columnName in columnNames)
        {
            var values = new List<object?>();
            Type? inferredType = null;

            foreach (var record in records)
            {
                if (record.TryGetValue(columnName, out var element))
                {
                    object? value = null;

                    switch (element.ValueKind)
                    {
                        case JsonValueKind.Number:
                            if (element.TryGetInt32(out var intValue))
                            {
                                value = intValue;
                                inferredType ??= typeof(int);
                            }
                            else if (element.TryGetDouble(out var doubleValue))
                            {
                                value = doubleValue;
                                inferredType ??= typeof(double);
                            }
                            break;
                        case JsonValueKind.String:
                            value = element.GetString();
                            inferredType ??= typeof(string);
                            break;
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            value = element.GetBoolean();
                            inferredType ??= typeof(bool);
                            break;
                        case JsonValueKind.Null:
                            value = null;
                            break;
                    }

                    values.Add(value);
                }
                else
                {
                    values.Add(null);
                }
            }

            // 컬럼 생성
            inferredType ??= typeof(string);

            if (inferredType == typeof(int))
            {
                var intValues = values.Select(v => v == null ? 0 : Convert.ToInt32(v)).ToArray();
                columns[columnName] = new Core.Column.PrimitiveColumn<int>(intValues);
            }
            else if (inferredType == typeof(double))
            {
                var doubleValues = values.Select(v => v == null ? 0.0 : Convert.ToDouble(v)).ToArray();
                columns[columnName] = new Core.Column.PrimitiveColumn<double>(doubleValues);
            }
            else if (inferredType == typeof(bool))
            {
                var boolValues = values.Select(v => v != null && Convert.ToBoolean(v)).ToArray();
                columns[columnName] = new Core.Column.PrimitiveColumn<bool>(boolValues);
            }
            else
            {
                var stringValues = values.Select(v => v?.ToString() ?? "").ToArray();
                columns[columnName] = new Core.Column.StringColumn(stringValues);
            }
        }

        return new DataFrame(columns);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}
