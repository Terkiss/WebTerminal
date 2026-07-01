using System;
using System.Collections.Generic;
using System.Linq;
using TeruTeruPandas.Core.SIMD;

namespace TeruTeruPandas.Core.Column;

/// <summary>
/// Vector 데이터(float[])를 위한 컬럼 구현.
/// 임베딩 벡터 저장 및 코사인 유사도 검색을 지원합니다.
/// </summary>
public class VectorColumn : IColumn
{
    private float[][] _data;
    private bool[] _naMask;
    public int Length { get; private set; }
    public Type DataType => typeof(float[]);

    public VectorColumn(int length)
    {
        Length = length;
        _data = new float[length][];
        _naMask = new bool[length];
    }

    public VectorColumn(float[][] data, bool[]? naMask = null)
    {
        _data = data;
        Length = data.Length;
        _naMask = naMask ?? new bool[Length];
    }

    public object? GetValue(int index)
    {
        if (index < 0 || index >= Length) throw new IndexOutOfRangeException();
        return _naMask[index] ? null : _data[index];
    }

    public void SetValue(int index, object? value)
    {
        if (index < 0 || index >= Length) throw new IndexOutOfRangeException();
        if (value == null)
        {
            _naMask[index] = true;
            _data[index] = null!;
        }
        else
        {
            _data[index] = (float[])value;
            _naMask[index] = false;
        }
    }

    public bool IsNA(int index) => _naMask[index];
    public void SetNA(int index) => _naMask[index] = true;

    public IColumn Clone()
    {
        var newData = new float[Length][];
        for (int i = 0; i < Length; i++)
        {
            if (!_naMask[i]) newData[i] = (float[])_data[i].Clone();
        }
        return new VectorColumn(newData, (bool[])_naMask.Clone());
    }

    public IColumn Slice(int start, int length)
    {
        var newData = new float[length][];
        var newMask = new bool[length];
        for (int i = 0; i < length; i++)
        {
            newMask[i] = _naMask[start + i];
            if (!newMask[i]) newData[i] = (float[])_data[start + i].Clone();
        }
        return new VectorColumn(newData, newMask);
    }

    public int[] Argsort(bool ascending = true) => throw new NotSupportedException("VectorColumn does not support direct sorting.");
    public IColumn Reorder(int[] indices)
    {
        var newData = new float[indices.Length][];
        var newMask = new bool[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            newMask[i] = _naMask[indices[i]];
            if (!newMask[i]) newData[i] = _data[indices[i]];
        }
        return new VectorColumn(newData, newMask);
    }

    public IColumn FillNA(object? value) => throw new NotSupportedException();
    public IColumn FillNA(string method) => throw new NotSupportedException();

    public IColumn Add(IColumn other) => throw new NotSupportedException();
    public IColumn Add(object scalar) => throw new NotSupportedException();
    public IColumn Sub(IColumn other) => throw new NotSupportedException();
    public IColumn Sub(object scalar) => throw new NotSupportedException();
    public IColumn Mul(IColumn other) => throw new NotSupportedException();
    public IColumn Mul(object scalar) => throw new NotSupportedException();
    public IColumn Div(IColumn other) => throw new NotSupportedException();
    public IColumn Div(object scalar) => throw new NotSupportedException();
    public IColumn Mod(IColumn other) => throw new NotSupportedException();
    public IColumn Mod(object scalar) => throw new NotSupportedException();
    public IColumn Pow(IColumn other) => throw new NotSupportedException();
    public IColumn Pow(object scalar) => throw new NotSupportedException();

    public double Sum() => 0;
    public double Mean() => 0;
    public object? Max() => null;
    public object? Min() => null;
    public double Median() => 0;
    public double Var() => 0;
    public double Std() => 0;
    public double Quantile(double q) => 0;
    public IColumn Shift(int periods) => throw new NotSupportedException();

    // --- Vector Specific ---
    public double[] CalculateSimilarities(float[] target)
    {
        double[] results = new double[Length];
        for (int i = 0; i < Length; i++)
        {
            if (_naMask[i] || _data[i] == null) results[i] = -1.0;
            else results[i] = SimdOperations.CosineSimilarity(_data[i], target);
        }
        return results;
    }
}
