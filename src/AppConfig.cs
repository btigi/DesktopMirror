using System.Text.Json.Serialization;

namespace DesktopMirror
{
    public class AppConfig
    {
        public bool CloseOnEscape { get; set; } = true;
        public int? TargetMonitor { get; set; } = null; // null = current monitor
        public string? HideRegex { get; set; } = null;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HideExtensionsMode HideExtensions { get; set; } = HideExtensionsMode.Never;
        public string? HideExtensionsList { get; set; } = null; // pipe-separated list of extensions

        public bool UseCtrl { get; set; } = true;
        public bool UseAlt { get; set; } = true;
        public bool UseShift { get; set; } = false;
        public string Hotkey { get; set; } = "D";
        public bool ShowPasteArea { get; set; } = true;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HideExtensionsMode
    {
        Never,
        Always,
        ListedOnly
    }
}