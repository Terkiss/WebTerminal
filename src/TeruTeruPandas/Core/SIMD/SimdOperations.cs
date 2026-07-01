using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace TeruTeruPandas.Core.SIMD;

/// <summary>
/// SIMD(Single Instruction, Multiple Data) 하드웨어 가속 연산을 담당하는 클래스.
/// .NET Generic Math(`INumber<T>`)를 활용하여 타입에 구애받지 않고 컬럼 간 덧셈, 뺄셈 등
/// 배열 연산을 Vector&lt;T&gt; 단위로 일괄 처리하여 압도적인 성능 향상을 이끌어냅니다.
/// CPU의 레지스터 수준에서 병렬 처리를 수행하므로 대용량 데이터 셋에서 루프보다 수배~수십배 빠릅니다.
/// </summary>
public static class SimdOperations
{
    /// <summary>
    /// 두 배열의 요소별 덧셈을 SIMD로 수행합니다.
    /// CPU의 넓은 레지스터(256/512비트)를 사용하여 한 번의 연산으로 여러 요소를 동시에 더합니다.
    /// </summary>
    /// <param name="left">왼쪽 피연산자 배열</param>
    /// <param name="right">오른쪽 피연산자 배열</param>
    /// <param name="result">결과를 저장할 배열</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddArrays<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right, Span<T> result) where T : struct, INumber<T>
    {
        if (left.Length != right.Length || left.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        // 현재 하드웨어에서 한 벡터에 담을 수 있는 요소의 개수 (예: float는 256비트 AVX에서 8개)
        int vectorSize = Vector<T>.Count;
        // 벡터화 가능한 최대 길이 계산
        int vectorizedLength = left.Length - (left.Length % vectorSize);

        // 1. 벡터화된 루프: 여러 데이터를 하나의 레지스터에 로드하여 일괄 연산
        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var leftVec = new Vector<T>(left.Slice(i, vectorSize));
            var rightVec = new Vector<T>(right.Slice(i, vectorSize));
            var resultVec = leftVec + rightVec; // CPU 수준의 병렬 덧셈
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        // 2. 나머지(Remainder) 루프: 벡터 크기에 맞지 않는 남은 요소들을 순차 처리
        for (int i = vectorizedLength; i < left.Length; i++)
        {
            result[i] = left[i] + right[i];
        }
    }

    /// <summary>
    /// 두 배열의 요소별 뺄셈을 SIMD로 수행합니다.
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
    /// 두 배열의 요소별 곱셈을 SIMD로 수행합니다.
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
    /// 두 배열의 요소별 나눗셈을 SIMD로 수행합니다.
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
    /// 두 배열의 요소별 나머지(Modulus) 연산을 수행합니다.
    /// 나머지 연산은 하드웨어 벡터 가속 지원이 불완전할 수 있어 순차 처리합니다.
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
    /// 배열의 모든 요소에 스칼라 값을 더합니다 (Broadcasting).
    /// 스칼라 값을 벡터로 복제하여 배열의 여러 요소와 동시에 더합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddScalar<T>(ReadOnlySpan<T> array, T scalar, Span<T> result) where T : struct, INumber<T>
    {
        if (array.Length != result.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = array.Length - (array.Length % vectorSize);
        // 스칼라 값을 벡터의 모든 레인에 복제
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
    /// 배열의 모든 요소에서 스칼라 값을 빼거나, 스칼라 값에서 배열 요소를 뺍니다.
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
            // 뺄셈의 순서에 따라 처리 (Broadcasting)
            var resultVec = scalarIsRight ? (arrayVec - scalarVec) : (scalarVec - arrayVec);
            resultVec.CopyTo(result.Slice(i, vectorSize));
        }

        for (int i = vectorizedLength; i < array.Length; i++)
        {
            result[i] = scalarIsRight ? (array[i] - scalar) : (scalar - array[i]);
        }
    }

    /// <summary>
    /// 배열의 모든 요소에 스칼라 값을 곱합니다.
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
    /// 배열의 모든 요소를 스칼라 값으로 나누거나, 스칼라 값을 배열 요소로 나눕니다.
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
    /// 배열의 모든 요소의 합계를 SIMD로 구합니다.
    /// 부분 합계를 벡터로 계산한 뒤, 마지막에 벡터의 요소들을 더하여 최종 합계를 구합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Sum<T>(ReadOnlySpan<T> array) where T : struct, INumber<T>
    {
        if (array.IsEmpty) return T.Zero;

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = array.Length - (array.Length % vectorSize);
        var sumVec = Vector<T>.Zero;

        // 1. 중간 합계를 벡터 레지스터에 누적
        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var vec = new Vector<T>(array.Slice(i, vectorSize));
            sumVec += vec;
        }

        // 2. 벡터 레지스터 내의 요소들을 스칼라로 추출하여 합산 (Horizontal Sum)
        T totalSum = T.Zero;
        for (int i = 0; i < vectorSize; i++)
        {
            totalSum += sumVec[i];
        }

        // 3. 나머지 요소들 합산
        for (int i = vectorizedLength; i < array.Length; i++)
        {
            totalSum += array[i];
        }

        return totalSum;
    }

    /// <summary>
    /// 배열 요소들의 평균을 구합니다. SIMD 가속된 Sum을 내부적으로 사용합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Mean<T>(ReadOnlySpan<T> array) where T : struct, INumber<T>
    {
        if (array.IsEmpty) return 0.0;
        var sum = Sum(array);
        // INumber<T> 타입을 double로 안전하게 변환하여 계산
        return double.CreateChecked(sum) / array.Length;
    }

    /// <summary>
    /// 두 배열의 내적(Dot Product)을 SIMD로 구합니다.
    /// 각 요소의 곱을 벡터로 구하고 이를 누적하여 합산합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T DotProduct<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right) where T : struct, INumber<T>
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Array lengths must match");

        int vectorSize = Vector<T>.Count;
        int vectorizedLength = left.Length - (left.Length % vectorSize);
        var sumVec = Vector<T>.Zero;

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            var leftVec = new Vector<T>(left.Slice(i, vectorSize));
            var rightVec = new Vector<T>(right.Slice(i, vectorSize));
            // 요소별 곱셈 결과를 벡터에 누적
            sumVec += leftVec * rightVec;
        }

        T totalSum = T.Zero;
        for (int i = 0; i < vectorSize; i++)
        {
            totalSum += sumVec[i];
        }

        for (int i = vectorizedLength; i < left.Length; i++)
        {
            totalSum += left[i] * right[i];
        }

        return totalSum;
    }

    /// <summary>
    /// 두 벡터(float) 간의 코사인 유사도를 계산합니다.
    /// SIMD 가속 내적 연산을 활용하여 고속으로 계산합니다.
    /// </summary>
    public static double CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length || left.IsEmpty || right.IsEmpty) return 0.0;

        float dot = DotProduct(left, right);
        float magLeft = MathF.Sqrt(DotProduct(left, left));
        float magRight = MathF.Sqrt(DotProduct(right, right));

        if (magLeft == 0 || magRight == 0) return 0;
        return (double)(dot / (magLeft * magRight));
    }

    // --- 레거시 지원 및 편의 기능 ---

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

    public static int SumInt(int[] array) => Sum<int>(array);
    public static double SumDouble(double[] array) => Sum<double>(array);
    public static float SumAvx2(ReadOnlySpan<float> array) => Sum<float>(array);

}
