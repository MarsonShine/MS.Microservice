using System;
using System.IO;

namespace MS.Microservice.Core.Tests
{
	public readonly record struct Queue<T>(int FrontCount, Stream<T> Front, int RearCount, Stream<T> Rear)
	{
		public static Queue<T> Empty { get; } = new(0, Stream<T>.Empty, 0, Stream<T>.Empty);

		public bool IsEmpty => FrontCount == 0;

		public Queue<T> Snoc(T item) => (this with { RearCount = RearCount + 1, Rear = new(item, Rear) }).Check();

		public T Head => this switch
		{
			(0, _, _, _) => throw new InvalidOperationException("Queue is empty"),
			(_, { Value: var (item, _) }, _, _) => item,
			_ => default!,
		};

		public Queue<T> Tail() => this switch
		{
			(0, _, _, _) => throw new InvalidOperationException("Queue is empty"),
			(_, { Value: var (_, rest) }, _, _) => (this with { FrontCount = FrontCount - 1, Front = rest }).Check(),
			_ => default!,
		};


		private Queue<T> Check() => RearCount <= FrontCount ?
				this :
				new(FrontCount + RearCount, Front.Append(Rear.Reverse()), 0, Stream<T>.Empty);
	}

	public sealed record class StreamCell<T>(T Item, Stream<T> Rest);
	public sealed record class Stream<T>
	{
		public Stream(Func<StreamCell<T>?> factory) => _lazy = new(factory);
		public Stream(StreamCell<T>? value) => _lazy = new(value);
		public Stream(T item, Stream<T> rest) => _lazy = new(new StreamCell<T>(item, rest));
		public static Stream<T> Empty { get; } = new((StreamCell<T>?)null);
		public static Stream<T> Unit(T item) => new(item, Empty);
		private static StreamCell<T> Cons(T item, StreamCell<T>? rest) => new(item, rest == null ? Empty : new(rest));
		public StreamCell<T>? Value => _lazy.Value;


		public Stream<T> Append(Stream<T> other) => new(() => this switch
		{
			{ Value: null } => other.Value,
			{ Value: var (item, rest) } => new(item, rest.Append(other)),
		});
		public Stream<T> Take(int count) => new(() => (count, this) switch
		{
			(0, _) => null,
			(_, { Value: null }) => null,
			(_, { Value: var (item, rest) }) => new(item, rest.Take(count - 1)),
		});
		public Stream<T> DropRecursive(int count) => count == 0 ? this : new(() => (count, this) switch
		{
			(0, _) => Value,
			(_, { Value: null }) => null,
			(_, { Value: var (_, rest) }) => rest.DropRecursive(count - 1).Value,
		});

		internal Stream<T> Reverse() => new(() =>
		{
			StreamCell<T>? current = Value;
			StreamCell<T>? result = null;
			while (current != null)
			{
				var (item, rest) = current;
				current = rest.Value;
				result = Cons(item, result);
			}
			return result;
		});


		private readonly Lazy<StreamCell<T>?> _lazy;
	}
}
