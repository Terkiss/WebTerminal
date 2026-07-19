using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace TeruTeruPandas.Core.SIMD;

/// <summary>
/// SIMD(Single Instruction, Multiple Data) 하드웨어 가속 연산을 담당하는 클래스.
/// .NET Generic Math(`INumber<T>`)를 활용하여 타입에 구애받지 않고 컬럼 간 덧셈, 뺄셈 등
/// 배열 연산을 Vector<T> 단위로 일괄 처리하여 압도적인 성능 향상을 이끌어냅니다.
/// </summary>
public static class SimdOperations
{
    /// <summary>
    /// SIMD Array Addition (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddArrays<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right, Span<T> result) where T : struct, INumber<T>
    {
        if (left.Length != right.Length || left.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = left.Length - (left.Length % vectorSize);

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var leftVec = new Vector<T>(left.Slice(i, vectorSize));
            var rightVec = new Vector<T>(right.Slice(i, vectorSize));
            var resultVec = leftVec + rightVec;
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        for (int i = vectorizedLength; i < left.Length; i++)
        {
            result[i] = left[i] + right[i];
        }
    }

    /// <summary>
    /// SIMD Array Subtraction (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubtractArrays<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right, Span<T> result) where T : struct, INumber<T>
    {
        if (left.Length != right.Length || left.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = left.Length - (left.Length % vectorSize);

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var leftVec = new Vector<T>(left.Slice(i, vectorSize));
            var rightVec = new Vector<T>(right.Slice(i, vectorSize));
            var resultVec = leftVec - rightVec;
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        for (int i = vectorizedLength; i < left.Length; i++)
        {
            result[i] = left[i] - right[i];
        }
    }

    /// <summary>
    /// SIMD Array Multiplication (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MultiplyArrays<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right, Span<T> result) where T : struct, INumber<T>
    {
        if (left.Length != right.Length || left.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = left.Length - (left.Length % vectorSize);

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var leftVec = new Vector<T>(left.Slice(i, vectorSize));
            var rightVec = new Vector<T>(right.Slice(i, vectorSize));
            var resultVec = leftVec * rightVec;
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        for (int i = vectorizedLength; i < left.Length; i++)
        {
            result[i] = left[i] * right[i];
        }
    }

    /// <summary>
    /// SIMD Array Division (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DivideArrays<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right, Span<T> result) where T : struct, INumber<T>
    {
        if (left.Length != right.Length || left.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = left.Length - (left.Length % vectorSize);

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var leftVec = new Vector<T>(left.Slice(i, vectorSize));
            var rightVec = new Vector<T>(right.Slice(i, vectorSize));
            var resultVec = leftVec / rightVec;
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        for (int i = vectorizedLength; i < left.Length; i++)
        {
            result[i] = left[i] / right[i];
        }
    }

    /// <summary>
    /// SIMD Array Modulus (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ModArrays<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right, Span<T> result) where T : struct, INumber<T>
    {
        if (left.Length != right.Length || left.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        // Loop fallback for modulus as Vector support is not guaranteed for all types/architectures
        for (int i = 0; i < left.Length; i++)
        {
            result[i] = left[i] % right[i];
        }
    }

    /// <summary>
    /// SIMD Scalar Addition (Broadcasting, Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddScalar<T>(ReadOnlySpan<T> array, T scalar, Span<T> result) where T : struct, INumber<T>
    {
        if (array.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = array.Length - (array.Length % vectorSize);
        var scalarVec = new Vector<T>(scalar);

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var arrayVec = new Vector<T>(array.Slice(i, vectorSize));
            var resultVec = arrayVec + scalarVec;
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        for (int i = vectorizedLength; i < array.Length; i++)
        {
            result[i] = array[i] + scalar;
        }
    }

    /// <summary>
    /// SIMD Scalar Subtraction (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubtractScalar<T>(ReadOnlySpan<T> array, T scalar, Span<T> result, bool scalarIsRight = true) where T : struct, INumber<T>
    {
        if (array.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = array.Length - (array.Length % vectorSize);
        var scalarVec = new Vector<T>(scalar);

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var arrayVec = new Vector<T>(array.Slice(i, vectorSize));
            var resultVec = scalarIsRight ? (arrayVec - scalarVec) : (scalarVec - arrayVec);
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        for (int i = vectorizedLength; i < array.Length; i++)
        {
            result[i] = scalarIsRight ? (array[i] - scalar) : (scalar - array[i]);
        }
    }

    /// <summary>
    /// SIMD Scalar Multiplication (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MultiplyScalar<T>(ReadOnlySpan<T> array, T scalar, Span<T> result) where T : struct, INumber<T>
    {
        if (array.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = array.Length - (array.Length % vectorSize);
        var scalarVec = new Vector<T>(scalar);

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var arrayVec = new Vector<T>(array.Slice(i, vectorSize));
            var resultVec = arrayVec * scalarVec;
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        for (int i = vectorizedLength; i < array.Length; i++)
        {
            result[i] = array[i] * scalar;
        }
    }

    /// <summary>
    /// SIMD Scalar Division (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DivideScalar<T>(ReadOnlySpan<T> array, T scalar, Span<T> result, bool scalarIsRight = true) where T : struct, INumber<T>
    {
        if (array.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = array.Length - (array.Length % vectorSize);
        var scalarVec = new Vector<T>(scalar);

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var arrayVec = new Vector<T>(array.Slice(i, vectorSize));
            var resultVec = scalarIsRight ? (arrayVec / scalarVec) : (scalarVec / arrayVec);
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        for (int i = vectorizedLength; i < array.Length; i++)
        {
            result[i] = scalarIsRight ? (array[i] / scalar) : (scalar / array[i]);
        }
    }

    /// <summary>
    /// SIMD Sum (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Sum<T>(ReadOnlySpan<T> array) where T : struct, INumber<T>
    {
        if (array.IsEmpty) return T.Zero;

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = array.Length - (array.Length % vectorSize);
        var sumVec = Vector<T>.Zero;

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var vec = new Vector<T>(array.Slice(i, vectorSize));
            sumVec += vec;
        }

        T totalSum = T.Zero;
        for (int i = 0; i < vectorSize; i++)
        {
            totalSum += sumVec[i];
        }

        for (int i = vectorizedLength; i < array.Length; i++)
        {
            totalSum += array[i];
        }

        return totalSum;
    }

    /// <summary>
    /// SIMD Mean (Generic)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Mean<T>(ReadOnlySpan<T> array) where T : struct, INumber<T>
    {
        if (array.IsEmpty) return 0.0;
        var sum = Sum(array);
        // Using CreateChecked to support various numeric types efficiently
        return double.CreateChecked(sum) / array.Length;
    }

    // --- Legacy / Wrapper Support ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddArrays(Span<float> left, ReadOnlySpan<float> right, Span<float> result) => AddArrays<float>(left, right, result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MultiplyArrays(Span<float> left, ReadOnlySpan<float> right, Span<float> result) => MultiplyArrays<float>(left, right, result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddScalar(Span<float> array, float scalar, Span<float> result) => AddScalar<float>(array, scalar, result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sum(ReadOnlySpan<float> array) => Sum<float>(array);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Mean(ReadOnlySpan<float> array) => (float)Mean<float>(array);

    // Helpers for specific types that might be used by existing code
    public static int SumInt(int[] array) => Sum<int>(array);
    public static double SumDouble(double[] array) => Sum<double>(array);
    public static float SumAvx2(ReadOnlySpan<float> array) => Sum<float>(array);

}
