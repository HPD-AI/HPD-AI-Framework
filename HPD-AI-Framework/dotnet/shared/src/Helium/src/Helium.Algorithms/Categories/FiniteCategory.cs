using Helium.Primitives;

namespace Helium.Algorithms;

/// <summary>
/// Small finite category with explicit object, morphism, source, target, identity, and composition tables.
/// Composition is written Compose(left, right) = left after right.
/// </summary>
public readonly struct FiniteCategory<O, M> : IEquatable<FiniteCategory<O, M>>
    where O : notnull, ITotalOrder<O>
    where M : notnull, ITotalOrder<M>
{
    private readonly Dictionary<M, O> _source;
    private readonly Dictionary<M, O> _target;
    private readonly Dictionary<O, M> _identities;
    private readonly Dictionary<(M Left, M Right), M> _composition;

    public Finset<O> Objects { get; }
    public Finset<M> Morphisms { get; }

    public FiniteCategory(
        Finset<O> objects,
        Finset<M> morphisms,
        IReadOnlyDictionary<M, O> source,
        IReadOnlyDictionary<M, O> target,
        IReadOnlyDictionary<O, M> identities,
        IReadOnlyDictionary<(M Left, M Right), M> composition)
    {
        _source = new Dictionary<M, O>(source);
        _target = new Dictionary<M, O>(target);
        _identities = new Dictionary<O, M>(identities);
        _composition = new Dictionary<(M Left, M Right), M>(composition);

        Validate(objects, morphisms, _source, _target, _identities, _composition);

        Objects = objects;
        Morphisms = morphisms;
    }

    public O Source(M morphism)
    {
        RequireMorphism(morphism);
        return _source[morphism];
    }

    public O Target(M morphism)
    {
        RequireMorphism(morphism);
        return _target[morphism];
    }

    public M Identity(O obj)
    {
        RequireObject(obj);
        return _identities[obj];
    }

    public bool CanCompose(M left, M right)
    {
        RequireMorphism(left);
        RequireMorphism(right);
        return _target[right].Equals(_source[left]);
    }

    public M Compose(M left, M right)
    {
        if (!CanCompose(left, right))
            throw new InvalidOperationException("Morphism source and target do not match for composition.");

        return _composition[(left, right)];
    }

    public bool Equals(FiniteCategory<O, M> other) =>
        Objects == other.Objects &&
        Morphisms == other.Morphisms &&
        DictionaryEquals(_source, other._source) &&
        DictionaryEquals(_target, other._target) &&
        DictionaryEquals(_identities, other._identities) &&
        DictionaryEquals(_composition, other._composition);

    public override bool Equals(object? obj) => obj is FiniteCategory<O, M> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Objects, Morphisms);
    public static bool operator ==(FiniteCategory<O, M> left, FiniteCategory<O, M> right) => left.Equals(right);
    public static bool operator !=(FiniteCategory<O, M> left, FiniteCategory<O, M> right) => !left.Equals(right);

    private void RequireObject(O obj)
    {
        if (!Objects.Contains(obj))
            throw new ArgumentException("The object is not in this category.", nameof(obj));
    }

    private void RequireMorphism(M morphism)
    {
        if (!Morphisms.Contains(morphism))
            throw new ArgumentException("The morphism is not in this category.", nameof(morphism));
    }

    private static void Validate(
        Finset<O> objects,
        Finset<M> morphisms,
        IReadOnlyDictionary<M, O> source,
        IReadOnlyDictionary<M, O> target,
        IReadOnlyDictionary<O, M> identities,
        IReadOnlyDictionary<(M Left, M Right), M> composition)
    {
        foreach (var morphism in morphisms.Elements)
        {
            if (!source.TryGetValue(morphism, out var sourceObject) || !objects.Contains(sourceObject))
                throw new ArgumentException("Every morphism must have a source object.", nameof(source));
            if (!target.TryGetValue(morphism, out var targetObject) || !objects.Contains(targetObject))
                throw new ArgumentException("Every morphism must have a target object.", nameof(target));
        }

        foreach (var key in source.Keys)
        {
            if (!morphisms.Contains(key))
                throw new ArgumentException("The source table contains a morphism outside the category.", nameof(source));
        }

        foreach (var key in target.Keys)
        {
            if (!morphisms.Contains(key))
                throw new ArgumentException("The target table contains a morphism outside the category.", nameof(target));
        }

        foreach (var obj in objects.Elements)
        {
            if (!identities.TryGetValue(obj, out var identity) || !morphisms.Contains(identity))
                throw new ArgumentException("Every object must have an identity morphism.", nameof(identities));
            if (!source[identity].Equals(obj) || !target[identity].Equals(obj))
                throw new ArgumentException("Identity morphisms must start and end at their object.", nameof(identities));
        }

        foreach (var key in identities.Keys)
        {
            if (!objects.Contains(key))
                throw new ArgumentException("The identity table contains an object outside the category.", nameof(identities));
        }

        foreach (var ((left, right), result) in composition)
        {
            if (!morphisms.Contains(left) || !morphisms.Contains(right) || !morphisms.Contains(result))
                throw new ArgumentException("The composition table contains a morphism outside the category.", nameof(composition));
            if (!target[right].Equals(source[left]))
                throw new ArgumentException("The composition table contains a non-composable pair.", nameof(composition));
            if (!source[result].Equals(source[right]) || !target[result].Equals(target[left]))
                throw new ArgumentException("The composition result has the wrong source or target.", nameof(composition));
        }

        foreach (var left in morphisms.Elements)
        foreach (var right in morphisms.Elements)
        {
            var composable = target[right].Equals(source[left]);
            var hasEntry = composition.ContainsKey((left, right));

            if (composable && !hasEntry)
                throw new ArgumentException("The composition table is missing a composable pair.", nameof(composition));
            if (!composable && hasEntry)
                throw new ArgumentException("The composition table contains a non-composable pair.", nameof(composition));
        }

        foreach (var f in morphisms.Elements)
        {
            var leftIdentity = identities[target[f]];
            var rightIdentity = identities[source[f]];

            if (!composition[(leftIdentity, f)].Equals(f))
                throw new ArgumentException("Left identity law failed.", nameof(composition));
            if (!composition[(f, rightIdentity)].Equals(f))
                throw new ArgumentException("Right identity law failed.", nameof(composition));
        }

        foreach (var h in morphisms.Elements)
        foreach (var g in morphisms.Elements)
        foreach (var f in morphisms.Elements)
        {
            if (!target[f].Equals(source[g]) || !target[g].Equals(source[h]))
                continue;

            var leftAssociated = composition[(h, composition[(g, f)])];
            var rightAssociated = composition[(composition[(h, g)], f)];
            if (!leftAssociated.Equals(rightAssociated))
                throw new ArgumentException("Associativity law failed.", nameof(composition));
        }
    }

    private static bool DictionaryEquals<TKey, TValue>(Dictionary<TKey, TValue> left, Dictionary<TKey, TValue> right)
        where TKey : notnull
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var otherValue) || !EqualityComparer<TValue>.Default.Equals(value, otherValue))
                return false;
        }

        return true;
    }
}

