using MS.Microservice.Core.Dto;
using System;

namespace MS.Microservice.Core.Extension
{
	internal static class ResultExtensions
	{
		public static Result<TResult> Select<TFrom, TResult>(
			this Result<TFrom> source,
			Func<TFrom, TResult> selector)
		{
			return source.Switch(
				onSuccess: r => selector(r),
				onFailure: e => Result<TResult>.Fail(e)); // 无法将Result<TResult> -> Exception
		}

		public static Result<TResult> SelectMany<TSource, TMiddle, TResult>(
			this Result<TSource> source,
			Func<TSource, Result<TMiddle>> collectionSelector,
			Func<TSource, TMiddle, TResult> resultSelector)
		{
			return source.Switch(
				onSuccess: r =>
				{
					Result<TMiddle> middleResult = collectionSelector(r);
					return middleResult.Select(v => resultSelector(r,v));
				},
				onFailure: e => Result<TResult>.Fail(e)); // 无法将Result<TResult> -> Exception
		}
	}
}
