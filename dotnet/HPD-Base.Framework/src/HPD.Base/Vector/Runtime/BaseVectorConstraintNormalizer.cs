using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseVectorConstraintNormalizer
{
    internal static (BaseVectorCandidateConstraint Constraint, BaseVectorConstraintDigest Digest) Normalize(BaseVectorCandidateConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        int nodes = 0;
        BaseVectorCandidateConstraint normalized = Visit(constraint, 1, ref nodes);
        byte[] encoded = Encoding.UTF8.GetBytes(Encode(normalized));
        return (normalized, BaseVectorConstraintDigest.Create(SHA256.HashData(encoded)));
    }

    private static BaseVectorCandidateConstraint Visit(BaseVectorCandidateConstraint node, int depth, ref int nodes)
    {
        if (depth > 8 || ++nodes > 64) throw new ArgumentException("The vector candidate constraint exceeds its fixed complexity limits.", nameof(node));
        return node switch
        {
            BaseVectorCandidateConstraint.True => node,
            BaseVectorCandidateConstraint.False => node,
            BaseVectorCandidateConstraint.Equal equal when equal.Value.Kind == equal.Field.ValueKind => equal,
            BaseVectorCandidateConstraint.In @in => @in,
            BaseVectorCandidateConstraint.And and => NormalizeGroup(true, VisitChildren(and.Children, depth, ref nodes)),
            BaseVectorCandidateConstraint.Or or => NormalizeGroup(false, VisitChildren(or.Children, depth, ref nodes)),
            _ => throw new ArgumentException("The vector candidate constraint is malformed.", nameof(node)),
        };
    }

    private static BaseVectorCandidateConstraint[] VisitChildren(System.Collections.Immutable.ImmutableArray<BaseVectorCandidateConstraint> children, int depth, ref int nodes)
    {
        var result = new BaseVectorCandidateConstraint[children.Length];
        for (int index = 0; index < children.Length; index++) result[index] = Visit(children[index], depth + 1, ref nodes);
        return result;
    }

    private static BaseVectorCandidateConstraint NormalizeGroup(bool and, IEnumerable<BaseVectorCandidateConstraint> source)
    {
        BaseVectorCandidateConstraint[] children = source.SelectMany(child => child switch { BaseVectorCandidateConstraint.And nested when and => nested.Children, BaseVectorCandidateConstraint.Or nested when !and => nested.Children, _ => [child] }).Distinct().OrderBy(Encode, StringComparer.Ordinal).ToArray();
        if (and && children.Any(static child => child is BaseVectorCandidateConstraint.False)) return new BaseVectorCandidateConstraint.False();
        if (!and && children.Any(static child => child is BaseVectorCandidateConstraint.True)) return new BaseVectorCandidateConstraint.True();
        children = children.Where(child => and ? child is not BaseVectorCandidateConstraint.True : child is not BaseVectorCandidateConstraint.False).ToArray();
        if (children.Length == 0) return and ? new BaseVectorCandidateConstraint.True() : new BaseVectorCandidateConstraint.False();
        if (children.Length == 1) return children[0];
        return and ? new BaseVectorCandidateConstraint.And(children) : new BaseVectorCandidateConstraint.Or(children);
    }

    private static string Encode(BaseVectorCandidateConstraint node) => node switch
    {
        BaseVectorCandidateConstraint.True => "T",
        BaseVectorCandidateConstraint.False => "F",
        BaseVectorCandidateConstraint.Equal equal => $"E:{equal.Field.StableFieldId.Length}:{equal.Field.StableFieldId}:{Value(equal.Value)}",
        BaseVectorCandidateConstraint.In @in => $"I:{@in.Field.StableFieldId.Length}:{@in.Field.StableFieldId}:{string.Join(';', @in.Values.Select(Value).Order(StringComparer.Ordinal))}",
        BaseVectorCandidateConstraint.And and => "A[" + string.Join(',', and.Children.Select(Encode)) + "]",
        BaseVectorCandidateConstraint.Or or => "O[" + string.Join(',', or.Children.Select(Encode)) + "]",
        _ => throw new InvalidOperationException(),
    };
    private static string Value(BaseVectorFilterValue value) => value.Kind switch { BaseVectorFilterValueKind.Null => "n", BaseVectorFilterValueKind.Boolean => value.Boolean == true ? "b1" : "b0", BaseVectorFilterValueKind.Integer => "i" + value.Integer!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), BaseVectorFilterValueKind.String => "s" + value.Text!.Length + ":" + value.Text, BaseVectorFilterValueKind.Id => "d" + value.Text!.Length + ":" + value.Text, _ => throw new InvalidOperationException() };
}
