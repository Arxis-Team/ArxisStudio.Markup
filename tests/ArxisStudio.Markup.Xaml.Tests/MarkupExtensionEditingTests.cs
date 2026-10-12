using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// One argument of an extension set where it stands: the rest of the expression keeps its text.
/// </summary>
public sealed class MarkupExtensionEditingTests
{
    private static XamlMarkupExtensionValue Extension(string text) =>
        Assert.IsType<XamlMarkupExtensionValue>(XamlValue.Parse(text));

    private static XamlLiteralValue Literal(string text) => new(text);

    [Fact]
    public void ReplacingAnArgumentLeavesTheOthersAsWritten()
    {
        XamlMarkupExtensionValue binding = Extension("{Binding  Name ,  Mode=OneWay,Converter={StaticResource  Upper}}");

        Assert.Equal(
            "{Binding  Title ,  Mode=OneWay,Converter={StaticResource  Upper}}",
            binding.WithArgument(null, Literal("Title")).ToXamlText());
        Assert.Equal(
            "{Binding  Name ,  Mode=TwoWay,Converter={StaticResource  Upper}}",
            binding.WithArgument("Mode", Literal("TwoWay")).ToXamlText());
    }

    [Theory]
    [InlineData("Mode", "{Binding Name, Delay=100}")]
    [InlineData("Delay", "{Binding Name, Mode=TwoWay}")]
    [InlineData(null, "{Binding Mode=TwoWay, Delay=100}")]
    public void RemovingAnArgumentTakesItsSeparator(string? name, string expected)
    {
        Assert.Equal(expected, Extension("{Binding Name, Mode=TwoWay, Delay=100}").WithArgument(name, null).ToXamlText());
    }

    [Fact]
    public void RemovingTheOnlyArgumentLeavesTheBareExtension()
    {
        Assert.Equal("{Binding}", Extension("{Binding Name}").WithArgument(null, null).ToXamlText());
    }

    [Fact]
    public void RemovingAnArgumentThatIsNotThereChangesNothing()
    {
        XamlMarkupExtensionValue binding = Extension("{Binding Name}");

        Assert.Same(binding, binding.WithArgument("Mode", null));
    }

    [Fact]
    public void ANamedArgumentGoesLastAndAPositionalOneFirst()
    {
        Assert.Equal("{Binding Name, Mode=TwoWay}", Extension("{Binding Name}").WithArgument("Mode", Literal("TwoWay")).ToXamlText());
        Assert.Equal("{Binding Name, Mode=TwoWay}", Extension("{Binding Mode=TwoWay}").WithArgument(null, Literal("Name")).ToXamlText());
        Assert.Equal("{Binding Name}", Extension("{Binding}").WithArgument(null, Literal("Name")).ToXamlText());
        Assert.Equal("{Binding Mode=OneTime}", Extension("{Binding }").WithArgument("Mode", Literal("OneTime")).ToXamlText());
    }

    [Theory]
    [InlineData("{0:N2}", "'{}{0:N2}'")]
    [InlineData("Итого: {0:N1}", "'Итого: {0:N1}'")]
    [InlineData("a, b", "'a, b'")]
    [InlineData("x=y", "'x=y'")]
    [InlineData(" padded", "' padded'")]
    [InlineData("", "''")]
    [InlineData("it's", "\"it's\"")]
    [InlineData("{0}'s", "\"{}{0}'s\"")]
    [InlineData("plain", "plain")]
    public void ALiteralABareArgumentWouldMisreadIsQuoted(string text, string written)
    {
        XamlMarkupExtensionValue binding = Extension("{Binding Name}").WithArgument("StringFormat", Literal(text));

        Assert.Equal("{Binding Name, StringFormat=" + written + "}", binding.ToXamlText());
        Assert.Equal(text, Assert.IsType<XamlLiteralValue>(binding.GetArgument("StringFormat")!.Value).Text);
    }