/// <summary>
/// Finite functor between two finite categories.
/// </summary>
public readonly struct FiniteFunctor<ODomain, MDomain, OCodomain, MCodomain>
    where ODomain : notnull, ITotalOrder<ODomain>
    where MDomain : notnull, ITotalOrder<MDomain>
    where OCodomain : notnull, ITotalOrder<OCodomain>
    where MCodomain : notnull, ITotalOrder<MCodomain>
{
    private readonly Dictionary<ODomain, OCodomain> _objectMap;
    private readonly Dictionary<MDomain, MCodomain> _morphismMap;

    public FiniteCategory<ODomain, MDomain> Domain { get; }
    public FiniteCategory<OCodomain, MCodomain> Codomain { get; }

    public FiniteFunctor(
        FiniteCategory<ODomain, MDomain> domain,
        FiniteCategory<OCodomain, MCodomain> codomain,
        IReadOnlyDictionary<ODomain, OCodomain> objectMap,
        IReadOnlyDictionary<MDomain, MCodomain> morphismMap)
    {
        _objectMap = new Dictionary<ODomain, OCodomain>(objectMap);
        _morphismMap = new Dictionary<MDomain, MCodomain>(morphismMap);

        Validate(domain, codomain, _objectMap, _morphismMap);

        Domain = domain;
        Codomain = codomain;
    }

    public OCodomain MapObject(ODomain obj)
    {
        if (!Domain.Objects.Contains(obj))
            throw new ArgumentException("The object is not in the domain category.", nameof(obj));
        return _objectMap[obj];
    }

    public MCodomain MapMorphism(MDomain morphism)
    {
        if (!Domain.Morphisms.Contains(morphism))
            throw new ArgumentException("The morphism is not in the domain category.", nameof(morphism));
        return _morphismMap[morphism];
    }

    private static void Validate(
        FiniteCategory<ODomain, MDomain> domain,
        FiniteCategory<OCodomain, MCodomain> codomain,
        IReadOnlyDictionary<ODomain, OCodomain> objectMap,
        IReadOnlyDictionary<MDomain, MCodomain> morphismMap)
    {
        foreach (var obj in domain.Objects.Elements)
        {
            if (!objectMap.TryGetValue(obj, out var image) || !codomain.Objects.Contains(image))
                throw new ArgumentException("Every domain object must map to a codomain object.", nameof(objectMap));
        }

        foreach (var morphism in domain.Morphisms.Elements)
        {
            if (!morphismMap.TryGetValue(morphism, out var image) || !codomain.Morphisms.Contains(image))
                throw new ArgumentException("Every domain morphism must map to a codomain morphism.", nameof(morphismMap));

            if (!codomain.Source(image).Equals(objectMap[domain.Source(morphism)]) ||
                !codomain.Target(image).Equals(objectMap[domain.Target(morphism)]))
                throw new ArgumentException("Functor morphism maps must preserve source and target.", nameof(morphismMap));
        }

        foreach (var obj in domain.Objects.Elements)
        {
            if (!morphismMap[domain.Identity(obj)].Equals(codomain.Identity(objectMap[obj])))
                throw new ArgumentException("Functor must preserve identity morphisms.", nameof(morphismMap));
        }

        foreach (var left in domain.Morphisms.Elements)
        foreach (var right in domain.Morphisms.Elements)
        {
            if (!domain.CanCompose(left, right))
                continue;

            var mappedComposite = morphismMap[domain.Compose(left, right)];
            var compositeOfMaps = codomain.Compose(morphismMap[left], morphismMap[right]);
            if (!mappedComposite.Equals(compositeOfMaps))
                throw new ArgumentException("Functor must preserve composition.", nameof(morphismMap));
        }
    }
}

