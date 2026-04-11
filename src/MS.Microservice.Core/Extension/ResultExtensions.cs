using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using System;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Extension
{
	public static partial class ResultExtensions
	{
		public static Result<T> Try<T>(Func<T> operation)
		{
			try
			{
				return Result<T>.Success(operation());
			}
			catch (Exception ex)
			{
				return Result<T>.Fail(ex);
			}
		}

		public static Result<Unit> Try(Action operation)
			=> Try(() =>
			{
				operation();
				return Unit.Default;
			});

		public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> operation)
		{
			try
			{
				return Result<T>.Success(await operation());
			}
			catch (Exception ex)
			{
				return Result<T>.Fail(ex);
			}
		}

		public static Task<Result<Unit>> TryAsync(Func<Task> operation)
			=> TryAsync(async () =>
			{
				await operation();
				return Unit.Default;
			});

		extension<T>(Result<T> result)
		{
			public Result<T> Ensure(Func<T, bool> predicate, Func<Exception> errorFactory)
				=> result.Bind(value => predicate(value)
					? Result<T>.Success(value)
					: Result<T>.Fail(errorFactory()));

			public Result<R> MapTry<R>(Func<T, R> mapper)
				=> result.Bind(value => Try(() => mapper(value)));

			public Task<Result<R>> MapAsync<R>(Func<T, Task<R>> mapper)
				=> result.IsSuccess
					? TryAsync(async () => await mapper(result.Value))
					: Task.FromResult(Result<R>.Fail(result.Error));

			public Task<Result<R>> BindAsync<R>(Func<T, Task<Result<R>>> binder)
				=> result.IsSuccess
					? binder(result.Value)
					: Task.FromResult(Result<R>.Fail(result.Error));

			public async Task<Result<T>> TapAsync(Func<T, Task> effect)
			{
				if (result.IsFailure)
				{
					return result;
				}

				var effectResult = await TryAsync(() => effect(result.Value));
				return effectResult.Match(
					onSuccess: _ => result,
					onFailure: Result<T>.Fail);
			}

			public async Task<R> MatchAsync<R>(Func<T, Task<R>> onSuccess, Func<Exception, Task<R>> onFailure)
				=> result.IsSuccess
					? await onSuccess(result.Value)
					: await onFailure(result.Error);
		}
	}
}
