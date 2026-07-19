using Microsoft.Data.Sqlite;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.Core.Index;

namespace TeruTeruPandas.IO;

/// <summary>
/// SQLite 데이터베이스와의 양방향 통신 모듈.
/// DataFrame의 데이터를 곧바로 DB 테이블에 쓰거나, 쿼리 결과를 DataFrame 배열 구조로 로드합니다.
/// </summary>
public static class SqliteIO
{
    public static DataFrame ReadSqlite(string connectionString, string query)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = new SqliteCommand(query, connection);
        using var reader = command.ExecuteReader();

        // 컬럼 정보 가져오기
        var columnNames = new string[reader.FieldCount];
        var columnTypes = new Type[reader.FieldCount];

        for (int i = 0; i < reader.FieldCount; i++)
        {
            columnNames[i] = reader.GetName(i);
            columnTypes[i] = reader.GetFieldType(i);
        }

        // 데이터 읽기
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

        // DataFrame 생성
        var columns = new Dictionary<string, IColumn>();
        for (int colIndex = 0; colIndex < columnNames.Length; colIndex++)
        {
            var columnName = columnNames[colIndex];
            var columnType = columnTypes[colIndex];

            columns[columnName] = CreateColumnFromSqliteData(rows, colIndex, columnType);
        }

        return new DataFrame(columns, new RangeIndex(rows.Count));
    }

    public static void ToSqlite(DataFrame dataFrame, string connectionString, string tableName, bool ifExists = false)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // 테이블 생성
        var createTableSql = GenerateCreateTableSql(dataFrame, tableName, ifExists);
        using (var command = new SqliteCommand(createTableSql, connection))
        {
            command.ExecuteNonQuery();
        }

        // 데이터 삽입
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

    private static IColumn CreateColumnFromSqliteData(List<object?[]> rows, int columnIndex, Type columnType)
    {
        var rowCount = rows.Count;

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
        else // string
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
    /// SQLite 데이터베이스의 모든 테이블 이름 가져오기
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
    /// SQLite 데이터베이스에서 특정 테이블 읽기 (파일 경로 + 테이블명)
    /// </summary>
    public static DataFrame ReadSqliteTable(string dbPath, string tableName)
    {
        var connectionString = $"Data Source={dbPath}";
        var query = $"SELECT * FROM {tableName}";
        return ReadSqlite(connectionString, query);
    }

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