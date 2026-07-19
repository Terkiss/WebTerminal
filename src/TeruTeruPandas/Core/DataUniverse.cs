using System.Collections.Concurrent;
using System.Text;

namespace TeruTeruPandas.Core;

/// <summary>
/// 데이터프레임 유니버스 - 여러 DataFrame을 DBMS처럼 관리하는 컨테이너
/// 각 DataFrame을 테이블로 취급하여 저장, 검색, 관리하는 기능 제공
/// </summary>
public class DataUniverse
{
    private readonly ConcurrentDictionary<string, DataFrame> _tables;
    private readonly Dictionary<string, DataFrameMetadata> _metadata;
    private readonly object _metadataLock = new();

    public int TableCount => _tables.Count;
    public IEnumerable<string> TableNames => _tables.Keys.ToList();

    public DataUniverse()
    {
        _tables = new ConcurrentDictionary<string, DataFrame>(StringComparer.OrdinalIgnoreCase);
        _metadata = new Dictionary<string, DataFrameMetadata>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 테이블(DataFrame) 추가
    /// </summary>
    public void AddTable(string tableName, DataFrame dataFrame, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be empty", nameof(tableName));

        if (dataFrame == null)
            throw new ArgumentNullException(nameof(dataFrame));

        if (!_tables.TryAdd(tableName, dataFrame))
        {
            throw new InvalidOperationException($"Table '{tableName}' already exists. Use UpdateTable to modify existing table.");
        }

        lock (_metadataLock)
        {
            _metadata[tableName] = new DataFrameMetadata
            {
                TableName = tableName,
                Description = description,
                CreatedAt = DateTime.Now,
                LastModified = DateTime.Now,
                RowCount = dataFrame.RowCount,
                ColumnCount = dataFrame.ColumnCount,
                Columns = dataFrame.Columns.ToList()
            };
        }
    }

    /// <summary>
    /// 테이블 업데이트 (기존 테이블 덮어쓰기)
    /// </summary>
    public void UpdateTable(string tableName, DataFrame dataFrame)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be empty", nameof(tableName));

        if (dataFrame == null)
            throw new ArgumentNullException(nameof(dataFrame));

        _tables[tableName] = dataFrame;

        lock (_metadataLock)
        {
            if (_metadata.TryGetValue(tableName, out var metadata))
            {
                metadata.LastModified = DateTime.Now;
                metadata.RowCount = dataFrame.RowCount;
                metadata.ColumnCount = dataFrame.ColumnCount;
                metadata.Columns = dataFrame.Columns.ToList();
            }
        }
    }

    /// <summary>
    /// 테이블이 존재하면 업데이트, 없으면 추가
    /// </summary>
    public void AddOrUpdateTable(string tableName, DataFrame dataFrame, string? description = null)
    {
        if (_tables.ContainsKey(tableName))
        {
            UpdateTable(tableName, dataFrame);
        }
        else
        {
            AddTable(tableName, dataFrame, description);
        }
    }

    /// <summary>
    /// 테이블 조회
    /// </summary>
    public DataFrame? GetTable(string tableName)
    {
        _tables.TryGetValue(tableName, out var dataFrame);
        return dataFrame;
    }

    /// <summary>
    /// 테이블 조회 (없으면 예외 발생)
    /// </summary>
    public DataFrame GetTableOrThrow(string tableName)
    {
        if (!_tables.TryGetValue(tableName, out var dataFrame))
        {
            throw new KeyNotFoundException($"Table '{tableName}' not found in universe");
        }
        return dataFrame;
    }

    /// <summary>
    /// 테이블 존재 여부 확인
    /// </summary>
    public bool ContainsTable(string tableName)
    {
        return _tables.ContainsKey(tableName);
    }

    /// <summary>
    /// 테이블 제거
    /// </summary>
    public bool RemoveTable(string tableName)
    {
        var removed = _tables.TryRemove(tableName, out _);
        if (removed)
        {
            lock (_metadataLock)
            {
                _metadata.Remove(tableName);
            }
        }
        return removed;
    }

