using Microsoft.System.Collection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Core.Extension
{
	public static partial class IEnumerableExtensions
	{
		extension<T>(IEnumerable<T> source)
		{
			public int FindIndex(Func<T, bool> predicate)
			{
				if (source == null) throw new ArgumentNullException(nameof(source));
				if (predicate == null) throw new ArgumentNullException(nameof(predicate));
				var index = 0;
				foreach (var item in source)
				{
					if (predicate(item)) return index;
					index++;
				}
				return -1;
			}

			public void ForEach(Action<T> doAction)
			{
				if (source == null) throw new ArgumentNullException(nameof(source));
				if (doAction == null) throw new ArgumentNullException(nameof(doAction));
				foreach (T item in source)
				{
					doAction(item);
				}
			}

			public void ForEach(Action<T, int> doAction)
			{
				if (source == null) throw new ArgumentNullException(nameof(source));
				if (doAction == null) throw new ArgumentNullException(nameof(doAction));
				ForEachInterator(source, doAction);
			}

			public IEnumerable<T> Shuffle() => source.ToArray().Shuffle();

			public IEnumerable<T> Flatten(Func<T, IEnumerable<T>?> childrenSelector)
			{
				ArgumentNullException.ThrowIfNull(source);
				ArgumentNullException.ThrowIfNull(childrenSelector);

				return FlattenInternal(source, childrenSelector);
			}
		}

		extension<T>(IEnumerable<T>? source)
		{
			public string JoinAsString(string separator)
			{
				return source == null ? throw new ArgumentNullException(nameof(source)) : string.Join(separator, source);
			}
		}

		extension<TSource, TResult>(IEnumerable<TSource> source)
		{
			public TResult[] ToArray(Func<TSource, TResult> conveter)
			{
				if (source == null) throw new ArgumentNullException(nameof(source));
				if (conveter == null) throw new ArgumentNullException(nameof(conveter));

				return ConvertInterator(source, conveter);
			}
		}

		private static void ForEachInterator<TSource>(IEnumerable<TSource> source, Action<TSource, int> dpActoin)
		{
			var index = -1;
			foreach (TSource item in source)
			{
				index = checked(index + 1);
				dpActoin(item, index);
			}
		}

		private static TResult[] ConvertInterator<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> conveter)
		{
			var sourceArray = source.ToArray();
			TResult[] results = new TResult[sourceArray.Length];

			ForEachInterator(source, (item, index) =>
			{
				results[index] = conveter(item);
			});

			return results;
		}

		private static IEnumerable<T> FlattenInternal<T>(IEnumerable<T> source, Func<T, IEnumerable<T>?> childrenSelector)
		{
			var disposables = new Stack<IEnumerator<T>>(); // 可以用 DisposableStack<T> 简化代码
			var current = source.GetEnumerator();

			try
			{
				while (true)
				{
					if (current.MoveNext())
					{
						var item = current.Current;
						yield return item;

						var children = childrenSelector(item);
						if (children != null)
						{
							disposables.Push(current);
							current = children.GetEnumerator();
						}
					}
					else if (disposables.TryPop(out var parent))
					{
						current.Dispose();
						current = parent;
					}
					else
					{
						break;
					}
				}
			}
			finally
			{
				current.Dispose();
				while (disposables.TryPop(out var enumerator))
				{
					enumerator.Dispose();
				}
			}
		}
	}
}