    [Fact]
    public void AQuotedArgumentReadsWithoutItsBraceEscape()
    {
        // Avalonia reads '{0:N2}' as an extension, so a quoted format that starts with a brace is escaped — and the
        // escape is how it is written, as it is for a whole attribute.
        XamlMarkupExtensionValue binding = Extension("{Binding Price, StringFormat='{}{0:N2}'}");

        Assert.Equal("{0:N2}", Assert.IsType<XamlLiteralValue>(binding.GetArgument("StringFormat")!.Value).Text);
        Assert.Equal("{Binding Price, StringFormat='{}{0:N2}'}", binding.ToXamlText());
    }

    [Fact]
    public void ALiteralHoldingBothQuotesCannotBeWritten()
    {
        Assert.Throws<ArgumentException>(() => Extension("{Binding Name}").WithArgument("StringFormat", Literal("it's \"x\"")));
    }

    [Fact]
    public void ANestedExtensionIsWrittenAsAnExtension()
    {
        XamlMarkupExtensionValue binding = Extension("{Binding Name}")
            .WithArgument("Converter", XamlValue.Parse("{x:Static BoolConverters.Not}"));

        Assert.Equal("{Binding Name, Converter={x:Static BoolConverters.Not}}", binding.ToXamlText());
        Assert.IsType<XamlMarkupExtensionValue>(binding.GetArgument("Converter")!.Value);
    }

    [Fact]
    public void AQuotedArgumentNobodyTouchedKeepsItsQuotes()
    {
        XamlMarkupExtensionValue binding = Extension("{Binding Price, StringFormat='{}{0:N2}', Mode=OneWay}");

        Assert.Equal(
            "{Binding Price, StringFormat='{}{0:N2}', Mode=OneTime}",
            binding.WithArgument("Mode", Literal("OneTime")).ToXamlText());
    }

    [Fact]
    public void TheResultIsParsedSoEditsChain()
    {
        XamlMarkupExtensionValue binding = Extension("{Binding Name}")
            .WithArgument("Mode", Literal("TwoWay"))
            .WithArgument(null, Literal("Customer.Name"))
            .WithArgument("Delay", Literal("250"))
            .WithArgument("Mode", null);

        Assert.Equal("{Binding Customer.Name, Delay=250}", binding.ToXamlText());
        Assert.Equal(["Customer.Name", "250"], binding.Arguments.Select(static argument => ((XamlLiteralValue)argument.Value).Text));
    }

    [Fact]
    public void AConstructedExtensionIsWrittenAnewWithItsLiteralsQuoted()
    {
        var built = new XamlMarkupExtensionValue(
            XamlQualifiedName.Unprefixed("Binding"),
            [new XamlMarkupExtensionArgument(null, Literal("Name"))]);

        Assert.Equal("{Binding Name, StringFormat='a, b'}", built.WithArgument("StringFormat", Literal("a, b")).ToXamlText());
        Assert.Equal(
            "StringFormat='{}{0}, {1}'",
            new XamlMarkupExtensionArgument("StringFormat", Literal("{0}, {1}")).ToString());
    }

    [Fact]
    public void AnEmptyNameIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Extension("{Binding Name}").WithArgument(string.Empty, Literal("x")));
    }

    [Fact]
    public void EveryNestedArgumentInTheFixturesWritesBackWithoutTouchingTheText()
    {
        // A nested extension is written as its own text, so setting it to itself is a no-op across every real
        // document here — and so is every argument the edit did not touch, spacing and quotes included.
        foreach (string name in Fixtures.Names)
        {
            foreach (XamlAttribute attribute in Fixtures.Parse(name).DescendantElements().SelectMany(static e => e.Attributes))
            {
                if (!attribute.HasValue || attribute.GetValue() is not XamlMarkupExtensionValue extension)
                {
                    continue;
                }

                XamlMarkupExtensionArgument? positional = extension.Arguments.FirstOrDefault(static argument => argument.IsPositional);

                foreach (XamlMarkupExtensionArgument argument in extension.Arguments)
                {
                    if (argument.Value is not XamlMarkupExtensionValue || (argument.IsPositional && !ReferenceEquals(argument, positional)))
                    {
                        continue;
                    }

                    Assert.Equal(attribute.GetValueText(), extension.WithArgument(argument.Name, argument.Value).ToXamlText());
                }
            }
        }
    }
}
