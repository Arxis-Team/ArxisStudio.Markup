using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// Something for the document's bindings to bind to.
/// </summary>
/// <remarks>
/// A host supplies the data; the library never does. Without it the view's bindings resolve to
/// nothing and the preview shows empty labels, which reads as a rendering fault rather than as
/// bindings waiting for a data context — and hides the thing worth seeing, that a binding was
/// kept as a binding rather than written back as whatever it evaluated to.
/// </remarks>
internal static class SampleData
{
    /// <summary>Gives a loaded root something to bind against.</summary>
    /// <param name="root">The object the document produced.</param>
    /// <returns>The root as a control, when it is one.</returns>
    internal static Control? Attach(object root)
    {
        if (root is Control control)
        {
            control.DataContext = new Workspace(new Customer());

            return control;
        }

        return null;
    }

    /// <summary>What the showcase's documents bind against.</summary>
    /// <param name="Customer">The customer on display.</param>
    internal sealed record Workspace(Customer Customer);

    /// <summary>One customer, invented.</summary>
    internal sealed record Customer
    {
        /// <summary>Gets the customer's name.</summary>
        public string Name { get; init; } = "Grace Hopper";

        /// <summary>Gets what they do.</summary>
        public string Title { get; init; } = "Ведущий инженер · с 2019 года";

        /// <summary>Gets whether the account is in good standing.</summary>
        public string Status { get; init; } = "Активен";

        /// <summary>Gets the e-mail address.</summary>
        public string Email { get; init; } = "grace@arxis.dev";

        /// <summary>Gets the telephone number.</summary>
        public string Phone { get; init; } = "+7 900 123-45-67";

        /// <summary>Gets the city.</summary>
        public string City { get; init; } = "Санкт-Петербург";

        /// <summary>Gets how many orders they have placed.</summary>
        public string Orders { get; init; } = "128 заказов";

        /// <summary>Gets what those came to.</summary>
        public string Total { get; init; } = "1 284 000 ₽";

        /// <summary>Gets whatever was last written about them.</summary>
        public string Note { get; init; } =
            "Просила выставлять счета в конце месяца. Договор продлён до августа.";
    }
}
