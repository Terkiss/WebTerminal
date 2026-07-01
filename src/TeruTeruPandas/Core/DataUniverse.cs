using System.Collections.Concurrent;
using System.Text;

namespace TeruTeruPandas.Core;

/// <summary>
/// 데이터프레임 유니버스 - 여러 DataFrame을 DBMS처럼 관리하는 컨테이너입니다.
/// 각 DataFrame을 독립된 테이블로 취급하여 저장, 검색, 관계 연산(Join) 및 통합 관리 기능을 제공합니다.
/// 메모리 내 데이터 웨어하우스 역할을 수행합니다.
/// </summary>
public class DataUniverse
{
    private readonly ConcurrentDictionary<string, DataFrame> _tables;
    private readonly Dictionary<string, DataFrameMetadata> _metadata;
    private readonly object _metadataLock = new();

    /// <summary>
    /// 유니버스에 등록된 전체 테이블 개수를 가져옵니다.
    /// </summary>
    public int TableCount => _tables.Count;

    /// <summary>
    /// 등록된 모든 테이블의 이름 목록을 가져옵니다.
    /// </summary>
    public IEnumerable<string> TableNames => _tables.Keys.ToList();

    public DataUniverse()
    {
        _tables = new ConcurrentDictionary<string, DataFrame>(StringComparer.OrdinalIgnoreCase);
        _metadata = new Dictionary<string, DataFrameMetadata>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 새로운 테이블(DataFrame)을 추가합니다.
    /// </summary>
    /// <param name="tableName">식별자로 사용할 테이블 이름</param>
    /// <param name="dataFrame">추가할 데이터프레임 객체</param>
    /// <param name="description">테이블에 대한 설명(옵션)</param>
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

        // 메타데이터 업데이트 (동기화 블록)
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
    /// 기존 테이블의 데이터를 업데이트합니다. (덮어쓰기)
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
    /// 테이블이 존재하면 업데이트하고, 없으면 새로 추가합니다 (Upsert).
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
    /// 이름으로 테이블(DataFrame)을 조회합니다. 없으면 null을 반환합니다.
    /// </summary>
    public DataFrame? GetTable(string tableName)
    {
        _tables.TryGetValue(tableName, out var dataFrame);
        return dataFrame;
    }

    /// <summary>
    /// 이름으로 테이블을 조회합니다. 테이블이 없으면 KeyNotFoundException을 발생시킵니다.
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
    /// 특정 이름의 테이블이 존재하는지 확인합니다.
    /// </summary>
    public bool ContainsTable(string tableName)
    {
        return _tables.ContainsKey(tableName);
    }

    /// <summary>
    /// 테이블을 유니버스에서 제거합니다.
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
    /// 등록된 모든 테이블을 제거합니다.
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
    /// 특정 컬럼 이름을 포함하고 있는 모든 테이블 목록을 검색합니다.
    /// </summary>
    public List<string> FindTablesWithColumn(string columnName)
    {
        return _tables
            .Where(kvp => kvp.Value.Columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// 지정된 여러 컬럼들을 모두 포함하고 있는 테이블 목록을 검색합니다.
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
    /// 행 개수 범위를 기준으로 테이블을 검색합니다.
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
    /// 사용자 정의 조건(Predicate)을 만족하는 테이블 목록을 검색합니다.
    /// </summary>
    public List<string> FindTables(Func<DataFrame, bool> predicate)
    {
        return _tables
            .Where(kvp => predicate(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// 이름 패턴(와일드카드 *, ? 지원)을 기준으로 테이블을 검색합니다.
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
    /// 특정 테이블의 메타데이터(생성일, 수정일, 컬럼 목록 등)를 조회합니다.
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
    /// 모든 테이블의 메타데이터 목록을 가져옵니다.
    /// </summary>
    public List<DataFrameMetadata> GetAllMetadata()
    {
        lock (_metadataLock)
        {
            return _metadata.Values.ToList();
        }
    }

    /// <summary>
    /// 유니버스 전체의 통계 정보(전체 행수, 셀 수, 최대 테이블 등)를 계산하여 반환합니다.
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
    /// 두 테이블 간의 관계형 조인(Join) 연산을 수행합니다.
    /// </summary>
    /// <param name="leftTableName">왼쪽 테이블 이름</param>
    /// <param name="rightTableName">오른쪽 테이블 이름</param>
    /// <param name="on">기준이 될 조인 키 컬럼 이름</param>
    /// <param name="how">조인 방식 ("inner", "left", "right", "outer")</param>
    public DataFrame Join(string leftTableName, string rightTableName, string on, string how = "inner")
    {
        var left = GetTableOrThrow(leftTableName);
        var right = GetTableOrThrow(rightTableName);
        
        return left.Merge(right, on: on, how: how);
    }

    /// <summary>
    /// 여러 테이블을 하나로 합치는 Concat 연산을 수행합니다.
    /// </summary>
    /// <param name="tableNames">합칠 테이블들의 이름 목록</param>
    /// <param name="axis">0: 수직 연결(행 추가), 1: 수평 연결(열 추가)</param>
    public DataFrame ConcatTables(IEnumerable<string> tableNames, int axis = 0)
    {
        var dataFrames = tableNames.Select(name => GetTableOrThrow(name)).ToList();
        
        if (dataFrames.Count == 0)
            throw new ArgumentException("At least one table name is required");
        
        return DataFrameJoinExtensions.Concat(dataFrames, axis);
    }

    /// <summary>
    /// 기존 테이블의 데이터를 복사하여 새로운 이름의 테이블로 등록합니다.
    /// </summary>
    public void CopyTable(string sourceTableName, string destTableName, bool overwrite = false)
    {
        var source = GetTableOrThrow(sourceTableName);
        
        if (!overwrite && ContainsTable(destTableName))
            throw new InvalidOperationException($"Table '{destTableName}' already exists");

        // DataFrame 복사 (현재는 얕은 복사 수행)
        var newDataFrame = source;

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
    /// 테이블의 이름을 변경합니다.
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
    /// 유니버스의 상태를 요약 문자열로 반환합니다.
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
    /// SQL 쿼리를 실행하여 결과 데이터프레임을 반환합니다.
    /// SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT 문법을 지원합니다.
    /// </summary>
    /// <param name="sql">실행할 SQL 쿼리 문자열</param>
    /// <returns>쿼리 결과로 생성된 DataFrame</returns>
    public DataFrame SqlExecute(string sql)
    {
        var parser = new SimpleSqlParser(sql);
        var query = parser.Parse();
        var executor = new SqlQueryExecutor(this);
        return executor.Execute(query);
    }

    /// <summary>
    /// SQL 쿼리의 구문 유효성을 검사합니다.
    /// </summary>
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
    /// 인덱서(Indexer)를 사용하여 테이블에 편리하게 접근합니다.
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
/// DataFrame의 메타데이터 정보를 저장하는 클래스입니다.
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
/// 유니버스 전체의 통계 지표를 나타내는 클래스입니다.
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
