using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Settings for browser automation used in OAuth/SAML authentication flows.
/// </summary>
public class BrowserAutomationSettings
{
    /// <summary>
    /// Whether to run the browser in headless mode (no visible window).
    /// Default: true for production. Set to false for debugging auth flows.
    /// </summary>
    [JsonPropertyName("headless")]
    public bool Headless { get; set; } = true;

    /// <summary>
    /// Path to store browser storage state (cookies, localStorage, sessionStorage).
    /// This enables persistent authentication across app restarts.
    /// </summary>
    [JsonPropertyName("storageStatePath")]
    public string StorageStatePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CopilotAgent", "browser-data");

    /// <summary>
    /// Default timeout for browser operations in milliseconds.
    /// </summary>
    [JsonPropertyName("defaultTimeoutMs")]
    public int DefaultTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// User agent string for browser requests.
    /// Using a standard Chrome user agent to avoid detection.
    /// </summary>
    [JsonPropertyName("userAgent")]
    public string UserAgent { get; set; } = 
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    /// <summary>
    /// Browser viewport width.
    /// </summary>
    [JsonPropertyName("viewportWidth")]
    public int ViewportWidth { get; set; } = 1280;

    /// <summary>
    /// Browser viewport height.
    /// </summary>
    [JsonPropertyName("viewportHeight")]
    public int ViewportHeight { get; set; } = 800;

    /// <summary>
    /// Maximum number of days to retain browser storage state.
    /// After this period, users may need to re-authenticate.
    /// </summary>
    [JsonPropertyName("storageRetentionDays")]
    public int StorageRetentionDays { get; set; } = 30;

    /// <summary>
    /// Whether to enable browser console logging for debugging.
    /// </summary>
    [JsonPropertyName("enableConsoleLogging")]
    public bool EnableConsoleLogging { get; set; } = false;

    /// <summary>
    /// Additional browser arguments for Chromium.
    /// </summary>
    [JsonPropertyName("additionalBrowserArgs")]
    public List<string> AdditionalBrowserArgs { get; set; } = new()
    {
        "--disable-blink-features=AutomationControlled"
    };

    /// <summary>
    /// Gets the full path to the storage state file.
    /// </summary>
    [JsonIgnore]
    public string StorageStateFilePath => Path.Combine(StorageStatePath, "storage-state.json");
}