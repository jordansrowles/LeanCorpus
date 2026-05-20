using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rowles.LeanCorpus.SourceGen.Emitters;
using Rowles.LeanCorpus.SourceGen.Models;
using Rowles.LeanCorpus.SourceGen.Pipeline;

namespace Rowles.LeanCorpus.SourceGen;

/// <summary>
/// Roslyn incremental source generator that produces typed document mappers, schemas, and
/// stored-field materialisers for <c>[LeanDocument]</c>-decorated types.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class LeanDocumentGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeReader.LeanDocumentAttribute,
            predicate: static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax,
            transform: static (ctx, ct) =>
            {
                if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol) return null;
                var attr = ctx.Attributes.FirstOrDefault();
                if (attr is null) return null;
                return AttributeReader.Read(typeSymbol, attr, ct);
            })
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(models, static (spc, doc) =>
        {
            foreach (var d in doc.Diagnostics)
                spc.ReportDiagnostic(d);

            if (doc.Fields.Count == 0) return;

            string source = DocumentMapEmitter.Emit(doc);
            string hint = (string.IsNullOrEmpty(doc.Namespace) ? doc.TypeName : doc.Namespace + "." + doc.TypeName) + "Index.g.cs";
            spc.AddSource(hint, source);
        });
    }
}
