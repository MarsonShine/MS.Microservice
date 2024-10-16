using MS.Microservice.Core.Dto;
using System;

namespace MS.Microservice.Core.Extension
{
	internal static class ResultExtensions
	{
		public static Result2<TResult> Select<TFrom,TResult>(
			this Result2<TFrom> source, 
			Func<TFrom, TResult> selector)
		{
			return source.Switch(
				onSuccess: r => selector(r),
				onFailure: e => Result2<TResult>.Fail(e)); // 无法将Result<TResult> -> Exception
		}

		public static Result2<TResult> SelectMany<TSource,TMiddle,TResult>(
			this Result2<TSource> source,
			Func<TSource,Result2<TMiddle>> collectionSelector,
			Func<TSource,TMiddle,TResult> resultSelector)
		{
			return source.Switch(
				onSuccess: r => { 
					Result2<TMiddle> result = collectionSelector(r);
					return result.Select(v => resultSelector(r, v));
				},
				onFailure: e => Result2<TResult>.Fail(e));
		}
	}
}
