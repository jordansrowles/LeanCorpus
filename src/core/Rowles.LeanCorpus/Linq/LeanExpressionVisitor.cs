using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Linq;

/// <summary>
/// Translates a LINQ <see cref="Expression"/> tree — typically an
/// <c>Expression&lt;Func&lt;TDocument, bool&gt;&gt;</c> — into a
/// LeanCorpus <see cref="Query"/> tree suitable for
/// <see cref="Search.Searcher.IndexSearcher.Search(Query, int)"/>.
/// </summary>
/// <remarks>
/// The struct is allocated on the stack; the returned <see cref="Query"/>
/// objects are the only heap allocations in the translation path.
/// Field lookups are delegated to a <see cref="Func{T, TResult}"/>
/// that is expected to be a source-generator-emitted switch expression
/// (JIT-compiled to a jump table, zero allocation).
/// </remarks>
public readonly struct LeanExpressionVisitor
{
    private readonly Func<string, IFieldDescriptor?>? _fieldResolver;

    /// <summary>
    /// Initialises a new <see cref="LeanExpressionVisitor"/>.
    /// </summary>
    /// <param name="fieldResolver">
    /// Maps C# property names to <see cref="IFieldDescriptor"/> instances.
    /// A typical implementation is a switch expression over property names
    /// returning the corresponding <see cref="Mapping.LeanField{TDoc,TVal}"/>
    /// or <see cref="Mapping.LeanFieldBinding{TDoc}"/>.
    /// When <c>null</c>, member accesses that cannot be resolved will throw
    /// <see cref="NotSupportedException"/>.
    /// </param>
    public LeanExpressionVisitor(Func<string, IFieldDescriptor?>? fieldResolver)
    {
        _fieldResolver = fieldResolver;
    }

    /// <summary>
    /// Translates an expression tree into a <see cref="Query"/>.
    /// </summary>
    /// <param name="expression">
    /// The expression to translate. Typically obtained from
    /// <c>IQueryable.Expression</c> or an <c>Expression&lt;Func&lt;T, bool&gt;&gt;</c>
    /// predicate.
    /// </param>
    /// <returns>A <see cref="Query"/> representing the expression.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when an expression node type is not supported by the translator.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="expression"/> is <c>null</c>.
    /// </exception>
    public Query Translate(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        // Unwrap any outer Quote nodes added by the provider.
        while (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            expression = quote.Operand;

        return Visit(expression);
    }

    private Query Visit(Expression node) => node switch
    {
        LambdaExpression lambda => VisitLambda(lambda),
        BinaryExpression binary => VisitBinary(binary),
        MethodCallExpression call => VisitMethodCall(call),
        UnaryExpression unary => VisitUnary(unary),
        MemberExpression member => VisitMemberPredicate(member),
        _ => throw new NotSupportedException(
            $"Expression node type '{node.NodeType}' is not supported in LeanCorpus LINQ queries. Supported operations are: ==, !=, >, >=, <, <=, .Contains(), .StartsWith(), .EndsWith(), &&, ||, and !."),
    };

    private Query VisitLambda(LambdaExpression lambda)
    {
        // Unwrap the lambda body — strip any nested Quote wrappers.
        Expression body = lambda.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            body = quote.Operand;
        return Visit(body);
    }

    private Query VisitUnary(UnaryExpression unary)
    {
        if (unary.NodeType == ExpressionType.Not)
        {
            var inner = Visit(unary.Operand);
            return new BooleanQuery.Builder()
                .Add(inner, Occur.MustNot)
                .Build();
        }

        throw new NotSupportedException(
            $"Unary expression '{unary.NodeType}' is not supported. Use ! for negation.");
    }

    private Query VisitBinary(BinaryExpression binary)
    {
        return binary.NodeType switch
        {
            ExpressionType.Equal => VisitEqual(binary.Left, binary.Right),
            ExpressionType.NotEqual => VisitNotEqual(binary.Left, binary.Right),
            ExpressionType.GreaterThan => VisitRange(binary.Left, binary.Right, RangeSide.LowerExclusive),
            ExpressionType.GreaterThanOrEqual => VisitRange(binary.Left, binary.Right, RangeSide.LowerInclusive),
            ExpressionType.LessThan => VisitRange(binary.Left, binary.Right, RangeSide.UpperExclusive),
            ExpressionType.LessThanOrEqual => VisitRange(binary.Left, binary.Right, RangeSide.UpperInclusive),
            ExpressionType.AndAlso => VisitAndAlso(binary.Left, binary.Right),
            ExpressionType.OrElse => VisitOrElse(binary.Left, binary.Right),
            _ => throw new NotSupportedException(
                $"Binary expression '{binary.NodeType}' is not supported. Use ==, !=, >, >=, <, <=, &&, or ||."),
        };
    }

    private Query VisitAndAlso(Expression left, Expression right)
    {
        var l = Visit(left);
        var r = Visit(right);

        var builder = new BooleanQuery.Builder();

        // Flatten nested Must clauses from chained &&.
        AddToBuilder(builder, l, Occur.Must);
        AddToBuilder(builder, r, Occur.Must);

        return builder.Build();
    }

    private Query VisitOrElse(Expression left, Expression right)
    {
        var l = Visit(left);
        var r = Visit(right);

        var builder = new BooleanQuery.Builder();

        // Flatten nested Should clauses from chained ||.
        AddToBuilder(builder, l, Occur.Should);
        AddToBuilder(builder, r, Occur.Should);

        return builder.Build();
    }

    /// <summary>
    /// Adds a query to the builder, flattening nested <see cref="BooleanQuery"/>
    /// clauses of the same <see cref="Occur"/> type so that chained
    /// <c>a &amp;&amp; b &amp;&amp; c</c> produces a single flat
    /// <c>BooleanQuery</c> rather than a nested tree.
    /// </summary>
    private static void AddToBuilder(BooleanQuery.Builder builder, Query query, Occur occur)
    {
        if (query is BooleanQuery bq && bq.Clauses.Count > 0 && bq.Clauses.All(c => c.Occur == occur))
        {
            foreach (var clause in bq.Clauses)
                builder.Add(clause.Query, occur);
        }
        else
        {
            builder.Add(query, occur);
        }
    }

    private Query VisitEqual(Expression left, Expression right)
    {
        // Resolve which side is the member access and which is the constant.
        if (!TryResolveMemberAndValue(left, right, out var descriptor, out var value))
            throw new NotSupportedException(
                "Equality comparisons must have a member access on one side and a constant or captured variable on the other. Expressions like 'd.Field == localVar' are supported.");

        EnsureIndexed(descriptor);

        return descriptor.FieldType switch
        {
            FieldType.Numeric => new RangeQuery(descriptor.Name, ToDouble(value), ToDouble(value)),
            FieldType.Int64 => new Int64RangeQuery(descriptor.Name, ToInt64(value), ToInt64(value)),
            _ => new TermQuery(descriptor.Name, ToStringValue(value)),
        };
    }

    private Query VisitNotEqual(Expression left, Expression right)
    {
        if (!TryResolveMemberAndValue(left, right, out var descriptor, out var value))
            throw new NotSupportedException(
                "Inequality comparisons must have a member access on one side and a constant or captured variable on the other.");

        EnsureIndexed(descriptor);

        Query inner = descriptor.FieldType switch
        {
            FieldType.Numeric => new RangeQuery(descriptor.Name, ToDouble(value), ToDouble(value)),
            FieldType.Int64 => new Int64RangeQuery(descriptor.Name, ToInt64(value), ToInt64(value)),
            _ => new TermQuery(descriptor.Name, ToStringValue(value)),
        };

        return new BooleanQuery.Builder()
            .Add(inner, Occur.MustNot)
            .Build();
    }

    private enum RangeSide { LowerInclusive, LowerExclusive, UpperInclusive, UpperExclusive }

    /// <summary>
    /// Translates range comparisons into <see cref="RangeQuery"/> objects.
    /// </summary>
    /// <remarks>
    /// <see cref="RangeQuery"/> only supports inclusive bounds. For open-ended ranges
    /// (e.g. <c>d.Year &gt; 2020</c>), unbounded sides use
    /// <see cref="double.MinValue"/> or <see cref="double.MaxValue"/>. These are valid
    /// <see cref="double"/> values, not sentinel tokens; a document that genuinely stored
    /// an extreme double-precision value could be incorrectly included or excluded.
    /// In practice this is harmless — real-world document values never approach these bounds.
    /// </remarks>
    private Query VisitRange(Expression left, Expression right, RangeSide side)
    {
        if (!TryResolveMemberAndValue(left, right, out var descriptor, out var value))
            throw new NotSupportedException(
                "Range comparisons must have a member access on one side and a constant or captured variable on the other.");

        EnsureIndexed(descriptor);

        if (descriptor.FieldType is not FieldType.Numeric and not FieldType.Int64)
            throw new NotSupportedException(
                $"Range operators (>, >=, <, <=) can only be used with numeric fields. Field '{descriptor.Name}' is of type {descriptor.FieldType}.");

        if (descriptor.FieldType == FieldType.Int64)
        {
            long l = ToInt64(value);
            return side switch
            {
                RangeSide.LowerInclusive => new Int64RangeQuery(descriptor.Name, l, long.MaxValue),
                RangeSide.LowerExclusive => new Int64RangeQuery(descriptor.Name, l + 1, long.MaxValue),
                RangeSide.UpperInclusive => new Int64RangeQuery(descriptor.Name, long.MinValue, l),
                RangeSide.UpperExclusive => new Int64RangeQuery(descriptor.Name, long.MinValue, l - 1),
                _ => throw new NotSupportedException($"Unexpected range side: {side}"),
            };
        }

        double d = ToDouble(value);

        return side switch
        {
            RangeSide.LowerInclusive => new RangeQuery(descriptor.Name, d, double.MaxValue),
            RangeSide.LowerExclusive =>
                IsIntegralValue(value)
                    ? new RangeQuery(descriptor.Name, d + 1.0, double.MaxValue)
                    : new BooleanQuery.Builder()
                        .Add(new RangeQuery(descriptor.Name, double.MinValue, d), Occur.MustNot)
                        .Build(),
            RangeSide.UpperInclusive => new RangeQuery(descriptor.Name, double.MinValue, d),
            RangeSide.UpperExclusive =>
                IsIntegralValue(value)
                    ? new RangeQuery(descriptor.Name, double.MinValue, d - 1.0)
                    : new BooleanQuery.Builder()
                        .Add(new RangeQuery(descriptor.Name, d, double.MaxValue), Occur.MustNot)
                        .Build(),
            _ => throw new NotSupportedException($"Unexpected range side: {side}"),
        };
    }

    private Query VisitMethodCall(MethodCallExpression call)
    {
        // Handle static extension-method Contains(collection, d.Field),
        // e.g. Enumerable.Contains or similar (object is null, 2 args).
        if (call.Object is null && call.Method.Name == "Contains" &&
            call.Arguments.Count == 2)
        {
            return VisitEnumerableContains(call.Arguments[0], call.Arguments[1]);
        }

        // Handle .Contains() on a collection, which can appear in two forms:
        //   d.Tags.Contains("foo")  — member is the field, arg is the value
        //   statuses.Contains(d.Status) — object is the collection, arg is the field
        // Accepts 1 or 2 args (second arg, e.g. StringComparison, is ignored).
        if (call.Method.Name == "Contains" && call.Arguments.Count >= 1)
        {
            return VisitAnyContains(call.Object!, call.Arguments[0], call.Method.DeclaringType == typeof(string));
        }

        if (call.Object is not MemberExpression member)
            throw new NotSupportedException(
                "Method calls like .StartsWith() and .EndsWith() must be called on a member access. Static method calls are not supported.");

        var descriptor = ResolveDescriptor(member);
        EnsureIndexed(descriptor);

        if (call.Arguments.Count < 1)
            throw new NotSupportedException(
                $"Method '{call.Method.Name}' expects at least one argument.");

        string arg = ToStringValue(GetConstantValue(call.Arguments[0]));

        return call.Method.Name switch
        {
            nameof(string.StartsWith) => new PrefixQuery(descriptor.Name, arg),
            nameof(string.EndsWith) => new WildcardQuery(descriptor.Name, string.Concat("*", arg)),
            _ => throw new NotSupportedException(
                $"Method '{call.Method.Name}' is not supported. Use .Contains(), .StartsWith(), or .EndsWith() on string fields, or .Contains() on collection fields."),
        };
    }

    /// <summary>
    /// Handles <c>.Contains()</c> calls in all forms:
    /// <c>d.Title.Contains("sub")</c> (substring → WildcardQuery),
    /// <c>d.Tags.Contains("foo")</c> (collection membership → TermQuery), and
    /// <c>ids.Contains(d.Status)</c> (collection on captured variable → TermInSetQuery).
    /// </summary>
    private Query VisitAnyContains(Expression obj, Expression arg, bool isStringDeclaringType)
    {
        // Unwrap Convert/ConvertChecked nodes on the argument (e.g. from d.Status!).
        while (arg is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } ua)
            arg = ua.Operand;
        while (obj is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } uo)
            obj = uo.Operand;

        // Case 1: string.Contains("sub") — obj is a MemberExpression (the field),
        // declaring type is string → substring search.
        if (isStringDeclaringType && obj is MemberExpression fieldMember)
        {
            var descriptor = ResolveDescriptor(fieldMember);
            EnsureIndexed(descriptor);
            string val = ToStringValue(GetConstantValue(arg));
            return new WildcardQuery(descriptor.Name, string.Concat("*", val, "*"));
        }

        // Case 2: d.Tags.Contains("foo") — obj is a MemberExpression that resolves
        // to a field, arg is the constant value → TermQuery.
        if (obj is MemberExpression fm && TryResolveField(fm, out var desc))
        {
            EnsureIndexed(desc);
            string val = ToStringValue(GetConstantValue(arg));
            return new TermQuery(desc.Name, val);
        }

        // Case 3: collection.Contains(d.Status) — arg is the MemberExpression
        // that resolves to a field, obj is the collection → TermInSetQuery.
        if (arg is MemberExpression argMember && TryResolveField(argMember, out var desc2))
        {
            EnsureIndexed(desc2);
            var terms = GetCollectionValues(obj);
            if (terms.Count == 0)
                return new MatchNoDocsQuery();
            return new TermInSetQuery(desc2.Name, terms);
        }

        throw new NotSupportedException(
            "Could not resolve the field in the .Contains() call. " +
            "Use d.Field.Contains(value) for substring searches or collection.Contains(d.Field) for set membership.");
    }

    /// <summary>
    /// Handles a standalone <c>MemberExpression</c> used as a predicate, e.g.
    /// <c>d.IsPublished</c> — treated as an implicit <c>== true</c>.
    /// </summary>
    private Query VisitMemberPredicate(MemberExpression member)
    {
        var descriptor = ResolveDescriptor(member);
        EnsureIndexed(descriptor);

        return descriptor.FieldType switch
        {
            FieldType.Numeric => new RangeQuery(descriptor.Name, 1.0, 1.0),
            _ => new TermQuery(descriptor.Name, "true"),
        };
    }

    /// <summary>
    /// Translates a static <c>Enumerable.Contains(collection, d.Field)</c> call
    /// — typically <c>ids.Contains(d.Id)</c> — into a <see cref="TermInSetQuery"/>.
    /// </summary>
    private Query VisitEnumerableContains(Expression collection, Expression memberOrValue)
    {
        // Unwrap Convert nodes from generic type conversions
        // (e.g. string[] → IEnumerable<string>).
        while (collection is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
            collection = u.Operand;
        while (memberOrValue is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
            memberOrValue = u.Operand;

        // Unwrap op_Implicit calls (e.g. string[] → ReadOnlySpan<string>
        // emitted by .NET 9+ for array.Contains via MemoryExtensions.Contains).
        if (collection is MethodCallExpression { Method.Name: "op_Implicit" } ic &&
            ic.Arguments.Count == 1)
            collection = ic.Arguments[0];
        if (memberOrValue is MethodCallExpression { Method.Name: "op_Implicit" } im &&
            im.Arguments.Count == 1)
            memberOrValue = im.Arguments[0];

        // Case A: d.Tags.Contains("foo") — the collection IS a document field.
        // The value is the single constant argument → TermQuery.
        if (collection is MemberExpression collMember && TryResolveField(collMember, out var collDesc))
        {
            EnsureIndexed(collDesc);
            string val = ToStringValue(GetConstantValue(memberOrValue));
            return new TermQuery(collDesc.Name, val);
        }

        // Case B: ids.Contains(d.Field) — the value IS a document field,
        // the collection is a captured variable → point-in-set or term-in-set query.
        if (memberOrValue is MemberExpression valMember && TryResolveField(valMember, out var valDesc))
        {
            EnsureIndexed(valDesc);
            return valDesc.FieldType switch
            {
                FieldType.Numeric => CreateNumericPointInSetQuery(valDesc.Name, collection),
                FieldType.Int64 => CreateInt64PointInSetQuery(valDesc.Name, collection),
                _ => CreateTermInSetQuery(valDesc.Name, collection),
            };
        }

        throw new NotSupportedException(
            "Enumerable.Contains requires a document field on one side, e.g. d.Tags.Contains(value) or ids.Contains(d.Field).");
    }

    private static Query CreateNumericPointInSetQuery(string fieldName, Expression collection)
    {
        var values = GetNumericCollectionValues(collection);
        if (values.Count == 0)
            return new MatchNoDocsQuery();
        return new PointInSetQuery(fieldName, values);
    }

    private static Query CreateInt64PointInSetQuery(string fieldName, Expression collection)
    {
        var values = GetInt64CollectionValues(collection);
        if (values.Count == 0)
            return new MatchNoDocsQuery();
        return new Int64PointInSetQuery(fieldName, values);
    }

    private static Query CreateTermInSetQuery(string fieldName, Expression collection)
    {
        var terms = GetCollectionValues(collection);
        if (terms.Count == 0)
            return new MatchNoDocsQuery();
        return new TermInSetQuery(fieldName, terms);
    }

    /// <summary>
    /// Maximum number of terms or values allowed in a Contains() collection.
    /// Prevents unbounded materialisation of captured collections that could OOM
    /// or hang on lazy/infinite enumerables.
    /// </summary>
    private const int MaxInSetTerms = 10_000;

    /// <summary>
    /// Extracts items from a captured collection with a hard size cap.
    /// Only accepts concrete collections (ICollection) so Count can be checked
    /// before materialisation. Rejects lazy/infinite enumerables outright.
    /// </summary>
    private static List<T> GetCappedCollectionValues<T>(object value, Func<object, T> convert, string typeLabel)
    {
        if (value is not ICollection col)
        {
            throw new NotSupportedException(
                $"Contains() requires a concrete collection such as an array, List<T>, or HashSet<T>. " +
                $"Lazy enumerables (e.g. iterator methods, LINQ queries) are not supported because they " +
                $"cannot be checked for size without full materialisation.");
        }

        if (col.Count > MaxInSetTerms)
        {
            throw new NotSupportedException(
                $"Contains() collection has {col.Count:N0} items, which exceeds the maximum of {MaxInSetTerms:N0}. " +
                $"Reduce the collection size or use a different filter strategy.");
        }

        var results = new List<T>();
        foreach (var item in col)
        {
            if (item is not null)
                results.Add(convert(item));
        }

        return results;
    }

    /// <summary>
    /// Extracts string values from a collection expression (constant array or list).
    /// </summary>
    private static List<string> GetCollectionValues(Expression expr)
    {
        var value = GetConstantValue(expr);

        // String is IEnumerable<char> — treat as a single value, not a collection.
        if (value is string s)
        {
            var terms = new List<string>();
            if (!string.IsNullOrEmpty(s))
                terms.Add(s);
            return terms;
        }

        return GetCappedCollectionValues(value, item =>
            item is string si && !string.IsNullOrEmpty(si)
                ? si
                : Convert.ToString(item, CultureInfo.InvariantCulture)!,
            "string");
    }

    /// <summary>
    /// Extracts double values from a collection expression (constant array or list).
    /// </summary>
    private static List<double> GetNumericCollectionValues(Expression expr)
        => GetCappedCollectionValues(GetConstantValue(expr), ToDouble, "numeric");

    /// <summary>
    /// Extracts 64-bit integer values from a collection expression (constant array or list).
    /// </summary>
    private static List<long> GetInt64CollectionValues(Expression expr)
        => GetCappedCollectionValues(GetConstantValue(expr), ToInt64, "int64");

    /// <summary>
    /// Tries to resolve which side of a binary expression is the member access
    /// and which is the value. Handles cases where the constant is on either side.
    /// </summary>
    private bool TryResolveMemberAndValue(
        Expression left, Expression right,
        out IFieldDescriptor descriptor, out object value)
    {
        // Try left as the model member first.
        if (left is MemberExpression leftMember)
        {
            if (TryResolveField(leftMember, out descriptor))
            {
                value = GetConstantValue(right);
                return true;
            }

            // Left didn't resolve but right might be the actual field member
            // (e.g. capturedLocal == d.Field).
            if (right is MemberExpression rightMember && TryResolveField(rightMember, out descriptor))
            {
                value = GetConstantValue(left);
                return true;
            }

            // Neither resolved — throw the specific error for the left member.
            descriptor = ResolveDescriptor(leftMember); // throws
            value = default!;
            return false;
        }

        // Left is not a member; try right as the model member.
        if (right is MemberExpression rm)
        {
            descriptor = ResolveDescriptor(rm); // throws on failure
            value = GetConstantValue(left);
            return true;
        }

        descriptor = default!;
        value = default!;
        return false;
    }

    /// <summary>
    /// Attempts to resolve a <see cref="MemberExpression"/> as a LeanCorpus field
    /// without throwing. Returns <c>false</c> when the resolver returns <c>null</c>,
    /// which lets callers try the other side of a binary expression.
    /// </summary>
    private bool TryResolveField(MemberExpression member, out IFieldDescriptor descriptor)
    {
        string propertyName = member.Member.Name;

        if (_fieldResolver is not null)
        {
            var resolved = _fieldResolver(propertyName);
            if (resolved is not null)
            {
                descriptor = resolved;
                return true;
            }
        }

        descriptor = default!;
        return false;
    }

    /// <summary>
    /// Resolves a <see cref="MemberExpression"/> to an <see cref="IFieldDescriptor"/>
    /// using the field resolver delegate.
    /// </summary>
    private IFieldDescriptor ResolveDescriptor(MemberExpression member)
    {
        string propertyName = member.Member.Name;

        if (_fieldResolver is not null)
        {
            var descriptor = _fieldResolver(propertyName);
            if (descriptor is not null)
                return descriptor;
        }

        throw new NotSupportedException(
            $"Property '{propertyName}' on type '{member.Member.DeclaringType?.Name ?? "?"}' is not recognised as a mapped LeanCorpus field. Ensure the property is decorated with a [LeanText], [LeanString], [LeanNumeric], or similar attribute, or add it to the field resolver delegate.");
    }

    /// <summary>
    /// Extracts the constant value from an expression by evaluating compile-time constants
    /// and captured variable references. Uses the expression interpreter rather than
    /// reflection to remain AOT-compatible.
    /// </summary>
    private static object GetConstantValue(Expression expr)
    {
        // Unwrap any conversion nodes (e.g. int → object boxing,
        // or op_Implicit calls like string[] → ReadOnlySpan<string>).
        while (expr is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            expr = convert.Operand;

        // Unwrap op_Implicit method calls (e.g. string[] → ReadOnlySpan<string>
        // emitted by .NET 9+ for array.Contains() → MemoryExtensions.Contains).
        if (expr is MethodCallExpression { Method.Name: "op_Implicit" } implicitCall &&
            implicitCall.Arguments.Count == 1)
        {
            expr = implicitCall.Arguments[0];
        }

        return expr switch
        {
            ConstantExpression constant => constant.Value!,
            MemberExpression => EvaluateMemberExpression(expr),
            _ => throw new NotSupportedException(
                $"Could not resolve a constant value from expression node type '{expr.NodeType}'. Use a literal, a constant, or a captured local variable in your query predicate."),
        };
    }

    // Cache for compiled member-access delegates, keyed by expression shape.
    // Avoids recompilation of captured-variable reads on every query.
    private static readonly ConcurrentDictionary<Expression, Func<object?>> s_memberAccessorCache = new();

    /// <summary>
    /// Evaluates a <see cref="MemberExpression"/> (typically a closure-captured
    /// local variable) via the expression interpreter. This avoids
    /// <see cref="System.Reflection.FieldInfo.GetValue"/> which is AOT-hostile.
    /// Compiled accessors are cached per unique expression shape.
    /// </summary>
    private static object EvaluateMemberExpression(Expression memberExpr)
    {
        var accessor = s_memberAccessorCache.GetOrAdd(memberExpr, static e =>
            Expression.Lambda<Func<object?>>(
                Expression.Convert(e, typeof(object))
            ).Compile(preferInterpretation: true));

        return accessor()!;
    }

    private static string ToStringValue(object value)
    {
        return value switch
        {
            string s => s,
            null => throw new NotSupportedException("Null values are not supported in query predicates."),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                 ?? throw new NotSupportedException($"Could not convert value of type '{value.GetType().Name}' to a string."),
        };
    }

    private static double ToDouble(object value)
    {
        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        };
    }

    private static long ToInt64(object value)
    {
        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Returns <c>true</c> when the value's CLR type is an integral numeric type
    /// where ±1 adjustment is safe for exclusive range bounds.
    /// </summary>
    private static bool IsIntegralValue(object value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong;
    }

    private static void EnsureIndexed(IFieldDescriptor descriptor)
    {
        if (!descriptor.IsIndexed)
            throw new NotSupportedException(
                $"Field '{descriptor.Name}' is not indexed and cannot be used in query predicates. Set IsIndexed = true on the field mapping.");
    }
}
