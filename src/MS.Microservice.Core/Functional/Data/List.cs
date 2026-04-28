namespace MS.Microservice.Core.Functional.Data.LinkedList
{
    using static MS.Microservice.Core.Functional.F;
    public sealed class List<T>
    {
        readonly bool isEmpty;
        readonly T? head;
        readonly List<T>? tail;

        // the empty list
        internal List() { isEmpty = true; }

        // the non empty list
        internal List(T head, List<T> tail)
        {
            this.head = head;
            this.tail = tail;
        }

        public R Match<R>(Func<R> Empty, Func<T, List<T>, R> Cons)
           => isEmpty ? Empty() : Cons(head!, tail!);

        public Option<T> Head => Match(
           () => (Option<T>)None,
           (head, _) => (Option<T>)Some(head));

        public Option<List<T>> Tail => Match(
           () => (Option<List<T>>)None,
           (_, tail) => (Option<List<T>>)Some(tail));

        public Option<T> this[int index] => index < 0
           ? (Option<T>)None
           : Match(
              () => (Option<T>)None,
              (head, tail) => index == 0 ? (Option<T>)Some(head) : tail[index - 1]);

        public IEnumerable<T> AsEnumerable()
        {
            if (isEmpty) 
                yield break;

            yield return head!;
            foreach (T t in tail!.AsEnumerable())
                yield return t;
        }

        public override string ToString() => Match(
           () => "{ }",
           (_, __) => $"{{ {string.Join(", ", AsEnumerable().Map(v => v!.ToString()))} }}");
    }

    public static class LinkedList
    {
        // factory functions
        public static List<T> List<T>(T h, List<T> t) => new List<T>(h, t);

        public static List<T> List<T>(params T[] items)
           => items.Reverse().Aggregate(new List<T>()
              , (tail, head) => List(head, tail));

        public static int Length<T>(this List<T> @this) => @this.Match(
           () => 0,
           (t, ts) => 1 + ts.Length());

        public static List<T> Add<T>(this List<T> @this, T value)
           => List(value, @this);

        public static List<T> Append<T>(this List<T> @this, T value)
           => @this.Match(
              () => List(value, List<T>()),
              (head, tail) => List(head, tail.Append(value)));

        public static Option<List<T>> InsertAt<T>(this List<T> @this, int m, T value)
           => m < 0
              ? (Option<List<T>>)None
              : m == 0
                 ? (Option<List<T>>)Some(List(value, @this))
                 : @this.Match(
                    () => (Option<List<T>>)None,
                    (head, tail) => tail
                       .InsertAt(m - 1, value)
                       .Map(insertedTail => List(head, insertedTail)));

        public static Option<List<T>> RemoveAt<T>(this List<T> @this, int index)
           => index < 0
              ? (Option<List<T>>)None
              : @this.Match(
                 () => (Option<List<T>>)None,
                 (head, tail) => index == 0
                    ? (Option<List<T>>)Some(tail)
                    : tail
                       .RemoveAt(index - 1)
                       .Map(updatedTail => List(head, updatedTail)));

        public static List<T> TakeWhile<T>(this List<T> @this, Func<T, bool> predicate)
           => @this.Match(
              () => List<T>(),
              (head, tail) => predicate(head)
                 ? List(head, tail.TakeWhile(predicate))
                 : List<T>());

        public static List<T> DropWhile<T>(this List<T> @this, Func<T, bool> predicate)
           => @this.Match(
              () => List<T>(),
              (head, tail) => predicate(head)
                 ? tail.DropWhile(predicate)
                 : @this);

        public static List<R> Map<T, R>(this List<T> @this, Func<T, R> f)
           => @this.Match(
              () => List<R>(),
              (head, tail) => List(f(head), tail.Map(f)));

        public static Unit ForEach<T>(this List<T> @this, Action<T> action)
        {
            @this.Map(action.ToFunc());
            return UnitValue;
        }

        public static List<R> Bind<T, R>(this List<T> @this, Func<T, List<R>> f)
           => @this.Map(f).Join();

        public static List<T> Join<T>(this List<List<T>> @this) => @this.Match(
           () => List<T>(),
           (xs, xss) => concat(xs, Join(xss)));

        public static Acc Aggregate<T, Acc>(this List<T> @this, Acc acc, Func<Acc, T, Acc> f)
           => @this.Match(
              () => acc,
              (head, tail) => Aggregate(tail, f(acc, head), f));

        public static IEnumerable<T> TakeWhile<T>(IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (var item in source)
            {
                if (!predicate(item))
                    yield break;

                yield return item;
            }
        }

        public static IEnumerable<T> DropWhile<T>(IEnumerable<T> source, Func<T, bool> predicate)
        {
            using var enumerator = source.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (predicate(enumerator.Current))
                    continue;

                yield return enumerator.Current;
                break;
            }

            while (enumerator.MoveNext())
                yield return enumerator.Current;
        }

        static List<T> concat<T>(List<T> l, List<T> r) => l.Match(
           () => r,
           (h, t) => List(h, concat(t, r)));
    }
}
