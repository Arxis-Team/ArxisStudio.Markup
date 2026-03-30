namespace ArxisStudio.Markup.DesignEditorBridge;

/// <summary>
/// Коды диагностик bridge-слоя.
/// </summary>
public static class BridgeDiagnosticCodes
{
    /// <summary>
    /// Для указанного узла не найден соответствующий контрол.
    /// </summary>
    public const string ControlNotFound = "ADB0001";
    /// <summary>
    /// Свойство дизайнера отсутствует в реестре.
    /// </summary>
    public const string UnknownDesignProperty = "ADB0002";
    /// <summary>
    /// Для свойства не зарегистрирован обработчик применения.
    /// </summary>
    public const string ApplierNotRegistered = "ADB0003";
    /// <summary>
    /// Значение свойства не является скаляром.
    /// </summary>
    public const string NonScalarValue = "ADB0004";
}
