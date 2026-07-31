using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

public sealed class XamlValueTests
{
    private static XamlMarkupExtensionValue Extension(string text) =>
        Assert.IsType<XamlMarkupExtensionValue>(XamlValue.Parse(text));

    [Theory]
    [InlineData("320")]
    [InlineData("")]
    [InlineData("Hello, world")]
    [InlineData("a &amp; b")]
    public void OrdinaryTextIsALiteral(string text)
    {
        XamlLiteralValue literal = Assert.IsType<XamlLiteralValue>(XamlValue.Parse(text));

        Assert.Equal(text, literal.Text);
        Assert.Equal(text, literal.ToXamlText());
    }

    [Fact]
    public void EntityReferencesInALiteralAreLeftUnexpanded()
    {
        // The value is what the document says, not what it would mean once decoded.
        Assert.Equal("a &amp; b &#65;", ((XamlLiteralValue)XamlValue.Parse("a &amp; b &#65;")).Text);
    }

    [Fact]
    public void TheEmptyBraceEscapeProducesALiteral()
    {
        XamlLiteralValue literal = Assert.IsType<XamlLiteralValue>(XamlValue.Parse("{}{not an extension}"));

        Assert.Equal("{not an extension}", literal.Text);

        // Writing it back has to restore the escape, or it would read as an extension.
        Assert.Equal("{}{not an extension}", literal.ToXamlText());
    }

    [Fact]
    public void ASimpleExtensionHasItsTypeNameAndOnePositionalArgument()
    {
        XamlMarkupExtensionValue binding = Extension("{Binding Customer.Name}");

        Assert.Equal("Binding", binding.TypeName.LocalName);

        XamlMarkupExtensionArgument argument = Assert.Single(binding.Arguments);

        Assert.True(argument.IsPositional);
        Assert.Equal("Customer.Name", ((XamlLiteralValue)argument.Value).Text);
    }

    [Fact]
    public void AnExtensionWithNoArgumentsIsRecognised()
    {
        XamlMarkupExtensionValue value = Extension("{x:Null}");

        Assert.Equal("x", value.TypeName.Prefix);
        Assert.Equal("Null", value.TypeName.LocalName);
        Assert.Empty(value.Arguments);
    }

    [Fact]
    public void NamedArgumentsAreDistinguishedFromPositionalOnes()
    {
        XamlMarkupExtensionValue binding = Extension("{Binding Customer.Name, Mode=TwoWay}");

        Assert.Equal(2, binding.Arguments.Length);
        Assert.Single(binding.PositionalArguments);

        XamlMarkupExtensionArgument mode = Assert.Single(binding.NamedArguments);

        Assert.Equal("Mode", mode.Name);
        Assert.Equal("TwoWay", ((XamlLiteralValue)mode.Value).Text);
    }

    [Fact]
    public void APositionalArgumentContainingADotIsNotMistakenForANamedOne()
    {
        // The decision cannot be made until an equals sign is found or ruled out.
        XamlMarkupExtensionValue binding = Extension("{Binding Customer.Name}");

        Assert.True(binding.Arguments[0].IsPositional);
        Assert.Null(binding.GetArgument("Customer.Name"));
    }

    [Fact]
    public void NestedExtensionsAreParsedAsValues()
    {
        XamlMarkupExtensionValue binding = Extension(
            "{Binding Value, Converter={StaticResource PriceConverter}}");

        XamlMarkupExtensionArgument converter = Assert.Single(binding.NamedArguments);
        XamlMarkupExtensionValue nested = Assert.IsType<XamlMarkupExtensionValue>(converter.Value);

        Assert.Equal("StaticResource", nested.TypeName.LocalName);
        Assert.Equal("PriceConverter", ((XamlLiteralValue)Assert.Single(nested.Arguments).Value).Text);
    }

    [Fact]
    public void ExtensionsNestThreeDeep()
    {
        XamlMarkupExtensionValue outer = Extension(
            "{Binding A, Converter={StaticResource {x:Static local:Keys.Converter}}}");

        var middle = (XamlMarkupExtensionValue)Assert.Single(outer.NamedArguments).Value;
        var inner = (XamlMarkupExtensionValue)Assert.Single(middle.Arguments).Value;

        Assert.Equal("StaticResource", middle.TypeName.LocalName);
        Assert.Equal("x", inner.TypeName.Prefix);
        Assert.Equal("Static", inner.TypeName.LocalName);
        Assert.Equal("local:Keys.Converter", ((XamlLiteralValue)Assert.Single(inner.Arguments).Value).Text);
    }

