using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// The exit criteria of this milestone: the property matrix, diagnostics for read-only members,
/// and bindings that survive editing elsewhere.
/// </summary>
public sealed class PropertyMatrixTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string TestControlsNamespace = "https://arxis.studio/test-controls";

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault([typeof(MemberMatrixControl).Assembly], new InMemoryMarkupSourceProvider());

    private static ValueTask<XamlLoadSession> Load(string xaml) =>
        XamlLoadSession.CreateAsync(
            XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = new Uri("file:///Views/Matrix.axaml") }),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

    private static ValueTask<XamlLoadSession> LoadMatrix(string attributes = "") =>
        Load($"<local:MemberMatrixControl xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"{TestControlsNamespace}\"\n" +
             $"                           {attributes} />");

    [AvaloniaTheory]
    [InlineData(nameof(MemberMatrixControl.Label), XamlMemberKind.StyledProperty, true)]
    [InlineData(nameof(MemberMatrixControl.Counter), XamlMemberKind.DirectProperty, true)]
    [InlineData(nameof(MemberMatrixControl.Computed), XamlMemberKind.DirectProperty, false)]
    [InlineData(nameof(MemberMatrixControl.Note), XamlMemberKind.ClrProperty, true)]
    [InlineData(nameof(MemberMatrixControl.Items), XamlMemberKind.Collection, false)]
    public async Task EveryKindOfMemberIsClassifiedAndItsWritabilityReported(
        string name, XamlMemberKind kind, bool canWrite)
    {
        await using XamlLoadSession session = await LoadMatrix();

        XamlMemberDescriptor member = session.GetMember(session.RootObject, name);

        Assert.Equal(kind, member.Kind);
        Assert.Equal(canWrite, member.CanWrite);
        Assert.Equal(!canWrite, member.IsReadOnly);
        Assert.True(member.IsResolved);
    }

    [AvaloniaFact]
    public async Task AnAttachedPropertyIsClassifiedAsAttached()
    {
        await using XamlLoadSession session = await LoadMatrix();

        XamlMemberDescriptor member = session.GetMember(
            session.RootObject, $"{nameof(MemberMatrixControl)}.Slot");

        Assert.Equal(XamlMemberKind.AttachedProperty, member.Kind);
        Assert.True(member.IsAttached);
        Assert.Equal(typeof(int), member.ValueType);
        Assert.Equal(typeof(MemberMatrixControl), member.DeclaringType);
    }

    [AvaloniaFact]
    public async Task AnEventIsClassifiedAsAnEvent()
    {
        await using XamlLoadSession session = await Load(
            $"<Button xmlns=\"{AvaloniaNamespace}\" />");

        XamlMemberDescriptor member = session.GetMember(session.RootObject, nameof(Button.Click));

        Assert.Equal(XamlMemberKind.Event, member.Kind);
        Assert.NotNull(member.Event);
        Assert.False(member.CanRead);
    }

    [AvaloniaFact]
    public async Task ContentIsClassifiedAsContent()
    {
        await using XamlLoadSession session = await Load($"<Button xmlns=\"{AvaloniaNamespace}\" />");

        Assert.Equal(XamlMemberKind.Content, session.GetMember(session.RootObject, nameof(Button.Content)).Kind);
    }

    [AvaloniaFact]
    public async Task AnUnknownMemberResolvesToUnknownRatherThanThrowing()
    {
        await using XamlLoadSession session = await LoadMatrix();

        XamlMemberDescriptor member = session.GetMember(session.RootObject, "NotAMemberOfAnything");

        Assert.Equal(XamlMemberKind.Unknown, member.Kind);
        Assert.False(member.IsResolved);
    }

    [AvaloniaFact]
    public async Task EveryDescriptorReportsTheTypesTheContractRequires()
    {
        await using XamlLoadSession session = await LoadMatrix();

        XamlMemberDescriptor member = session.GetMember(session.RootObject, nameof(MemberMatrixControl.Label));

        Assert.Equal(typeof(MemberMatrixControl), member.DeclaringType);
        Assert.Equal(typeof(MemberMatrixControl), member.TargetType);
        Assert.Equal(typeof(string), member.ValueType);
        Assert.True(member.CanRead);
        Assert.NotNull(member.AvaloniaProperty);
    }

    [AvaloniaFact]
    public async Task SettingAStyledPropertyChangesTheObjectAndTheDocument()
    {
        await using XamlLoadSession session = await LoadMatrix("Label=\"before\"");
        var control = session.GetRoot<MemberMatrixControl>();

        XamlEditResult result = session.SetValue(control, MemberMatrixControl.LabelProperty, "after");

        Assert.True(result.Applied);
        Assert.False(result.HasErrors);
        Assert.Equal("after", control.Label);
        Assert.Contains("Label=\"after\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SettingADirectPropertyWorksToo()
    {
        await using XamlLoadSession session = await LoadMatrix("Counter=\"1\"");
        var control = session.GetRoot<MemberMatrixControl>();

        Assert.True(session.SetValue(control, MemberMatrixControl.CounterProperty, 7).Applied);
        Assert.Equal(7, control.Counter);
        Assert.Contains("Counter=\"7\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AReadOnlyPropertyProducesADiagnosticInsteadOfBeingWritten()
    {
        await using XamlLoadSession session = await LoadMatrix();
        var control = session.GetRoot<MemberMatrixControl>();

        XamlEditResult result = session.SetValue(control, MemberMatrixControl.ComputedProperty, "nope");

        Assert.False(result.Applied);
        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.ReadOnlyMember);
    }

    [AvaloniaFact]
    public async Task AValueTheTypeRejectsProducesADiagnosticAndLeavesBothSidesAlone()
    {
        await using XamlLoadSession session = await LoadMatrix("Counter=\"1\"");
        var control = session.GetRoot<MemberMatrixControl>();
        string before = session.Document.GetText();

        XamlEditResult result = session.SetValue(control, MemberMatrixControl.CounterProperty, "not a number");

        Assert.False(result.Applied);
        Assert.Equal(1, control.Counter);
        Assert.Equal(before, session.Document.GetText());
    }

    [AvaloniaFact]
    public async Task AnAttachedPropertyCanBeSetThroughTheSession()
    {
        await using XamlLoadSession session = await LoadMatrix();
        var control = session.GetRoot<MemberMatrixControl>();

        Assert.True(session.SetValue(control, MemberMatrixControl.SlotProperty, 4).Applied);
        Assert.Equal(4, MemberMatrixControl.GetSlot(control));
    }

    [AvaloniaFact]
    public async Task ABindingIsReportedAsABindingRatherThanAsItsValue()
    {
        await using XamlLoadSession session = await Load(
            $"<TextBlock xmlns=\"{AvaloniaNamespace}\" Text=\"{{Binding Missing}}\" />");

        var text = session.GetRoot<TextBlock>();
        XamlValueInfo info = session.GetValueInfo(text, TextBlock.TextProperty);

        Assert.True(info.HasBinding);
        Assert.Equal(XamlValueSource.Binding, info.Source);
        Assert.IsType<XamlMarkupExtensionValue>(info.SourceValue);
        Assert.True(info.WouldDestroyExpression);
    }

    [AvaloniaFact]
    public async Task ALiteralIsReportedAsALocalValue()
    {
        await using XamlLoadSession session = await Load(
            $"<TextBlock xmlns=\"{AvaloniaNamespace}\" Text=\"plain\" />");

        XamlValueInfo info = session.GetValueInfo(session.GetRoot<TextBlock>(), TextBlock.TextProperty);

        Assert.False(info.HasBinding);
        Assert.Equal(XamlValueSource.Local, info.Source);
        Assert.Equal("plain", Assert.IsType<XamlLiteralValue>(info.SourceValue).Text);
        Assert.False(info.WouldDestroyExpression);
    }

    [AvaloniaFact]
    public async Task AnUnmentionedPropertyReportsItsSourceValueAsUnset()
    {
        await using XamlLoadSession session = await Load($"<TextBlock xmlns=\"{AvaloniaNamespace}\" />");

        XamlValueInfo info = session.GetValueInfo(session.GetRoot<TextBlock>(), TextBlock.TextProperty);

        Assert.IsType<XamlUnsetValue>(info.SourceValue);
    }

    [AvaloniaFact]
    public async Task EditingOnePropertyLeavesABindingOnAnotherUntouched()
    {
        // The exit criterion in miniature: nothing writes an effective value back on its own.
        await using XamlLoadSession session = await Load(
            $"<TextBlock xmlns=\"{AvaloniaNamespace}\" Text=\"{{Binding Customer.Name}}\" Width=\"100\" />");

        var text = session.GetRoot<TextBlock>();

        Assert.True(session.SetValue(text, Layoutable.WidthProperty, 320d).Applied);

        string saved = session.Document.GetText();

        Assert.Contains("Text=\"{Binding Customer.Name}\"", saved, StringComparison.Ordinal);
        Assert.Contains("Width=\"320\"", saved, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ReplacingABindingIsAllowedButReported()
    {
        // A caller may mean exactly this, so it is not refused — but it must never happen
        // unnoticed.
        await using XamlLoadSession session = await Load(
            $"<TextBlock xmlns=\"{AvaloniaNamespace}\" Text=\"{{Binding Customer.Name}}\" />");

        XamlEditResult result = session.SetValue(
            session.GetRoot<TextBlock>(), TextBlock.TextProperty, "literal now");

        Assert.True(result.Applied);
        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.ExpressionReplaced
                && d.Severity == MarkupDiagnosticSeverity.Warning);
    }

    [AvaloniaFact]
    public async Task ABindingCanBeSetAsABindingRatherThanAsText()
    {
        await using XamlLoadSession session = await Load(
            $"<TextBlock xmlns=\"{AvaloniaNamespace}\" Text=\"plain\" />");

        XamlEditResult result = session.SetXamlValue(
            session.GetRoot<TextBlock>(),
            TextBlock.TextProperty,
            XamlValue.Parse("{Binding Customer.Name}"));

        Assert.True(result.Applied);
        Assert.Contains("Text=\"{Binding Customer.Name}\"", session.Document.GetText(), StringComparison.Ordinal);

        // The object is not given the expression's text as a value; the next load resolves it.
        Assert.NotEqual("{Binding Customer.Name}", session.GetRoot<TextBlock>().Text);
        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.ExpressionNotApplied);
    }

    [AvaloniaFact]
    public async Task ObjectsAreMappedBackToTheElementsThatDeclaredThem()
    {
        await using XamlLoadSession session = await Load(
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <TextBlock Text=\"one\" />\n" +
            "  <Button Content=\"two\" />\n" +
            "</StackPanel>");

        var panel = session.GetRoot<StackPanel>();

        foreach (Control child in panel.Children)
        {
            XamlElement? element = session.GetElement(child);

            Assert.NotNull(element);
            Assert.Equal(child.GetType().Name, element.Name.LocalName);
            Assert.Equal(XamlObjectOrigin.Document, session.GetOrigin(child));
            Assert.Same(child, session.GetObject(element));
        }
    }

    [AvaloniaFact]
    public async Task AnObjectNothingDeclaredHasNoElement()
    {
        await using XamlLoadSession session = await Load($"<Button xmlns=\"{AvaloniaNamespace}\" />");

        var created = new Button();

        Assert.Null(session.GetElement(created));
        Assert.Equal(XamlObjectOrigin.RuntimeGenerated, session.GetOrigin(created));
    }

    [AvaloniaFact]
    public async Task EditingAnObjectWithNoDeclarationTouchesOnlyTheObject()
    {
        await using XamlLoadSession session = await Load($"<Button xmlns=\"{AvaloniaNamespace}\" />");

        var created = new Button();
        string before = session.Document.GetText();

        XamlEditResult result = session.SetValue(created, Layoutable.WidthProperty, 100d);

        Assert.True(result.Applied);
        Assert.Equal(100d, created.Width);
        Assert.Equal(before, session.Document.GetText());
        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.NoSourceDeclaration);
    }

    [AvaloniaFact]
    public async Task TheDocumentAdvancesAsEditsAreApplied()
    {
        await using XamlLoadSession session = await LoadMatrix("Label=\"one\"");
        XamlDocument before = session.Document;

        session.SetValue(session.GetRoot<MemberMatrixControl>(), MemberMatrixControl.LabelProperty, "two");

        Assert.NotSame(before, session.Document);
        Assert.Contains("Label=\"one\"", before.GetText(), StringComparison.Ordinal);
        Assert.Contains("Label=\"two\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnEditThatChangesTheLengthOfALineDoesNotMoveLaterElements()
    {
        // Rebuilding the map after an edit reads positions recorded against the text as it was
        // when the objects were built. Resolving those against the document as it is now has to
        // survive an edit that lengthened an earlier line, or the next edit writes itself into
        // whatever element the stale offset happens to land in.
        await using XamlLoadSession session = await Load(
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <TextBlock Text=\"a\" />\n" +
            "  <Button Content=\"go\" />\n" +
            "</StackPanel>");

        var panel = session.GetRoot<StackPanel>();
        var text = (TextBlock)panel.Children[0];
        var button = (Button)panel.Children[1];

        Assert.True(session.SetValue(text, TextBlock.TextProperty, "a much longer value").Applied);
        Assert.True(session.SetValue(button, ContentControl.ContentProperty, "stop").Applied);

        string saved = session.Document.GetText();

        Assert.Contains("<TextBlock Text=\"a much longer value\" />", saved, StringComparison.Ordinal);
        Assert.Contains("<Button Content=\"stop\" />", saved, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TwoEditsInSequenceBothLand()
    {
        // The map is rebuilt after each edit, so the second edit's element is not stale.
        await using XamlLoadSession session = await LoadMatrix("Label=\"one\" Counter=\"1\"");
        var control = session.GetRoot<MemberMatrixControl>();

        Assert.True(session.SetValue(control, MemberMatrixControl.LabelProperty, "two").Applied);
        Assert.True(session.SetValue(control, MemberMatrixControl.CounterProperty, 9).Applied);

        string saved = session.Document.GetText();

        Assert.Contains("Label=\"two\"", saved, StringComparison.Ordinal);
        Assert.Contains("Counter=\"9\"", saved, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task MembersOfATypeCanBeListedRatherThanGuessed()
    {
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />");

        var border = session.GetRoot<Border>();
        ImmutableArray<XamlMemberDescriptor> members = session.GetMembers(border);

        Assert.Contains(members, member => member.Name == "Width" && member.CanWrite);
        Assert.Contains(members, member => member.Name == "Padding");

        // Attached members are written Owner.Member and are not registered on the type that
        // carries them, so a list built from the simple names alone would miss every one. They
        // also only exist once their owner has been initialised, which is why this touches Grid
        // before asking and why the answer is not cached.
        _ = Grid.RowProperty;

        Assert.Contains(
            session.GetMembers(border), member => member.Name == "Grid.Row" && member.IsAttached);

        // Ordered and without duplicates, so a panel can show them as they come.
        Assert.Equal(members, members.OrderBy(static member => member.Name, StringComparer.Ordinal));
        Assert.Equal(members.Length, members.Select(static member => member.Name).Distinct().Count());
    }
    [AvaloniaFact]
    public async Task AValueWrittenAsTextReachesTheSameValueTheDocumentLoadedWith()
    {
        // Thickness carries no TypeConverter: it is read by the XAML compiler through its own
        // Parse, and an update that did not do the same would hand the setter a string.
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\" Margin=\"1\" />");

        var border = session.GetRoot<Border>();

        XamlDocument updated = session.Document.SetAttribute(
            session.Document.Root!, XamlQualifiedName.Parse("Margin"), "6,0,4,0");

        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            updated, TestContext.Current.CancellationToken);

        Assert.True(result.Applied);
        Assert.Equal(new Thickness(6, 0, 4, 0), border.Margin);
    }

    [AvaloniaFact]
    public async Task TextTheMemberCannotHoldIsReportedRatherThanThrown()
    {
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\" Margin=\"1\" />");

        var border = session.GetRoot<Border>();

        XamlDocument updated = session.Document.SetAttribute(
            session.Document.Root!, XamlQualifiedName.Parse("Margin"), "not a thickness");

        // Half-typed text in an inspector is an ordinary user error, and the contract is that
        // those are diagnostics with a span rather than exceptions out of an update.
        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            updated, TestContext.Current.CancellationToken);

        Assert.False(result.Applied);
        Assert.Contains(result.Diagnostics, static d => d.Severity == MarkupDiagnosticSeverity.Error);

        // And the objects are left as they were, so the tree still matches the document the
        // session is still holding.
        Assert.Equal(new Thickness(1), border.Margin);
    }
    [AvaloniaFact]
    public async Task TextARefusedParseChokesOnIsReportedRatherThanThrown()
    {
        // PathFigures has no TypeConverter and its Parse raises InvalidDataException rather than
        // the FormatException most types raise. A conversion that listed the exceptions it knew
        // about would let this one out of a public async API.
        await using XamlLoadSession session = await Load(
            $"<PathGeometry xmlns=\"{AvaloniaNamespace}\" Figures=\"M 0,0 L 10,10\" />");

        XamlDocument updated = session.Document.SetAttribute(
            session.Document.Root!, XamlQualifiedName.Parse("Figures"), "certainly not a path");

        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            updated, TestContext.Current.CancellationToken);

        Assert.False(result.Applied);
        Assert.Contains(result.Diagnostics, static d => d.Severity == MarkupDiagnosticSeverity.Error);
    }



}
