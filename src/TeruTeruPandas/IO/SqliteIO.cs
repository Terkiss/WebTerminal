using Microsoft.Data.Sqlite;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.IO;

/// <summary>
/// SQLite 데이터베이스와의 양방향 통신 모듈입니다.
/// DataFrame의 데이터를 곧바로 DB 테이블에 쓰거나, SQL 쿼리 결과를 DataFrame 배열 구조로 로드합니다.
/// 대규모 데이터 분석 결과를 관계형 데이터베이스에 영구 저장하는 용도로 최적화되어 있습니다.
/// </summary>
public static class SqliteIO
{
    /// <summary>
    /// SQLite 데이터베이스에 연결하여 쿼리 결과를 DataFrame으로 읽어옵니다.
    /// </summary>
    /// <param name="connectionString">SQLite 연결 문자열</param>
    /// <param name="query">실행할 SQL SELECT 쿼리</param>
    public static DataFrame ReadSqlite(string connectionString, string query)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = new SqliteCommand(query, connection);
        using var reader = command.ExecuteReader();

        // 1. 결과 셋의 컬럼 및 타입 정보 추출
        var columnNames = new string[reader.FieldCount];
        var columnTypes = new Type[reader.FieldCount];

        for (int i = 0; i < reader.FieldCount; i++)
        {
            columnNames[i] = reader.GetName(i);
            columnTypes[i] = reader.GetFieldType(i);
        }

        // 2. 전체 데이터를 메모리에 로드 (버퍼링)
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        // 3. 열 단위(Columnar) 데이터 구조인 DataFrame으로 변환
        var columns = new Dictionary<string, IColumn>();
        for (int colIndex = 0; colIndex < columnNames.Length; colIndex++)
        {
            var columnName = columnNames[colIndex];
            var columnType = columnTypes[colIndex];

            // 수집된 행 데이터를 열별로 분리하여 전용 컬럼 객체 생성
            columns[columnName] = CreateColumnFromSqliteData(rows, colIndex, columnType);
        }

        return new DataFrame(columns, new RangeIndex(rows.Count));
    }

    /// <summary>
    /// DataFrame의 데이터를 SQLite 테이블로 내보냅니다.
    /// </summary>
    /// <param name="dataFrame">내보낼 데이터프레임</param>
    /// <param name="connectionString">연결 문자열</param>
    /// <param name="tableName">대상 테이블 이름</param>
    /// <param name="ifExists">true일 경우 기존 테이블을 삭제하고 새로 생성</param>
    public static void ToSqlite(DataFrame dataFrame, string connectionString, string tableName, bool ifExists = false)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // 1. 테이블 생성 (스키마에 맞춘 CREATE TABLE SQL 생성)
        var createTableSql = GenerateCreateTableSql(dataFrame, tableName, ifExists);
        using (var command = new SqliteCommand(createTableSql, connection))
        {
            command.ExecuteNonQuery();
        }

        // 2. 데이터 삽입 (매개변수화된 쿼리를 사용한 일괄 삽입)
        var insertSql = GenerateInsertSql(dataFrame, tableName);
        using (var command = new SqliteCommand(insertSql, connection))
        {
            for (int row = 0; row < dataFrame.RowCount; row++)
            {
                command.Parameters.Clear();

                for (int col = 0; col < dataFrame.ColumnCount; col++)
                {
                    var columnName = dataFrame.Columns[col];
                    var column = dataFrame[columnName];

                    var parameterName = $"@p{col}";
                    var value = column.IsNA(row) ? DBNull.Value : column.GetValue(row);
                    command.Parameters.AddWithValue(parameterName, value);
                }

                command.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// SQL 결과 데이터를 바탕으로 적절한 IColumn 객체를 생성합니다.
    /// </summary>
    private static IColumn CreateColumnFromSqliteData(List<object?[]> rows, int columnIndex, Type columnType)
    {
        var rowCount = rows.Count;

        // 타입별 고속 변환 및 PrimitiveColumn 생성
        if (columnType == typeof(long) || columnType == typeof(int))
        {
            var data = new int[rowCount];
            var naMask = new bool[rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                var value = rows[i][columnIndex];
                if (value == null)
                {
                    naMask[i] = true;
                }
                else
                {
                    data[i] = Convert.ToInt32(value);
                }
            }

            return new PrimitiveColumn<int>(data, naMask);
        }
        else if (columnType == typeof(double) || columnType == typeof(float))
        {
            var data = new double[rowCount];
            var naMask = new bool[rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                var value = rows[i][columnIndex];
                if (value == null)
                {
                    naMask[i] = true;
                }
                else
                {
                    data[i] = Convert.ToDouble(value);
                }
            }

            return new PrimitiveColumn<double>(data, naMask);
        }
        else // 기본적으로 문자열 컬럼으로 처리
        {
            var data = new string?[rowCount];
            var naMask = new bool[rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                var value = rows[i][columnIndex];
                if (value == null)
                {
                    naMask[i] = true;
                }
                else
                {
                    data[i] = value.ToString();
                }
            }

            return new StringColumn(data, naMask);
        }
    }

    /// <summary>
    /// 데이터프레임 스키마를 바탕으로 SQLite 테이블 생성 SQL을 생성합니다.
    /// </summary>
    private static string GenerateCreateTableSql(DataFrame dataFrame, string tableName, bool ifExists)
    {
        var dropTable = ifExists ? $"DROP TABLE IF EXISTS {tableName};" : "";

        var columnDefinitions = new List<string>();

        foreach (var columnName in dataFrame.Columns)
        {
            var column = dataFrame[columnName];
            var sqlType = GetSqliteType(column);
            columnDefinitions.Add($"{columnName} {sqlType}");
        }

        var createTable = $"CREATE TABLE {tableName} ({string.Join(", ", columnDefinitions)});";

        return dropTable + createTable;
    }

    /// <summary>
    /// SQLite 데이터베이스 파일에 포함된 모든 테이블 이름 목록을 가져옵니다.
    /// </summary>
    public static List<string> GetTableNames(string dbPath)
    {
        var connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var query = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        using var command = new SqliteCommand(query, connection);
        using var reader = command.ExecuteReader();

        var tableNames = new List<string>();
        while (reader.Read())
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }

    /// <summary>
    /// 파일 경로와 테이블 이름을 지정하여 SQLite 테이블의 전체 내용을 DataFrame으로 읽어옵니다.
    /// </summary>
    public static DataFrame ReadSqliteTable(string dbPath, string tableName)
    {
        var connectionString = $"Data Source={dbPath}";
        var query = $"SELECT * FROM {tableName}";
        return ReadSqlite(connectionString, query);
    }

    /// <summary>
    /// .NET 데이터 타입을 SQLite의 데이터 타입으로 매핑합니다.
    /// </summary>
    private static string GetSqliteType(IColumn column)
    {
        return column.DataType.Name switch
        {
            "Int32" => "INTEGER",
            "Double" => "REAL",
            "String" => "TEXT",
            "DateTime" => "TEXT",
            "Boolean" => "INTEGER",
            _ => "TEXT"
        };
    }

    private static string GenerateInsertSql(DataFrame dataFrame, string tableName)
    {
        var columnNames = string.Join(", ", dataFrame.Columns);
        var parameterNames = string.Join(", ", Enumerable.Range(0, dataFrame.ColumnCount).Select(i => $"@p{i}"));

        return $"INSERT INTO {tableName} ({columnNames}) VALUES ({parameterNames});";
    }
}
