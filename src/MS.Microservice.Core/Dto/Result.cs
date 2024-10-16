using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Dto
{
	public class Result<T>
	{
		private Result(T value)
		{
			IsSuccess = true;
			Value = value;
			Error = null;
		}

		private Result(Exception error)
		{
			IsSuccess = false;
			Value = default;
			Error = error;
		}
		[MemberNotNullWhen(true, nameof(Value))]
		[MemberNotNullWhen(false, nameof(Error))]
		public bool IsSuccess { get; private set; }

		public T? Value { get; private set; }

		public Exception? Error { get; private set; }

		public static Result<T> Success(T value) => new(value);
		public static Result<T> Fail(Exception error) => new(error);

		public static implicit operator Result<T>(T value) => Success(value);
	}

	//https://andrewlock.net/working-with-the-result-pattern-part-1-replacing-exceptions-as-control-flow/#making-the-result-pattern-safer
	public class Result2<T>
	{
		private readonly T? _value;
		private readonly Exception? _error;
		private Result2(T value)
		{
			IsSuccess = true;
			_value = value;
			_error = null;
		}

		private Result2(Exception error)
		{
			IsSuccess = false;
			_value = default;
			_error = error;
		}
		[MemberNotNullWhen(true, nameof(_value))]
		[MemberNotNullWhen(false, nameof(_error))]
		public bool IsSuccess { get; private set; }
		// https://github.com/dotnet/csharplang/blob/main/proposals/TypeUnions.md
		public Result2<TReturn> Switch<TReturn>(Func<T, TReturn> onSuccess, Func<Exception, Exception> onFailure)
		{
			if (IsSuccess)
			{
				return Result2<TReturn>.Success(onSuccess(_value));
			}
			else
			{
				return Result2<TReturn>.Fail(onFailure(_error));
			}
		}

		public static Result2<T> Success(T value) => new(value);
		public static Result2<T> Fail(Exception error) => new(error);

		public static implicit operator Result2<T>(T value) => Success(value);
	}
}
