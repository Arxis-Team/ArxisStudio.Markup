using Avalonia.Metadata;

// Maps a XAML namespace URI onto this assembly's CLR namespace, which is how a document can
// name these controls through a mapped namespace rather than a "using:" one. The type resolver
// reads exactly this attribute, so the mapping is exercised rather than assumed.
[assembly: XmlnsDefinition("https://arxis.studio/test-controls", "ArxisStudio.Markup.Xaml.Loader.TestControls")]

// And the prefix a tool should declare it under, which a type catalog reads the same way.
[assembly: XmlnsPrefix("https://arxis.studio/test-controls", "tc")]