    [Fact]
    public void QuotedArgumentsMayContainSeparators()
    {
        XamlMarkupExtensionValue binding = Extension("{Binding 'a, b}c', Mode=OneWay}");

        Assert.Equal("a, b}c", ((XamlLiteralValue)binding.Arguments[0].Value).Text);
        Assert.Equal("OneWay", ((XamlLiteralValue)binding.Arguments[1].Value).Text);
    }

    [Fact]
    public void AnExtensionRendersBackToExactlyWhatWasWritten()
    {
        // Spacing and argument order are the author's, and re-rendering from the parsed parts
        // would quietly reformat every binding in a document that was edited elsewhere.
        const string Written = "{Binding   Customer.Name ,  Mode = TwoWay }";

        Assert.Equal(Written, XamlValue.Parse(Written).ToXamlText());
    }

    [Fact]
    public void AConstructedExtensionRendersFromItsParts()
    {
        var value = new XamlMarkupExtensionValue(
            XamlQualifiedName.Parse("Binding"),
            [new XamlMarkupExtensionArgument(null, new XamlLiteralValue("Name"))]);

        Assert.Equal("{Binding Name}", value.ToXamlText());
    }

    [Fact]
    public void AnUnterminatedExtensionIsReportedAndStillReadable()
    {
        XamlValue value = XamlValue.Parse("{Binding Customer.Name", out ImmutableArray<MarkupDiagnostic> diagnostics);

        Assert.Contains(diagnostics, static d => d.Code == XamlDiagnosticCodes.UnterminatedMarkupExtension);
        Assert.Equal("Binding", ((XamlMarkupExtensionValue)value).TypeName.LocalName);
    }

    [Fact]
    public void AnExtensionWithNoTypeNameIsReported()
    {
        XamlValue.Parse("{}", out ImmutableArray<MarkupDiagnostic> escaped);
        XamlValue.Parse("{ }", out ImmutableArray<MarkupDiagnostic> empty);

        // "{}" is the escape, not a nameless extension.
        Assert.Empty(escaped);
        Assert.Contains(empty, static d => d.Code == XamlDiagnosticCodes.ExpectedMarkupExtensionName);
    }

    [Fact]
    public void AnUnterminatedQuotedArgumentIsReported()
    {
        XamlValue.Parse("{Binding 'unterminated}", out ImmutableArray<MarkupDiagnostic> diagnostics);

        Assert.Contains(diagnostics, static d => d.Code == XamlDiagnosticCodes.UnterminatedQuotedArgument);
    }

    [Fact]
    public void TextAfterAnExtensionIsReportedAndTheWholeValueKept()
    {
        XamlValue value = XamlValue.Parse("{Binding A} trailing", out ImmutableArray<MarkupDiagnostic> diagnostics);

        Assert.Contains(diagnostics, static d => d.Code == XamlDiagnosticCodes.MalformedMarkupExtension);
        Assert.Equal("{Binding A} trailing", value.ToXamlText());
    }

    [Fact]
    public void AnAttributeReadsItsOwnValue()
    {
        XamlDocument document = XamlDocument.Parse(
            "<a Text=\"{Binding Name}\" Width=\"320\" Bare=\"\" />");
        XamlElement element = document.Root!;

        Assert.IsType<XamlMarkupExtensionValue>(element.GetAttribute("Text")!.GetValue());
        Assert.IsType<XamlLiteralValue>(element.GetAttribute("Width")!.GetValue());
        Assert.IsType<XamlLiteralValue>(element.GetAttribute("Bare")!.GetValue());
    }

    [Fact]
    public void AnAttributeWithNoValueAtAllIsUnset()
    {
        XamlDocument document = XamlDocument.Parse("<a bare />");

        Assert.IsType<XamlUnsetValue>(document.Root!.Attributes[0].GetValue());
    }

    [Fact]
    public void EveryAttributeValueInTheFixturesRendersBackUnchanged()
    {
        // Reading a value and writing it back is a no-op across every real document here.
        foreach (string name in Fixtures.Names)
        {
            XamlDocument document = Fixtures.Parse(name);

            foreach (XamlAttribute attribute in document.DescendantElements().SelectMany(static e => e.Attributes))
            {
                if (attribute.HasValue)
                {
                    Assert.Equal(attribute.GetValueText(), attribute.GetValue().ToXamlText());
                }
            }
        }
    }
}
