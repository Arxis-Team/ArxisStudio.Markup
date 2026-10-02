using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// What the syntax package writes when markup crosses documents is markup Avalonia loads — and
/// loads as meaning what it meant where it came from.
/// </summary>
/// <remarks>
/// The syntax tests say the text came out as intended. Whether the intended text is right is a
/// question only the loader can answer: a rename that missed one place, or a declaration put
/// where Avalonia does not accept one, reads perfectly well and fails here.
/// </remarks>
public sealed class FragmentLoadTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";

    [AvaloniaTheory]
    [InlineData(XamlLoadMode.Runtime)]
    [InlineData(XamlLoadMode.Design)]
    public async Task AFragmentRenamedIntoAnotherDocumentLoadsAndMeansWhatItMeant(XamlLoadMode mode)
    {
        var source = XamlDocument.Parse(
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
            "            xmlns:sys=\"using:System\"\n" +
            $"            xmlns:d=\"{XamlNamespaces.Design}\" xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\"\n" +
            "            mc:Ignorable=\"d\">\n" +
            "  <TextBlock Text=\"{x:Static sys:Environment.NewLine}\" Tag=\"{x:Type sys:Int32}\" d:Text=\"sample\" />\n" +
            "</StackPanel>");

        // sys means something else here, so the fragment's has to become another prefix — and d
        // has to arrive ignorable, or Avalonia refuses the whole document over d:Text.
        var target = XamlDocument.Parse(
            $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:sys=\"using:System.IO\">\n" +
            "  <StackPanel />\n" +
            "</Border>");

        XamlDocumentEditor editor = target.Edit().InsertFragment(
            target.DescendantElements().Single(static e => e.Name.LocalName == "StackPanel"),
            0,
            XamlFragment.From(source.DescendantElements().Single(static e => e.Name.LocalName == "TextBlock")));

        Assert.Empty(editor.Diagnostics);

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            editor.Apply(),
            XamlLoadEnvironment.CreateDefault(),
            new XamlLoadOptions { Mode = mode },
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(session.Diagnostics, static d => d.IsError);

        var text = (TextBlock)((StackPanel)session.GetRoot<Border>().Child!).Children.Single();

        Assert.Equal(typeof(int), text.Tag);

        // In design mode the design value wins, which is what it is for; at run time the static
        // value the renamed prefix still has to reach.
        Assert.Equal(mode == XamlLoadMode.Design ? "sample" : Environment.NewLine, text.Text);
    }
}
