using System;
using System.Collections.Immutable;
using System.Linq;
using ArxisStudio.Markup.Xaml;
using BenchmarkDotNet.Attributes;

namespace ArxisStudio.Markup.Benchmarks;

/// <summary>
/// Lexing, parsing, writing back and editing — the operations an editor performs on every
/// keystroke and therefore the ones whose cost is felt.
/// </summary>
/// <remarks>
/// Round-trip and edit are the two the contract's performance principles are really about.
/// Writing an unchanged document back must not cost what formatting it would, and an edit must
/// cost what the edit is rather than what the document is.
/// </remarks>
[MemoryDiagnoser]
public class SyntaxBenchmarks
{
    private static readonly Uri DocumentUri = new("file:///Views/Benchmark.axaml");

    private SourceText _text = null!;
    private XamlDocument _document = null!;
    private XamlElement _target = null!;

    /// <summary>Gets or sets how many leaf controls the document being measured contains.</summary>
    [Params(20, 200)]
    public int Controls { get; set; }

    /// <summary>Builds the document each benchmark starts from.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _text = SourceText.From(SampleDocuments.View(Controls));
        _document = XamlDocument.Parse(_text, new XamlParseOptions { DocumentUri = DocumentUri });
        _target = _document.DescendantElements()
            .Last(static element => element.Name.LocalName == "Button");
    }

    /// <summary>Lexing alone, without building a tree over the tokens.</summary>
    /// <returns>The number of tokens, so nothing is optimised away.</returns>
    [Benchmark]
    public int Lex()
    {
        (ImmutableArray<XamlToken> tokens, _) = XamlLexer.Lex(_text, DocumentUri);

        return tokens.Length;
    }

    /// <summary>Lexing and parsing, which is what opening a document costs.</summary>
    /// <returns>The parsed document.</returns>
    [Benchmark]
    public XamlDocument Parse() =>
        XamlDocument.Parse(_text, new XamlParseOptions { DocumentUri = DocumentUri });

    /// <summary>
    /// Writing an unchanged document back, which must be a copy rather than a reformat.
    /// </summary>
    /// <returns>The document's text.</returns>
    [Benchmark]
    public string RoundTrip() => _document.GetText();

    /// <summary>Setting one attribute, which reparses and must not cost a whole edit session.</summary>
    /// <returns>The new document.</returns>
    [Benchmark]
    public XamlDocument SetOneAttribute() =>
        _document.SetAttribute(_target, XamlQualifiedName.Parse("Width"), "320");

    /// <summary>Parsing a markup extension, which happens once per value an editor inspects.</summary>
    /// <returns>The parsed value.</returns>
    [Benchmark]
    public XamlValue ParseMarkupExtension() =>
        XamlValue.Parse("{Binding Path=Rows[3].Name, Mode=TwoWay, Converter={StaticResource Titles}}");

    /// <summary>
    /// Resolving a prefix from the deepest element, which walks the whole scope chain.
    /// </summary>
    /// <returns>The namespace URI the prefix resolves to.</returns>
    [Benchmark]
    public string? ResolveNamespace() => _target.NamespaceContext.LookupNamespace("x");
}
