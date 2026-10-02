using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// What a binding can read from its source — the members a data panel lists, the path a binding
/// writes, and the data type and design data in scope where it is written.
/// </summary>
public sealed class BindingSourceTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly XamlMemberResolver Members = new();

    [Fact]
    public void ASourceListsWhatABindingCanReadOnIt()
    {
        Assert.Equal(
            [
                new XamlBindableMember("Address", "SourceAddress", CanWrite: false, IsCollection: false, IsCommand: false),
                new XamlBindableMember("Age", "int?", CanWrite: false, IsCollection: false, IsCommand: false),
                new XamlBindableMember("Lines", "string[]", CanWrite: false, IsCollection: true, IsCommand: false),
                new XamlBindableMember("Name", "string", CanWrite: true, IsCollection: false, IsCommand: false),
                new XamlBindableMember("Orders", "List<SourceOrder>", CanWrite: false, IsCollection: true, IsCommand: false),
                new XamlBindableMember("Save", "ICommand", CanWrite: false, IsCollection: false, IsCommand: true),
                new XamlBindableMember("Tag", "object", CanWrite: true, IsCollection: false, IsCommand: false),
            ],
            Members.EnumerateBindable(typeof(SourceCustomer)));
    }

    [Fact]
    public void AHidingMemberIsListedOnceAsTheDerivedTypeDeclaresIt()
    {
        // Hidden with a type of its own, which is when reflection answers both declarations.
        XamlBindableMember name = Members.EnumerateBindable(typeof(SourceVip)).Single(static member => member.Name == "Name");

        Assert.Equal("int", name.TypeName);
        Assert.False(name.CanWrite);
        Assert.Equal(typeof(int), Members.ResolveBindingPath(typeof(SourceVip), "Name").ResultType);
    }

    [Fact]
    public void AnInterfaceListsWhatTheInterfacesItExtendsDeclare()
    {
        Assert.Equal(["Age", "Name"], Members.EnumerateBindable(typeof(ISourcePerson)).Select(static member => member.Name));
    }

    [Theory]
    [InlineData("", typeof(SourceCustomer))]
    [InlineData(".", typeof(SourceCustomer))]
    [InlineData("Name", typeof(string))]
    [InlineData("Name.Length", typeof(int))]
    [InlineData("Address.City", typeof(string))]
    [InlineData("Orders[0].Total", typeof(decimal))]
    [InlineData("Lines[2]", typeof(string))]
    [InlineData("[key]", typeof(string))]
    [InlineData("!Name", typeof(bool))]
    [InlineData(" Address . City ", typeof(string))]
    public void APathEveryStepOfWhichIsThereResolvesToWhereItEnds(string path, Type result)
    {
        XamlBindingPathResult resolved = Members.ResolveBindingPath(typeof(SourceCustomer), path);

        Assert.Equal(XamlBindingPathStatus.Resolved, resolved.Status);
        Assert.Equal(result, resolved.ResultType);
    }

    [Theory]
    [InlineData("Nmae", "Nmae")]
    [InlineData("Address.Cty", "Cty")]
    [InlineData("Orders[0].Totl", "Totl")]
    [InlineData("Ordrs[0].Total", "Ordrs")]
    public void AStepThatNamesNothingBreaksThePathThere(string path, string step)
    {
        XamlBindingPathResult broken = Members.ResolveBindingPath(typeof(SourceCustomer), path);

        Assert.Equal(XamlBindingPathStatus.Broken, broken.Status);
        Assert.Equal(step, broken.Step);
        Assert.Null(broken.ResultType);
        Assert.Contains($"'{step}'", broken.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Tag.Anything")]
    [InlineData("(Grid.Row)")]
    [InlineData("$parent.Name")]
    [InlineData("#Title.Text")]
    [InlineData("Address[0]")]
    [InlineData("Name[")]
    public void APathThisReadingDoesNotFollowIsNeitherResolvedNorBroken(string path)
    {
        // An object-typed step is read when the binding runs, and the rest names sources a type
        // cannot answer for: claiming a break would be claiming something unknown.
        Assert.Equal(
            XamlBindingPathStatus.NotUnderstood,
            Members.ResolveBindingPath(typeof(SourceCustomer), path).Status);
    }

    [AvaloniaFact]
    public async Task AnElementSaysWhichDataTypeAndWhichDesignDataItsBindingsRead()
    {
        string text =
            $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:tc=\"{ControlsNamespace}\"\n" +
            "             x:DataType=\"tc:GreetingModel\" x:CompileBindings=\"False\">\n" +
            "  <Design.DataContext>\n" +
            "    <tc:GreetingModel />\n" +
            "  </Design.DataContext>\n" +
            "  <StackPanel>\n" +
            "    <TextBlock x:Name=\"Title\" Text=\"{Binding Name}\" />\n" +
            "    <Border x:DataType=\"{x:Type tc:CustomBadge}\" x:CompileBindings=\"True\">\n" +
            "      <TextBlock x:Name=\"Inner\" />\n" +
            "    </Border>\n" +
            "  </StackPanel>\n" +
            "</UserControl>\n";

        XamlLoadSession session = await XamlLoadSession.CreateAsync(
            XamlDocument.Parse(text, new XamlParseOptions { DocumentUri = new Uri("file:///Views/Data.axaml") }),
            XamlLoadEnvironment.CreateDefault([typeof(GreetingModel).Assembly], new InMemoryMarkupSourceProvider()),
            new XamlLoadOptions { Mode = XamlLoadMode.Design, UseCompiledBindingsByDefault = true },
            TestContext.Current.CancellationToken);

        await using (session)
        {
            XamlElement Named(string name) =>
                session.Document.DescendantElements().Single(element => element.Identity == name);

            XamlDataContextInfo title = await session.GetDataContextAsync(Named("Title"), TestContext.Current.CancellationToken);

            Assert.Same(session.Document.Root, title.DataTypeElement);
            Assert.Equal("tc:GreetingModel", title.WrittenDataType);
            Assert.Equal(typeof(GreetingModel), title.DataType);
            Assert.Equal(typeof(GreetingModel), title.DesignDataContextType);
            Assert.False(title.CompilesBindings);

            // The nearest of each wins, written either way.
            XamlDataContextInfo inner = await session.GetDataContextAsync(Named("Inner"), TestContext.Current.CancellationToken);

            Assert.Equal("Border", inner.DataTypeElement!.Name.LocalName);
            Assert.Equal(typeof(CustomBadge), inner.DataType);
            Assert.True(inner.CompilesBindings);

            // The session's default where nothing is written, and nothing in scope where nothing is.
            XamlLoadSession plain = await XamlLoadSession.CreateAsync(
                XamlDocument.Parse($"<StackPanel xmlns=\"{AvaloniaNamespace}\"><TextBlock /></StackPanel>"),
                XamlLoadEnvironment.CreateDefault(),
                new XamlLoadOptions { Mode = XamlLoadMode.Design, UseCompiledBindingsByDefault = true },
                TestContext.Current.CancellationToken);

            await using (plain)
            {
                XamlDataContextInfo none = await plain.GetDataContextAsync(
                    plain.Document.Root!.ContentElements.Single(), TestContext.Current.CancellationToken);

                Assert.Null(none.DataTypeElement);
                Assert.Null(none.DataType);
                Assert.Null(none.DesignDataContextType);
                Assert.True(none.CompilesBindings);
            }

            // An element of another parse is refused rather than answered for.
            await Assert.ThrowsAsync<ArgumentException>(() => session.GetDataContextAsync(
                XamlDocument.Parse(text).Root!, TestContext.Current.CancellationToken).AsTask());
        }
    }
}

/// <summary>A source a binding reads.</summary>
public class SourceCustomer
{
    /// <summary>Gets or sets the name.</summary>
    public virtual string Name { get; set; } = "Ada";

    /// <summary>Gets the age, when it is known.</summary>
    public int? Age { get; }

    /// <summary>Gets the address.</summary>
    public SourceAddress Address { get; } = new();

    /// <summary>Gets the orders.</summary>
    public List<SourceOrder> Orders { get; } = [];

    /// <summary>Gets the lines of the address label.</summary>
    public string[] Lines { get; } = [];

    /// <summary>Gets a command.</summary>
    public ICommand? Save { get; }

    /// <summary>Gets or sets anything at all.</summary>
    public object? Tag { get; set; }

    /// <summary>Reads a value by key.</summary>
    /// <param name="key">The key.</param>
    public string this[string key] => key;
}

/// <summary>A customer whose name hides the base one.</summary>
public sealed class SourceVip : SourceCustomer
{
    /// <summary>Gets the length of the name, which hides the name and cannot be written.</summary>
    public new int Name => base.Name.Length;
}

/// <summary>An address.</summary>
public sealed class SourceAddress
{
    /// <summary>Gets or sets the city.</summary>
    public string City { get; set; } = string.Empty;
}

/// <summary>An order.</summary>
public sealed class SourceOrder
{
    /// <summary>Gets or sets the total.</summary>
    public decimal Total { get; set; }
}

/// <summary>Something with a name.</summary>
public interface ISourceNamed
{
    /// <summary>Gets the name.</summary>
    string Name { get; }
}

/// <summary>A person, which has a name.</summary>
public interface ISourcePerson : ISourceNamed
{
    /// <summary>Gets the age.</summary>
    int Age { get; }
}
