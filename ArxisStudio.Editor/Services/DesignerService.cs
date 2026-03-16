using System;

namespace ArxisStudio.Editor.Services
{
    public enum DesignerChangeKind
    {
        Structure,
        PropertyValue
    }

    /// <summary>
    /// Сервис для управления глобальным состоянием конструктора и синхронизации.
    /// Используется как Singleton для оповещения об изменениях JSON.
    /// </summary>
    public sealed class DesignerService
    {
        private DesignerService() { }
        public static DesignerService Instance { get; } = new DesignerService();

        /// <summary>
        /// Событие, которое срабатывает при изменении JSON через панель свойств
        /// или через манипуляции с деревом.
        /// </summary>
        public event Action<DesignerChangeKind>? JsonChanged;

        /// <summary>
        /// Вызывается при изменении JSON в любой части модели.
        /// </summary>
        public void NotifyJsonChanged(DesignerChangeKind changeKind = DesignerChangeKind.Structure)
        {
            JsonChanged?.Invoke(changeKind);
        }
    }
}
