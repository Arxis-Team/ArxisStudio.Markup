using Newtonsoft.Json.Linq;

namespace ArxisStudio.Editor.Models
{
    public sealed class ToolboxItem
    {
        public ToolboxItem(string typeName, string displayName, JObject template)
        {
            TypeName = typeName;
            DisplayName = displayName;
            Template = template;
        }

        public string TypeName { get; }

        public string DisplayName { get; }

        public JObject Template { get; }
    }
}
