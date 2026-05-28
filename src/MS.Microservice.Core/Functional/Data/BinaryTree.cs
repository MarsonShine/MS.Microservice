namespace MS.Microservice.Core.Functional.Data.BinaryTree
{
    public abstract class Tree<T> : IEquatable<Tree<T>>
    {
        public abstract R Match<R>(Func<T, R> Leaf, Func<Tree<T>, Tree<T>, R> Branch);

        public bool Equals(Tree<T>? other) => this.ToString() == other?.ToString(); // hack
        public override bool Equals(object? obj) => Equals((Tree<T>)obj!);

        public override int GetHashCode() => HashCode.Combine(this.ToString()); // hack
    }

    internal class Branch<T> : Tree<T>
    {
        public Tree<T> Left { get; }
        public Tree<T> Right { get; }

        public Branch(Tree<T> Left, Tree<T> Right)
        {
            this.Left = Left;
            this.Right = Right;
        }

        public override string ToString() => $"Branch({Left}, {Right})";

        public override R Match<R>(Func<T, R> Leaf, Func<Tree<T>, Tree<T>, R> Branch)
           => Branch(Left, Right);
    }

    internal class Leaf<T> : Tree<T>
    {
        public T Value { get; }
        public Leaf(T Value) => this.Value = Value;
        public override string ToString() => Value!.ToString()!;

        public override R Match<R>(Func<T, R> Leaf, Func<Tree<T>, Tree<T>, R> Branch)
           => Leaf(Value);
    }

    public static class Tree
    {
        public static Tree<T> Leaf<T>(T Value) => new Leaf<T>(Value);

        public static Tree<T> Branch<T>(Tree<T> Left, Tree<T> Right)
           => new Branch<T>(Left, Right);

        public static Tree<R> Map<T, R>(this Tree<T> @this, Func<T, R> f)
           => @this.Match(
              Leaf: t => Leaf(f(t)),
              Branch: (left, right) => Branch
                 (
                    Left: left.Map(f),
                    Right: right.Map(f)
                 )
           );

        public static Tree<R> Bind<T, R>(this Tree<T> @this, Func<T, Tree<R>> binder)
           => @this.Match(
              Leaf: binder,
              Branch: (left, right) => Branch(left.Bind(binder), right.Bind(binder)));

        public static Tree<T> Insert<T>(this Tree<T> @this, T value)
           => @this.Match(
              Leaf: t => Branch(Leaf(t), Leaf(value)),
              Branch: (l, r) => Branch(l, r.Insert(value)));

        public static T Aggregate<T>(this Tree<T> tree, Func<T, T, T> f)
           => tree.Match(
              Leaf: t => t,
              Branch: (l, r) => f(l.Aggregate(f), r.Aggregate(f)));

        public static Acc Aggregate<T, Acc>(this Tree<T> tree, Acc acc, Func<Acc, T, Acc> f)
           => tree.Match(
              Leaf: t => f(acc, t),
              Branch: (l, r) =>
              {
                  var leftAcc = l.Aggregate(acc, f);
                  return r.Aggregate(leftAcc, f);
              });
    }
}