/// <summary>
/// Natural transformation between finite functors with the same source and target categories.
/// </summary>
public readonly struct NaturalTransformation<ODomain, MDomain, OCodomain, MCodomain>
    where ODomain : notnull, ITotalOrder<ODomain>
    where MDomain : notnull, ITotalOrder<MDomain>
    where OCodomain : notnull, ITotalOrder<OCodomain>
    where MCodomain : notnull, ITotalOrder<MCodomain>
{
    private readonly Dictionary<ODomain, MCodomain> _components;

    public FiniteFunctor<ODomain, MDomain, OCodomain, MCodomain> SourceFunctor { get; }
    public FiniteFunctor<ODomain, MDomain, OCodomain, MCodomain> TargetFunctor { get; }

    public NaturalTransformation(
        FiniteFunctor<ODomain, MDomain, OCodomain, MCodomain> sourceFunctor,
        FiniteFunctor<ODomain, MDomain, OCodomain, MCodomain> targetFunctor,
        IReadOnlyDictionary<ODomain, MCodomain> components)
    {
        if (sourceFunctor.Domain != targetFunctor.Domain || sourceFunctor.Codomain != targetFunctor.Codomain)
            throw new ArgumentException("Natural transformations require functors with the same domain and codomain.");

        _components = new Dictionary<ODomain, MCodomain>(components);
        Validate(sourceFunctor, targetFunctor, _components);

        SourceFunctor = sourceFunctor;
        TargetFunctor = targetFunctor;
    }

    public MCodomain Component(ODomain obj)
    {
        if (!SourceFunctor.Domain.Objects.Contains(obj))
            throw new ArgumentException("The object is not in the domain category.", nameof(obj));
        return _components[obj];
    }

    private static void Validate(
        FiniteFunctor<ODomain, MDomain, OCodomain, MCodomain> sourceFunctor,
        FiniteFunctor<ODomain, MDomain, OCodomain, MCodomain> targetFunctor,
        IReadOnlyDictionary<ODomain, MCodomain> components)
    {
        var codomain = sourceFunctor.Codomain;

        foreach (var obj in sourceFunctor.Domain.Objects.Elements)
        {
            if (!components.TryGetValue(obj, out var component) || !codomain.Morphisms.Contains(component))
                throw new ArgumentException("Every domain object must have a codomain morphism component.", nameof(components));

            if (!codomain.Source(component).Equals(sourceFunctor.MapObject(obj)) ||
                !codomain.Target(component).Equals(targetFunctor.MapObject(obj)))
                throw new ArgumentException("A natural transformation component must map F(x) to G(x).", nameof(components));
        }

        foreach (var morphism in sourceFunctor.Domain.Morphisms.Elements)
        {
            var x = sourceFunctor.Domain.Source(morphism);
            var y = sourceFunctor.Domain.Target(morphism);

            var left = codomain.Compose(targetFunctor.MapMorphism(morphism), components[x]);
            var right = codomain.Compose(components[y], sourceFunctor.MapMorphism(morphism));
            if (!left.Equals(right))
                throw new ArgumentException("Naturality square failed.", nameof(components));
        }
    }
}