    /// <summary>
    /// 모든 테이블 제거
    /// </summary>
    public void ClearAll()
    {
        _tables.Clear();
        lock (_metadataLock)
        {
            _metadata.Clear();
        }
    }

    /// <summary>
    /// 특정 컬럼을 포함하는 테이블 검색
    /// </summary>
    public List<string> FindTablesWithColumn(string columnName)
    {
        return _tables
            .Where(kvp => kvp.Value.Columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// 특정 컬럼들을 모두 포함하는 테이블 검색
    /// </summary>
    public List<string> FindTablesWithColumns(params string[] columnNames)
    {
        var columnSet = new HashSet<string>(columnNames, StringComparer.OrdinalIgnoreCase);
        return _tables
            .Where(kvp => columnSet.All(col => kvp.Value.Columns.Contains(col, StringComparer.OrdinalIgnoreCase)))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// 행 개수 조건으로 테이블 검색
    /// </summary>
    public List<string> FindTablesByRowCount(int minRows, int? maxRows = null)
    {
        return _tables
            .Where(kvp =>
            {
                var rowCount = kvp.Value.RowCount;
                return rowCount >= minRows && (maxRows == null || rowCount <= maxRows.Value);
            })
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// 커스텀 조건으로 테이블 검색
    /// </summary>
    public List<string> FindTables(Func<DataFrame, bool> predicate)
    {
        return _tables
            .Where(kvp => predicate(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// 이름 패턴으로 테이블 검색 (와일드카드 지원)
    /// </summary>
    public List<string> FindTablesByNamePattern(string pattern)
    {
        var regex = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        return _tables.Keys
            .Where(name => regex.IsMatch(name))
            .ToList();
    }

    /// <summary>
    /// 테이블 메타데이터 조회
    /// </summary>
    public DataFrameMetadata? GetMetadata(string tableName)
    {
        lock (_metadataLock)
        {
            _metadata.TryGetValue(tableName, out var metadata);
            return metadata;
        }
    }

    /// <summary>
    /// 모든 테이블의 메타데이터 조회
    /// </summary>
    public List<DataFrameMetadata> GetAllMetadata()
    {
        lock (_metadataLock)
        {
            return _metadata.Values.ToList();
        }
    }

    /// <summary>
    /// 테이블 통계 정보
    /// </summary>
    public UniverseStatistics GetStatistics()
    {
        var stats = new UniverseStatistics
        {
            TotalTables = _tables.Count,
            TotalRows = _tables.Sum(kvp => (long)kvp.Value.RowCount),
            TotalColumns = _tables.Sum(kvp => (long)kvp.Value.ColumnCount),
            TotalCells = _tables.Sum(kvp => (long)kvp.Value.RowCount * kvp.Value.ColumnCount)
        };

        if (_tables.Count > 0)
        {
            stats.AverageRowsPerTable = stats.TotalRows / _tables.Count;
            stats.AverageColumnsPerTable = stats.TotalColumns / _tables.Count;
            stats.LargestTable = _tables.OrderByDescending(kvp => kvp.Value.RowCount).First().Key;
            stats.SmallestTable = _tables.OrderBy(kvp => kvp.Value.RowCount).First().Key;
        }

        return stats;
    }

    /// <summary>
    /// 두 테이블 조인
    /// </summary>
    public DataFrame Join(string leftTableName, string rightTableName, string on, string how = "inner")
    {
        var left = GetTableOrThrow(leftTableName);
        var right = GetTableOrThrow(rightTableName);
        
        return left.Merge(right, on: on, how: how);
    }

    /// <summary>
    /// 여러 테이블 연결 (Concat)
    /// </summary>
    public DataFrame ConcatTables(IEnumerable<string> tableNames, int axis = 0)
    {
        var dataFrames = tableNames.Select(name => GetTableOrThrow(name)).ToList();
        
        if (dataFrames.Count == 0)
            throw new ArgumentException("At least one table name is required");
        
        return DataFrameJoinExtensions.Concat(dataFrames, axis);
    }

    /// <summary>
    /// 테이블 복사
    /// </summary>
    public void CopyTable(string sourceTableName, string destTableName, bool overwrite = false)
    {
        var source = GetTableOrThrow(sourceTableName);
        
        if (!overwrite && ContainsTable(destTableName))
            throw new InvalidOperationException($"Table '{destTableName}' already exists");

        // DataFrame 복사 (얕은 복사)
        var newDataFrame = source; // TODO: 깊은 복사 구현 필요 시 Copy 메서드 추가

        if (overwrite)
        {
            AddOrUpdateTable(destTableName, newDataFrame);
        }
        else
        {
            AddTable(destTableName, newDataFrame);
        }
    }

    /// <summary>
    /// 테이블 이름 변경
    /// </summary>
    public void RenameTable(string oldName, string newName)
    {
        if (oldName == newName) return;

        var dataFrame = GetTableOrThrow(oldName);
        
        if (ContainsTable(newName))
            throw new InvalidOperationException($"Table '{newName}' already exists");

        AddTable(newName, dataFrame);
        RemoveTable(oldName);
    }

    /// <summary>
    /// 유니버스 정보 출력
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DataUniverse: {TableCount} tables");
        sb.AppendLine("═══════════════════════════════════════");
        
        foreach (var tableName in TableNames.OrderBy(x => x))
        {
            var df = _tables[tableName];
            var metadata = GetMetadata(tableName);
            
            sb.AppendLine($"📊 {tableName}");
            sb.AppendLine($"   Rows: {df.RowCount:N0}, Columns: {df.ColumnCount}");
            sb.AppendLine($"   Columns: [{string.Join(", ", df.Columns)}]");
            
            if (metadata != null)
            {
                if (!string.IsNullOrEmpty(metadata.Description))
                    sb.AppendLine($"   Description: {metadata.Description}");
                sb.AppendLine($"   Created: {metadata.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            }
            
            sb.AppendLine();
        }
        
        var stats = GetStatistics();
        sb.AppendLine("Statistics:");
        sb.AppendLine($"  Total Rows: {stats.TotalRows:N0}");
        sb.AppendLine($"  Total Cells: {stats.TotalCells:N0}");
        
        return sb.ToString();
    }

    /// <summary>
    /// SQL 쿼리 실행
    /// SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT 지원
    /// </summary>
    /// <param name="sql">실행할 SQL 쿼리</param>
    /// <returns>쿼리 결과 DataFrame</returns>
    public DataFrame SqlExecute(string sql)
    {
        var parser = new SimpleSqlParser(sql);
        var query = parser.Parse();
        var executor = new SqlQueryExecutor(this);
        return executor.Execute(query);
    }

    /// <summary>
    /// SQL 쿼리 유효성 검사
    /// </summary>
    /// <param name="sql">검사할 SQL 쿼리</param>
    /// <param name="error">오류 메시지 (오류 발생 시)</param>
    /// <returns>쿼리가 유효하면 true, 아니면 false</returns>
    public bool ValidateSql(string sql, out string? error)
    {
        try
        {
            var parser = new SimpleSqlParser(sql);
            var query = parser.Parse();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 인덱서를 통한 테이블 접근
    /// </summary>
    public DataFrame? this[string tableName]
    {
        get => GetTable(tableName);
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            AddOrUpdateTable(tableName, value);
        }
    }
}

/// <summary>
/// DataFrame 메타데이터
/// </summary>
public class DataFrameMetadata
{
    public string TableName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<string> Columns { get; set; } = new();
    public Dictionary<string, string> Tags { get; set; } = new();
}

/// <summary>
/// 유니버스 통계 정보
/// </summary>
public class UniverseStatistics
{
    public int TotalTables { get; set; }
    public long TotalRows { get; set; }
    public long TotalColumns { get; set; }
    public long TotalCells { get; set; }
    public double AverageRowsPerTable { get; set; }
    public double AverageColumnsPerTable { get; set; }
    public string? LargestTable { get; set; }
    public string? SmallestTable { get; set; }
}
