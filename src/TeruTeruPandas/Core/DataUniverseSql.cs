using System.Text.RegularExpressions;
using TeruTeruPandas.Core.Column;

namespace TeruTeruPandas.Core;

/// <summary>
/// 간단한 SQL 파서
/// DataUniverse.SqlExecute() 메서드에서 사용
/// </summary>
internal class SimpleSqlParser
{
    private readonly string _sql;

    public SimpleSqlParser(string sql)
    {
        _sql = sql?.Trim() ?? throw new ArgumentNullException(nameof(sql));
    }

    public SqlQuery Parse()
    {
        var query = new SqlQuery();
        var sql = _sql;

        // SELECT 절 파싱
        var selectMatch = Regex.Match(sql, @"SELECT\s+(.*?)\s+FROM", RegexOptions.IgnoreCase);
        if (!selectMatch.Success)
            throw new ArgumentException("Invalid SQL: SELECT clause not found");

        query.SelectColumns = ParseSelectColumns(selectMatch.Groups[1].Value.Trim());

        // FROM 절 파싱
        var fromMatch = Regex.Match(sql, @"FROM\s+(\w+)", RegexOptions.IgnoreCase);
        if (!fromMatch.Success)
            throw new ArgumentException("Invalid SQL: FROM clause not found");

        query.FromTable = fromMatch.Groups[1].Value.Trim();

        // JOIN 절 파싱
        var joinMatches = Regex.Matches(sql, 
            @"(INNER|LEFT|RIGHT)?\s*JOIN\s+(\w+)\s+ON\s+(\w+)\.(\w+)\s*=\s*(\w+)\.(\w+)", 
            RegexOptions.IgnoreCase);
        
        foreach (Match joinMatch in joinMatches)
        {
            query.Joins.Add(new SqlJoin
            {
                JoinType = joinMatch.Groups[1].Value.ToUpperInvariant() switch
                {
                    "LEFT" => "left",
                    "RIGHT" => "right",
                    _ => "inner"
                },
                RightTable = joinMatch.Groups[2].Value.Trim(),
                LeftColumn = joinMatch.Groups[4].Value.Trim(),
                RightColumn = joinMatch.Groups[6].Value.Trim()
            });
        }

        // WHERE 절 파싱
        var whereMatch = Regex.Match(sql, @"WHERE\s+(.*?)(?=ORDER BY|GROUP BY|LIMIT|$)", 
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (whereMatch.Success)
        {
            query.WhereClause = whereMatch.Groups[1].Value.Trim();
        }

        // GROUP BY 절 파싱
        var groupByMatch = Regex.Match(sql, @"GROUP BY\s+(.*?)(?=ORDER BY|LIMIT|$)", 
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (groupByMatch.Success)
        {
            query.GroupByColumns = groupByMatch.Groups[1].Value
                .Split(',')
                .Select(s => s.Trim())
                .ToList();
        }

        // ORDER BY 절 파싱
        var orderByMatch = Regex.Match(sql, @"ORDER BY\s+(.*?)(?=LIMIT|$)", 
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (orderByMatch.Success)
        {
            var orderByParts = orderByMatch.Groups[1].Value.Trim().Split(',');
            foreach (var part in orderByParts)
            {
                var parts = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var column = parts[0];
                var direction = parts.Length > 1 && parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase) 
                    ? "DESC" 
                    : "ASC";
                query.OrderByColumns.Add((column, direction));
            }
        }

        // LIMIT 절 파싱
        var limitMatch = Regex.Match(sql, @"LIMIT\s+(\d+)", RegexOptions.IgnoreCase);
        if (limitMatch.Success)
        {
            query.Limit = int.Parse(limitMatch.Groups[1].Value);
        }

        return query;
    }

    private List<string> ParseSelectColumns(string selectPart)
    {
        if (selectPart == "*")
            return new List<string> { "*" };

        return selectPart.Split(',')
            .Select(s => s.Trim())
            .ToList();
    }
}

/// <summary>
/// SQL 쿼리 표현
/// </summary>
internal class SqlQuery
{
    public List<string> SelectColumns { get; set; } = new();
    public string FromTable { get; set; } = string.Empty;
    public List<SqlJoin> Joins { get; set; } = new();
    public string? WhereClause { get; set; }
    public List<string> GroupByColumns { get; set; } = new();
    public List<(string column, string direction)> OrderByColumns { get; set; } = new();
    public int? Limit { get; set; }
}

/// <summary>
/// SQL JOIN 정보
/// </summary>
internal class SqlJoin
{
    public string JoinType { get; set; } = "inner";
    public string RightTable { get; set; } = string.Empty;
    public string LeftColumn { get; set; } = string.Empty;
    public string RightColumn { get; set; } = string.Empty;
}

/// <summary>
/// SQL 쿼리 실행 엔진
/// </summary>
internal class SqlQueryExecutor
{
    private readonly DataUniverse _universe;

    public SqlQueryExecutor(DataUniverse universe)
    {
        _universe = universe ?? throw new ArgumentNullException(nameof(universe));
    }

    public DataFrame Execute(SqlQuery query)
    {
        // 1. FROM 절 - 기본 테이블 가져오기
        var result = _universe.GetTableOrThrow(query.FromTable);

        // 2. JOIN 절 실행
        foreach (var join in query.Joins)
        {
            var rightTable = _universe.GetTableOrThrow(join.RightTable);
            result = result.Merge(rightTable, on: join.LeftColumn, how: join.JoinType);
        }

        // 3. WHERE 절 필터링
        if (!string.IsNullOrWhiteSpace(query.WhereClause))
        {
            result = ApplyWhereClause(result, query.WhereClause);
        }

        // 4. GROUP BY 절
        if (query.GroupByColumns.Count > 0)
        {
            result = ApplyGroupBy(result, query.GroupByColumns, query.SelectColumns);
        }

        // 5. SELECT 절 - 컬럼 선택
        if (!query.SelectColumns.Contains("*"))
        {
            result = SelectColumns(result, query.SelectColumns);
        }

        // 6. ORDER BY 절
        if (query.OrderByColumns.Count > 0)
        {
            result = ApplyOrderBy(result, query.OrderByColumns);
        }

        // 7. LIMIT 절
        if (query.Limit.HasValue)
        {
            result = result.Head(query.Limit.Value);
        }

        return result;
    }

    private DataFrame ApplyWhereClause(DataFrame df, string whereClause)
    {
        // 간단한 WHERE 조건 파싱 (예: column = value, column > value 등)
        
        // 비교 연산자 패턴
        var comparisonPattern = @"(\w+)\s*(=|!=|>|<|>=|<=)\s*(.+)";
        var match = Regex.Match(whereClause, comparisonPattern);

        if (!match.Success)
            throw new ArgumentException($"Unsupported WHERE clause: {whereClause}");

        var column = match.Groups[1].Value.Trim();
        var op = match.Groups[2].Value.Trim();
        var valueStr = match.Groups[3].Value.Trim().Trim('\'', '"');

        if (!df.Columns.Contains(column))
            throw new ArgumentException($"Column '{column}' not found");

        var series = df[column];
        BoolSeries? mask = null;

        // 값 파싱 (숫자 또는 문자열)
        if (double.TryParse(valueStr, out var numValue))
        {
            // 숫자 비교
            mask = op switch
            {
                "=" => CreateNumericMask(series, v => Math.Abs(v - numValue) < 0.0001),
                "!=" => CreateNumericMask(series, v => Math.Abs(v - numValue) >= 0.0001),
                ">" => CreateNumericMask(series, v => v > numValue),
                "<" => CreateNumericMask(series, v => v < numValue),
                ">=" => CreateNumericMask(series, v => v >= numValue),
                "<=" => CreateNumericMask(series, v => v <= numValue),
                _ => throw new ArgumentException($"Unsupported operator: {op}")
            };
        }
        else
        {
            // 문자열 비교
            mask = op switch
            {
                "=" => CreateStringMask(series, s => s == valueStr),
                "!=" => CreateStringMask(series, s => s != valueStr),
                _ => throw new ArgumentException($"Operator '{op}' not supported for string comparison")
            };
        }

        return df[mask];
    }

    private BoolSeries CreateNumericMask(IColumn column, Func<double, bool> predicate)
    {
        var values = new bool[column.Length];
        for (int i = 0; i < column.Length; i++)
        {
            if (column.IsNA(i))
            {
                values[i] = false;
                continue;
            }

            var value = column.GetValue(i);
            var numValue = Convert.ToDouble(value);
            values[i] = predicate(numValue);
        }
        return new BoolSeries(values);
    }

    private BoolSeries CreateStringMask(IColumn column, Func<string, bool> predicate)
    {
        var values = new bool[column.Length];
        for (int i = 0; i < column.Length; i++)
        {
            if (column.IsNA(i))
            {
                values[i] = false;
                continue;
            }

            var value = column.GetValue(i)?.ToString() ?? "";
            values[i] = predicate(value);
        }
        return new BoolSeries(values);
    }

    private DataFrame SelectColumns(DataFrame df, List<string> columns)
    {
        var selectedColumns = new Dictionary<string, IColumn>();

        foreach (var col in columns)
        {
            // 집계 함수 처리 (예: COUNT(*), SUM(column), AVG(column))
            var aggMatch = Regex.Match(col, @"(COUNT|SUM|AVG|MIN|MAX)\s*\(\s*(\*|\w+)\s*\)", RegexOptions.IgnoreCase);
            
            if (aggMatch.Success)
            {
                // 집계 함수는 GROUP BY와 함께 사용되어야 함
                throw new ArgumentException("Aggregate functions require GROUP BY clause");
            }

            if (df.Columns.Contains(col))
            {
                selectedColumns[col] = df[col];
            }
            else
            {
                throw new ArgumentException($"Column '{col}' not found");
            }
        }

        return new DataFrame(selectedColumns);
    }

    private DataFrame ApplyGroupBy(DataFrame df, List<string> groupByColumns, List<string> selectColumns)
    {
        // 간단한 GROUP BY 구현 - 첫 번째 그룹 컬럼만 지원
        var grouped = df.GroupBy(groupByColumns[0]);
        
        // SELECT 절에서 집계 함수 찾기
        var aggregations = new Dictionary<string, string[]>();
        foreach (var col in selectColumns)
        {
            var aggMatch = Regex.Match(col, @"(COUNT|SUM|AVG|MIN|MAX)\s*\(\s*(\w+)\s*\)\s*(?:AS\s+(\w+))?", RegexOptions.IgnoreCase);
            if (aggMatch.Success)
            {
                var aggFunc = aggMatch.Groups[1].Value.ToLowerInvariant();
                var aggCol = aggMatch.Groups[2].Value;
                
                if (!aggregations.ContainsKey(aggCol))
                {
                    aggregations[aggCol] = new[] { aggFunc };
                }
            }
        }

        // 집계 수행
        if (aggregations.Count > 0)
        {
            return grouped.Agg(aggregations);
        }

        return grouped.Agg(new Dictionary<string, string[]>
        {
            { groupByColumns[0], new[] { "count" } }
        });
    }

    private DataFrame ApplyOrderBy(DataFrame df, List<(string column, string direction)> orderByColumns)
    {
        // TODO: DataFrame에 정렬 기능 구현 필요
        // 현재는 정렬 없이 반환
        return df;
    }
}
