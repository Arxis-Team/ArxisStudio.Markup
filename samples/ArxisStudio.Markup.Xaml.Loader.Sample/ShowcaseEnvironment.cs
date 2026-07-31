namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// The environment every load in the showcase goes through.
/// </summary>
/// <remarks>
/// Everything outside a document arrives here and nowhere else. The palette and the brand file
/// exist only in memory, which is the point: the includes in the document resolve through the
/// caller's own resolver rather than through anything on disk or compiled into an assembly.
/// </remarks>
internal static class ShowcaseEnvironment
{
    /// <summary>Builds the environment and the in-memory files it resolves.</summary>
    /// <returns>The environment, and the resolver its resource files can be changed through.</returns>
    internal static (XamlLoadEnvironment Environment, InMemoryResourceResolver Resources) Create()
    {
        var resources = new InMemoryResourceResolver();

        resources.Update(Fixtures.PaletteUri, Fixtures.Palette("#FF3366CC"));
        resources.Update(Fixtures.BrandUri, Fixtures.Brand);

        XamlLoadEnvironment defaults = XamlLoadEnvironment.CreateDefault();

        return (
            new XamlLoadEnvironment
            {
                SourceProvider = defaults.SourceProvider,
                AssemblyResolver = defaults.AssemblyResolver,
                TypeResolver = defaults.TypeResolver,

                // In front of the defaults, so an unsaved edit shadows whatever is on disk.
                ResourceResolver = new CompositeResourceResolver(resources, defaults.ResourceResolver),
            },
            resources);
    }
}
