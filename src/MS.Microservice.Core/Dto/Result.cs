using Microsoft.AspNetCore.Http.HttpResults;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Dto
{
	//https://andrewlock.net/working-with-the-result-pattern-part-1-replacing-exceptions-as-control-flow/#making-the-result-pattern-safer
	public class Result<T>
	{
		private readonly T? _value;
		private readonly Exception? _error;
		private Result(T value)
		{
			IsSuccess = true;
			_value = value;
			_error = null;
		}

		private Result(Exception error)
		{
			IsSuccess = false;
			_value = default;
			_error = error;
		}
		[MemberNotNullWhen(true, nameof(_value))]
		[MemberNotNullWhen(false, nameof(_error))]
		public bool IsSuccess { get; private set; }
		// https://github.com/dotnet/csharplang/blob/main/proposals/TypeUnions.md
		public Result<TReturn> Switch<TReturn>(Func<T, TReturn> onSuccess, Func<Exception, Exception> onFailure)
		{
			if (IsSuccess)
			{
				return Result<TReturn>.Success(onSuccess(_value));
			}
			else
			{
				var err = onFailure(_error);
				return Result<TReturn>.Fail(err);
			}
		}

		public static Result<T> Success(T value) => new(value);
		public static Result<T> Fail(Exception error) => new(error);

		public static implicit operator Result<T>(T value) => Success(value);
	}
}
