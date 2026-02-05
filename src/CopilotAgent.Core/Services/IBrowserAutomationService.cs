using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Result of a browser automation action.
/// </summary>
public class BrowserActionResult
{
    /// <summary>Whether the action succeeded.</summary>
    public bool Success { get; init; }
    
    /// <summary>Human-readable message describing the result.</summary>
    public string? Message { get; init; }
    
    /// <summary>Additional data returned by the action.</summary>
    public object? Data { get; init; }
    
    /// <summary>Error details if the action failed.</summary>
    public string? Error { get; init; }

    public static BrowserActionResult Ok(string? message = null, object? data = null) =>
        new() { Success = true, Message = message, Data = data };

    public static BrowserActionResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Information about the current browser page.
/// </summary>
public class BrowserPageInfo
{
    /// <summary>Current URL of the page.</summary>
    public string Url { get; init; } = string.Empty;
    
    /// <summary>Title of the page.</summary>
    public string Title { get; init; } = string.Empty;
    
    /// <summary>Whether the page has finished loading.</summary>
    public bool IsLoaded { get; init; }
}

/// <summary>
/// Link information extracted from a page.
/// </summary>
public class BrowserLinkInfo
{
    /// <summary>Link text content.</summary>
    public string Text { get; init; } = string.Empty;
    
    /// <summary>Link href attribute.</summary>
    public string Href { get; init; } = string.Empty;
}

/// <summary>
/// Form input information extracted from a page.
/// </summary>
public class BrowserFormInput
{
    /// <summary>HTML tag name (input, textarea, select).</summary>
    public string Tag { get; init; } = string.Empty;
    
    /// <summary>Input type (text, password, email, etc.).</summary>
    public string Type { get; init; } = string.Empty;
    
    /// <summary>Input name attribute.</summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>Input id attribute.</summary>
    public string Id { get; init; } = string.Empty;
    
    /// <summary>Input placeholder text.</summary>
    public string Placeholder { get; init; } = string.Empty;
    
    /// <summary>Current input value.</summary>
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Screenshot result from browser.
/// </summary>
public class BrowserScreenshot
{
    /// <summary>Screenshot image as base64-encoded PNG.</summary>
    public string Base64 { get; init; } = string.Empty;
    
    /// <summary>MIME type of the image.</summary>
    public string MimeType { get; init; } = "image/png";
}

/// <summary>
/// Service for browser automation, enabling OAuth/SAML authentication flows
/// and persistent browser sessions.
/// </summary>
public interface IBrowserAutomationService : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the browser is currently initialized and ready.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Gets whether there's an active browser session.
    /// </summary>
    bool HasActiveSession { get; }

    /// <summary>
    /// Gets the current browser settings.
    /// </summary>
    BrowserAutomationSettings Settings { get; }

    /// <summary>
    /// Initializes the browser instance. Called automatically on first use.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Navigates to a URL.
    /// </summary>
    /// <param name="url">The URL to navigate to.</param>
    /// <param name="waitUntil">Wait condition: "load", "domcontentloaded", "networkidle".</param>
    /// <param name="timeoutMs">Navigation timeout in milliseconds.</param>
    Task<BrowserActionResult> NavigateAsync(
        string url, 
        string waitUntil = "domcontentloaded",
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clicks an element by CSS selector.
    /// </summary>
    Task<BrowserActionResult> ClickAsync(
        string selector, 
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fills a form input with a value (replaces existing content).
    /// </summary>
    Task<BrowserActionResult> FillAsync(
        string selector, 
        string value, 
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Types text into an element (simulates keyboard input).
    /// </summary>
    Task<BrowserActionResult> TypeAsync(
        string selector, 
        string text,
        int delayMs = 50,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Presses a keyboard key.
    /// </summary>
    Task<BrowserActionResult> PressKeyAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a screenshot of the current page.
    /// </summary>
    Task<BrowserActionResult> TakeScreenshotAsync(
        bool fullPage = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the text content of an element or the whole page.
    /// </summary>
    Task<BrowserActionResult> GetTextContentAsync(
        string? selector = null,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the HTML content of an element or the whole page.
    /// </summary>
    Task<BrowserActionResult> GetHtmlContentAsync(
        string? selector = null,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for an element to appear.
    /// </summary>
    Task<BrowserActionResult> WaitForElementAsync(
        string selector,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for navigation to complete.
    /// </summary>
    Task<BrowserActionResult> WaitForNavigationAsync(
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about the current page.
    /// </summary>
    Task<BrowserPageInfo> GetPageInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects an option from a dropdown.
    /// </summary>
    Task<BrowserActionResult> SelectOptionAsync(
        string selector,
        string value,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a checkbox state.
    /// </summary>
    Task<BrowserActionResult> SetCheckboxAsync(
        string selector,
        bool isChecked,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scrolls the page or an element.
    /// </summary>
    Task<BrowserActionResult> ScrollAsync(
        string direction,
        string? selector = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Goes back in browser history.
    /// </summary>
    Task<BrowserActionResult> GoBackAsync(
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Goes forward in browser history.
    /// </summary>
    Task<BrowserActionResult> GoForwardAsync(
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads the current page.
    /// </summary>
    Task<BrowserActionResult> ReloadAsync(
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all links on the page.
    /// </summary>
    Task<BrowserActionResult> GetLinksAsync(
        int maxCount = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all form inputs on the page.
    /// </summary>
    Task<BrowserActionResult> GetFormInputsAsync(
        int maxCount = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the current browser storage state (cookies, localStorage, etc.).
    /// This enables persistent authentication across app restarts.
    /// </summary>
    Task SaveStorageStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a previously saved browser storage state.
    /// </summary>
    Task LoadStorageStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all browser data (cookies, cache, storage).
    /// </summary>
    Task ClearBrowserDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the current page for a session.
    /// </summary>
    Task CloseSessionPageAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the browser and all associated resources.
    /// </summary>
    Task CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates a page for a specific session.
    /// </summary>
    Task<BrowserActionResult> GetSessionPageAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a page navigation completes.
    /// </summary>
    event EventHandler<BrowserPageInfo>? NavigationCompleted;

    /// <summary>
    /// Event raised when the browser encounters an error.
    /// </summary>
    event EventHandler<string>? BrowserError;

    /// <summary>
    /// Event raised when console output is received from the browser.
    /// </summary>
    event EventHandler<string>? ConsoleMessage;
}