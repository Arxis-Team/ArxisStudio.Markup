# Documents

Reading XAML without losing any of it.

## Text

`SourceText` is an immutable snapshot with a line index. It knows its encoding and whether the file
had a byte order mark, so a document read from disk can be written back to disk unchanged.

```csharp
SourceText text = SourceText.From(source);
SourceText fromFile = await SourceText.FromAsync(stream, cancellationToken: token);

int lines = text.Lines.Count;
TextPosition where = text.Lines.GetPosition(offset);   // line and column of an offset
string line = text.GetText(text.Lines[3].Span);
```

`TextSpan` is a start and a length. `TextChange` is a span and the text that replaces it — the unit
every edit in these libraries is expressed in.

```csharp
var change = new TextChange(new TextSpan(offset, 3), "480");

SourceText updated = text.WithChange(change);
```

## Parsing

```csharp
var document = XamlDocument.Parse(source);

var identified = XamlDocument.Parse(
    source,
    new XamlParseOptions { DocumentUri = new Uri("file:///Views/MainView.axaml") });
```

`DocumentUri` is what diagnostics point at and what relative includes resolve against. Supply it
whenever the document came from somewhere.

Parsing never throws on malformed input. A document caught mid-keystroke still produces a tree, and
the errors are diagnostics:

```csharp
if (!document.IsWellFormed)
{
    foreach (MarkupDiagnostic diagnostic in document.GetDiagnostics())
    {
        TextPosition at = document.SourceText.Lines.GetPosition(diagnostic.Span!.Value.Start);

        Console.WriteLine($"{diagnostic.Code} line {at.Line + 1}: {diagnostic.Message}");
    }
}
```

## Round-trip

```csharp
string same = document.GetText();                              // byte for byte, always
string reflowed = document.GetText(XamlWriteMode.Format);       // reformats the whole document
```

`Preserve` is the default everywhere and is what saving uses. `Format` exists for a caller who
explicitly wants the document reflowed, and is never reached by accident.

## Navigating

Every node carries the span it occupies, so a tree position and a text position are the same thing.

```csharp
XamlElement? root = document.Root;

IEnumerable<XamlElement> all = document.DescendantElements();

XamlSyntaxNode? at = document.FindNode(caretOffset);            // what is under the caret
XamlElement? element = at?.AncestorsAndSelf().OfType<XamlElement>().FirstOrDefault();
```

`XamlElement` answers what an element is and where:

```csharp
element.Name                    // XamlQualifiedName: prefix and local name
element.NamespaceUri            // what the prefix resolves to, or null when it is not in scope
element.Span                    // the whole element, start tag through end tag
element.StartTagSpan            // the start tag alone
element.NameSpan                // the name inside it
element.IsEmpty                 // self-closing
element.IsUnclosed              // the parser never found an end tag
element.IsPropertyElementSyntax // <Border.Resources> rather than <Border>
element.MemberName              // "Resources", for a property element
element.OwnerName               // "Border", for a property element
```

`Elements` is every child element **including property elements**. Filter when you mean content:

```csharp
IEnumerable<XamlElement> content = element.Elements.Where(child => !child.IsPropertyElementSyntax);
```

`Content` is everything inside the element in order — elements, text, CDATA, comments, processing
instructions — which is what makes whitespace and comments visible rather than lost.

## Attributes and values

```csharp
XamlAttribute? width = element.GetAttribute("Width");
XamlAttribute? qualified = element.GetAttribute(XamlQualifiedName.Parse("Grid.Row"));

string? name = element.GetDirective(XamlDirectives.Name);        // x:Name
string? design = element.GetDesignTimeAttribute("Text");         // d:Text

IEnumerable<XamlAttribute> directives = element.Directives;      // x:*
IEnumerable<XamlAttribute> designTime = element.DesignTimeAttributes;
IEnumerable<XamlNamespaceDeclaration> namespaces = element.NamespaceDeclarations;
```

A value is read for what it *is*, without any CLR type being involved:

```csharp
switch (attribute.GetValue())
{
    case XamlMarkupExtensionValue extension:
        // {Binding Customer.Name}, {StaticResource Accent}, nested and all
        Console.WriteLine(extension.TypeName);
        foreach (XamlMarkupExtensionArgument argument in extension.Arguments)
        {
            Console.WriteLine($"  {argument}");
        }

        break;

    case XamlLiteralValue literal:
        Console.WriteLine(literal.Text);
        break;
}
```

`attribute.GetValueText()` gives the raw text between the quotes, entity references unexpanded —
which is what makes read-modify-write lossless. Use `XamlLiteralValue.FromPlainText` when you hold
text that must stay literal whatever it looks like.

## Namespaces

```csharp
XamlNamespaceContext context = element.NamespaceContext;

string? uri = context.LookupNamespace("x");
string? prefix = context.LookupPrefix(XamlNamespaces.Xaml);
```

A prefix is resolved where it is used, so an element deep in a document sees exactly the
declarations in scope for it.

## Resource references

What a document pulls in, discovered from the syntax alone — no Avalonia, no file system:

```csharp
ImmutableArray<XamlResourceReference> references = XamlResourceAnalyzer.Discover(document);

foreach (XamlResourceReference reference in references)
{
    // Kind: ResourceInclude or StyleInclude
    Console.WriteLine($"{reference.Kind} {reference.SourceText} → {reference.ResolvedUri}");
}
```

`XamlResourceGraph` follows those references across files and answers what depends on what — see
[Updates](updates.md#what-a-changed-file-costs).
