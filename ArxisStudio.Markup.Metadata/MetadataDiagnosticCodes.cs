namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Коды диагностик валидации метаданных.
/// </summary>
public static class MetadataDiagnosticCodes
{
    /// <summary>
    /// Пустой или некорректный <c>NodeRef</c>.
    /// </summary>
    public const string EmptyNodeRef = "MDV0001";
    /// <summary>
    /// Свойство не зарегистрировано в реестре.
    /// </summary>
    public const string UnknownProperty = "MDV0002";
    /// <summary>
    /// Значение свойства не является скалярным.
    /// </summary>
    public const string NonScalarProperty = "MDV0003";
    /// <summary>
    /// Тип значения свойства несовместим с ожидаемым типом.
    /// </summary>
    public const string InvalidPropertyType = "MDV0004";
}
