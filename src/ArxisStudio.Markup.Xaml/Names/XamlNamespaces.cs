namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Namespace URIs that XAML gives a defined meaning to.
/// </summary>
/// <remarks>
/// Always compare namespaces by these URIs, never by prefix. Nothing obliges a document to
/// spell them <c>x</c>, <c>d</c> or <c>mc</c>, and a document that spells them differently is
/// perfectly valid.
/// </remarks>
public static class XamlNamespaces
{
    /// <summary>The XAML language namespace, conventionally bound to the <c>x</c> prefix.</summary>
    public const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The design-time namespace, conventionally bound to the <c>d</c> prefix.</summary>
    public const string Design = "http://schemas.microsoft.com/expression/blend/2008";

    /// <summary>The markup-compatibility namespace, conventionally bound to the <c>mc</c> prefix.</summary>
    public const string MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>The namespace of the <c>xmlns</c> attributes themselves, fixed by the XML specification.</summary>
    public const string Xmlns = "http://www.w3.org/2000/xmlns/";

    /// <summary>The <c>xml</c> namespace, fixed by the XML specification.</summary>
    public const string Xml = "http://www.w3.org/XML/1998/namespace";
}
