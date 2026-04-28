namespace MS.Microservice.Core.Functional.Data.LabelTree
{
    using LinkedList;

    public sealed class LabelTree<T>
    {
        public LabelTree(T label, List<LabelTree<T>> children)
        {
            Label = label;
            Children = children;
        }

        public T Label { get; }
        public List<LabelTree<T>> Children { get; }
    }

    public static class LabelTree
    {
        public static LabelTree<T> Node<T>(T label, params LabelTree<T>[] children)
            => new(label, LinkedList.List(children));

        public static LabelTree<TResult> Map<T, TResult>(this LabelTree<T> tree, Func<T, TResult> map)
            => new(map(tree.Label), tree.Children.Map(child => child.Map(map)));

        public static LabelTree<string> Localize(this LabelTree<string> tree, IReadOnlyDictionary<string, string> translations)
            => tree.Map(label => translations.TryGetValue(label, out var translated) ? translated : label);
    }
}
