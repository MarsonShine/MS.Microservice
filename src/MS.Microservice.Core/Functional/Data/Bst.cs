using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Core.Functional.Data.Tree
{
    public abstract class Tree<T>
        where T : IComparable<T>
    {
        public abstract R Match<R>(Func<R> Empty, Func<Tree<T>, T, Tree<T>, R> Node);
        public abstract bool IsEmpty { get; }
        public abstract bool Contains(T value);
        public abstract Tree<T> Insert(T value);
        public abstract IEnumerable<T> AsEnumerable();

        public bool Equals(Tree<T> other) => this.ToString() == other.ToString(); // hack
        public override bool Equals(object? obj) => Equals((Tree<T>)obj!);

        public override int GetHashCode() => HashCode.Combine(this.ToString()); // hack
    }
}
