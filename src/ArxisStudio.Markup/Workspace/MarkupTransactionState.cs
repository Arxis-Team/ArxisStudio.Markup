namespace ArxisStudio.Markup;

/// <summary>The lifecycle stage of a <see cref="MarkupTransaction"/>.</summary>
public enum MarkupTransactionState
{
    /// <summary>The transaction is open and staging changes that no reader can see yet.</summary>
    Active,

    /// <summary>The transaction's changes were published to the workspace as a unit.</summary>
    Committed,

    /// <summary>The transaction's changes were discarded without ever being published.</summary>
    RolledBack,
}
