using System;

namespace WebPowerShell.Domain.Common
{
    public class Result<T>
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public T? Value { get; }
        public AppFailure? Failure { get; }

        private Result(bool isSuccess, T? value, AppFailure? failure)
        {
            IsSuccess = isSuccess;
            Value = value;
            Failure = failure;
        }

        public static Result<T> Success(T value) => new(true, value, null);
        public static Result<T> Fail(AppFailure failure) => new(false, default, failure);

        public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<AppFailure, TResult> onFailure)
        {
            if (IsSuccess)
            {
                return onSuccess(Value!);
            }
            return onFailure(Failure!);
        }
    }
}
