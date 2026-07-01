using System;
using System.Linq;
using TeruTeruPandas.Core.Column;

namespace TeruTeruPandas.Core;

public static class DataFrameVectorExtensions
{
    /// <summary>
    /// 지정된 컬럼의 벡터 데이터와 대상 벡터 사이의 코사인 유사도를 계산하여 내림차순으로 정렬된 새 DataFrame을 반환합니다.
    /// </summary>
    /// <param name="df">원본 DataFrame</param>
    /// <param name="columnName">벡터 데이터가 포함된 컬럼 이름</param>
    /// <param name="targetVector">비교 대상 임베딩 벡터</param>
    public static DataFrame OrderByDescendingCosineSimilarity(this DataFrame df, string columnName, float[] targetVector)
    {
        if (!df.Columns.Contains(columnName))
            throw new ArgumentException($"Column '{columnName}' not found.");

        var column = df[columnName];
        if (column is not VectorColumn vectorColumn)
            throw new ArgumentException($"Column '{columnName}' is not a VectorColumn.");

        // Calculate similarities
        double[] similarities = vectorColumn.CalculateSimilarities(targetVector);

        // Argsort based on similarities (descending)
        int[] indices = Enumerable.Range(0, df.RowCount).ToArray();
        Array.Sort(indices, (a, b) => similarities[b].CompareTo(similarities[a]));

        // Reorder all columns
        var newColumns = df.Columns.ToDictionary(
            c => c,
            c => df[c].Reorder(indices)
        );

        // Add Similarity column for reference
        var simColumn = new PrimitiveColumn<double>(indices.Select(idx => similarities[idx]).ToArray());
        newColumns["Similarity"] = simColumn;

        return new DataFrame(newColumns);
    }
}
