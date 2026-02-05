using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Playwright-based implementation of browser automation service.
/// Provides persistent browser sessions for OAuth/SAML authentication.
/// </summary>
public sealed class PlaywrightBrowserService : IBrowserAutomationService
{
    private readonly ILogger<PlaywrightBrowserService> _logger;
    private readonly BrowserAutomationSettings _settings;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ConcurrentDictionary<string, IPage> _sessionPages = new();

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _currentPage;
    private bool _disposed;

    public bool IsInitialized => _browser != null && _browser.IsConnected;
    public bool HasActiveSession => _sessionPages.Count > 0 || _currentPage != null;
    public BrowserAutomationSettings Settings => _settings;

    public event EventHandler<BrowserPageInfo>? NavigationCompleted;
    public event EventHandler<string>? BrowserError;
    public event EventHandler<string>? ConsoleMessage;

    public PlaywrightBrowserService(
        ILogger<PlaywrightBrowserService> logger,
        BrowserAutomationSettings? settings = null)
    {
        _logger = logger;
        _settings = settings ?? new BrowserAutomationSettings();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsInitialized)
        {
            _logger.LogDebug("[Browser] Already initialized, reusing existing instance");
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (IsInitialized) return;

            _logger.LogInformation("[Browser] Initializing Playwright browser...");
            
            // Create storage directory if needed
            EnsureStorageDirectory();

            // Initialize Playwright
            _playwright = await Playwright.CreateAsync();
            
            // Launch browser with configured settings
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _settings.Headless,
                Args = _settings.AdditionalBrowserArgs.ToArray()
            });

            _browser.Disconnected += (_, _) =>
            {
                _logger.LogInformation("[Browser] Browser disconnected");
                _browser = null;
                _context = null;
                _currentPage = null;
                _sessionPages.Clear();
            };

            _logger.LogInformation("[Browser] Browser instance launched (Headless: {Headless})", _settings.Headless);

            // Create persistent browser context with storage state
            await CreateBrowserContextAsync();

            _logger.LogInformation("[Browser] Browser initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Failed to initialize browser");
            BrowserError?.Invoke(this, $"Failed to initialize browser: {ex.Message}");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void EnsureStorageDirectory()
    {
        if (!Directory.Exists(_settings.StorageStatePath))
        {
            Directory.CreateDirectory(_settings.StorageStatePath);
            _logger.LogDebug("[Browser] Created storage directory: {Path}", _settings.StorageStatePath);
        }
    }

    private async Task CreateBrowserContextAsync()
    {
        if (_browser == null) throw new InvalidOperationException("Browser not initialized");

        var hasStorageState = File.Exists(_settings.StorageStateFilePath);
        
        _logger.LogDebug("[Browser] Creating browser context, storage state exists: {HasState}", hasStorageState);

        // Log storage state info for debugging
        if (hasStorageState)
        {
            try
            {
                var stateContent = await File.ReadAllTextAsync(_settings.StorageStateFilePath);
                var state = JsonDocument.Parse(stateContent);
                var root = state.RootElement;
                
                var cookieCount = root.TryGetProperty("cookies", out var cookies) 
                    ? cookies.GetArrayLength() : 0;
                var originCount = root.TryGetProperty("origins", out var origins) 
                    ? origins.GetArrayLength() : 0;

                _logger.LogDebug("[Browser] Storage state has {CookieCount} cookies, {OriginCount} origins", 
                    cookieCount, originCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Browser] Could not parse storage state for logging");
            }
        }

        var contextOptions = new BrowserNewContextOptions
        {
            UserAgent = _settings.UserAgent,
            ViewportSize = new ViewportSize
            {
                Width = _settings.ViewportWidth,
                Height = _settings.ViewportHeight
            }
        };

        // Load storage state if it exists
        if (hasStorageState)
        {
            contextOptions.StorageStatePath = _settings.StorageStateFilePath;
        }

        _context = await _browser.NewContextAsync(contextOptions);

        // Set up context event handlers
        _context.Page += (_, page) =>
        {
            _logger.LogDebug("[Browser] New page created: {Url}", page.Url);
            SetupPageEventHandlers(page);
        };

        _context.Close += (_, _) =>
        {
            _logger.LogInformation("[Browser] Browser context closed");
            _context = null;
        };

        // Create initial page
        _currentPage = await _context.NewPageAsync();
        _logger.LogDebug("[Browser] Initial page created");
    }

    private void SetupPageEventHandlers(IPage page)
    {
        page.Load += (_, _) =>
        {
            var info = new BrowserPageInfo
            {
                Url = page.Url,
                Title = page.TitleAsync().GetAwaiter().GetResult() ?? "",
                IsLoaded = true
            };
            NavigationCompleted?.Invoke(this, info);
        };

        page.PageError += (_, error) =>
        {
            _logger.LogWarning("[Browser] Page error: {Error}", error);
            BrowserError?.Invoke(this, error);
        };

        if (_settings.EnableConsoleLogging)
        {
            page.Console += (_, msg) =>
            {
                _logger.LogDebug("[Browser Console] {Type}: {Text}", msg.Type, msg.Text);
                ConsoleMessage?.Invoke(this, $"[{msg.Type}] {msg.Text}");
            };
        }

        page.Close += (_, _) =>
        {
            // Remove from session pages if tracked
            var sessionId = _sessionPages.FirstOrDefault(kvp => kvp.Value == page).Key;
            if (sessionId != null)
            {
                _sessionPages.TryRemove(sessionId, out _);
                _logger.LogDebug("[Browser] Session page closed: {SessionId}", sessionId);
            }
        };
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!IsInitialized)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private async Task<IPage> GetCurrentPageAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (_currentPage == null || _currentPage.IsClosed)
        {
            if (_context == null) throw new InvalidOperationException("Browser context not initialized");
            _currentPage = await _context.NewPageAsync();
            SetupPageEventHandlers(_currentPage);
        }

        return _currentPage;
    }

    public async Task<BrowserActionResult> GetSessionPageAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (_sessionPages.TryGetValue(sessionId, out var existingPage))
        {
            if (!existingPage.IsClosed)
            {
                _logger.LogDebug("[Browser] Reusing existing page for session: {SessionId}", sessionId);
                _currentPage = existingPage;
                return BrowserActionResult.Ok($"Using existing page for session {sessionId}");
            }
            _sessionPages.TryRemove(sessionId, out _);
        }

        _logger.LogInformation("[Browser] Creating new page for session: {SessionId}", sessionId);
        if (_context == null) throw new InvalidOperationException("Browser context not initialized");

        var page = await _context.NewPageAsync();
        SetupPageEventHandlers(page);
        _sessionPages[sessionId] = page;
        _currentPage = page;

        return BrowserActionResult.Ok($"Created new page for session {sessionId}");
    }

    public async Task<BrowserActionResult> NavigateAsync(
        string url,
        string waitUntil = "domcontentloaded",
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Browser] Navigating to: {Url}", url);

        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            var timeout = timeoutMs ?? _settings.DefaultTimeoutMs;

            var waitUntilState = waitUntil.ToLowerInvariant() switch
            {
                "load" => WaitUntilState.Load,
                "networkidle" => WaitUntilState.NetworkIdle,
                "commit" => WaitUntilState.Commit,
                _ => WaitUntilState.DOMContentLoaded
            };

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = waitUntilState,
                Timeout = timeout
            });

            var title = await page.TitleAsync();
            
            // Auto-save storage state after navigation
            await SaveStorageStateAsync(cancellationToken);

            _logger.LogInformation("[Browser] Navigation complete: {Title}", title);
            return BrowserActionResult.Ok($"Navigated to \"{title}\" ({url})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Navigation failed: {Url}", url);
            return BrowserActionResult.Fail($"Navigation failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> ClickAsync(
        string selector,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.ClickAsync(selector, new PageClickOptions
            {
                Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
            });
            return BrowserActionResult.Ok($"Clicked element: {selector}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Click failed: {Selector}", selector);
            return BrowserActionResult.Fail($"Click failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> FillAsync(
        string selector,
        string value,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.FillAsync(selector, value, new PageFillOptions
            {
                Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
            });
            return BrowserActionResult.Ok($"Filled \"{selector}\" with value");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Fill failed: {Selector}", selector);
            return BrowserActionResult.Fail($"Fill failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> TypeAsync(
        string selector,
        string text,
        int delayMs = 50,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.ClickAsync(selector, new PageClickOptions
            {
                Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
            });
            await page.Keyboard.TypeAsync(text, new KeyboardTypeOptions
            {
                Delay = delayMs
            });
            return BrowserActionResult.Ok($"Typed text into \"{selector}\"");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Type failed: {Selector}", selector);
            return BrowserActionResult.Fail($"Type failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> PressKeyAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.Keyboard.PressAsync(key);
            return BrowserActionResult.Ok($"Pressed key: {key}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Key press failed: {Key}", key);
            return BrowserActionResult.Fail($"Key press failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> TakeScreenshotAsync(
        bool fullPage = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            var buffer = await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = fullPage,
                Type = ScreenshotType.Png
            });

            var base64 = Convert.ToBase64String(buffer);
            var screenshot = new BrowserScreenshot
            {
                Base64 = base64,
                MimeType = "image/png"
            };

            return BrowserActionResult.Ok("Screenshot captured", screenshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Screenshot failed");
            return BrowserActionResult.Fail($"Screenshot failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> GetTextContentAsync(
        string? selector = null,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            string? text;

            if (!string.IsNullOrEmpty(selector))
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element == null)
                {
                    return BrowserActionResult.Fail($"Element not found: {selector}");
                }
                text = await element.TextContentAsync();
            }
            else
            {
                text = await page.TextContentAsync("body");
            }

            // Truncate if too long
            const int maxLength = 5000;
            if (text?.Length > maxLength)
            {
                text = text[..maxLength] + "... (truncated)";
            }

            return BrowserActionResult.Ok("Text content retrieved", text?.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Get text failed");
            return BrowserActionResult.Fail($"Get text failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> GetHtmlContentAsync(
        string? selector = null,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            string html;

            if (!string.IsNullOrEmpty(selector))
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element == null)
                {
                    return BrowserActionResult.Fail($"Element not found: {selector}");
                }
                html = await element.InnerHTMLAsync();
            }
            else
            {
                html = await page.ContentAsync();
            }

            // Truncate if too long
            const int maxLength = 10000;
            if (html.Length > maxLength)
            {
                html = html[..maxLength] + "... (truncated)";
            }

            return BrowserActionResult.Ok("HTML content retrieved", html);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Get HTML failed");
            return BrowserActionResult.Fail($"Get HTML failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> WaitForElementAsync(
        string selector,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
            {
                Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
            });
            return BrowserActionResult.Ok($"Element found: {selector}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Wait for element failed: {Selector}", selector);
            return BrowserActionResult.Fail($"Wait failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> WaitForNavigationAsync(
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
            {
                Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
            });

            var url = page.Url;
            var title = await page.TitleAsync();

            return BrowserActionResult.Ok($"Navigation complete: \"{title}\" ({url})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Wait for navigation failed");
            return BrowserActionResult.Fail($"Wait for navigation failed: {ex.Message}");
        }
    }

    public async Task<BrowserPageInfo> GetPageInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            return new BrowserPageInfo
            {
                Url = page.Url,
                Title = await page.TitleAsync() ?? "",
                IsLoaded = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Get page info failed");
            return new BrowserPageInfo { Url = "", Title = "", IsLoaded = false };
        }
    }

    public async Task<BrowserActionResult> SelectOptionAsync(
        string selector,
        string value,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.SelectOptionAsync(selector, value, new PageSelectOptionOptions
            {
                Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
            });
            return BrowserActionResult.Ok($"Selected \"{value}\" in {selector}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Select option failed: {Selector}", selector);
            return BrowserActionResult.Fail($"Select failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> SetCheckboxAsync(
        string selector,
        bool isChecked,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            if (isChecked)
            {
                await page.CheckAsync(selector, new PageCheckOptions
                {
                    Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
                });
            }
            else
            {
                await page.UncheckAsync(selector, new PageUncheckOptions
                {
                    Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
                });
            }
            return BrowserActionResult.Ok($"Checkbox {(isChecked ? "checked" : "unchecked")}: {selector}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Checkbox operation failed: {Selector}", selector);
            return BrowserActionResult.Fail($"Checkbox operation failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> ScrollAsync(
        string direction,
        string? selector = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);

            if (!string.IsNullOrEmpty(selector))
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element == null)
                {
                    return BrowserActionResult.Fail($"Element not found: {selector}");
                }
                await element.ScrollIntoViewIfNeededAsync();
                return BrowserActionResult.Ok($"Scrolled to element: {selector}");
            }

            var key = direction.ToLowerInvariant() switch
            {
                "up" => "PageUp",
                "down" => "PageDown",
                "top" => "Home",
                "bottom" => "End",
                _ => "PageDown"
            };

            await page.Keyboard.PressAsync(key);
            return BrowserActionResult.Ok($"Scrolled {direction}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Scroll failed");
            return BrowserActionResult.Fail($"Scroll failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> GoBackAsync(
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.GoBackAsync(new PageGoBackOptions
            {
                Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
            });
            var title = await page.TitleAsync();
            return BrowserActionResult.Ok($"Navigated back to: \"{title}\"");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Go back failed");
            return BrowserActionResult.Fail($"Go back failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> GoForwardAsync(
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.GoForwardAsync(new PageGoForwardOptions
            {
                Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
            });
            var title = await page.TitleAsync();
            return BrowserActionResult.Ok($"Navigated forward to: \"{title}\"");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Go forward failed");
            return BrowserActionResult.Fail($"Go forward failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> ReloadAsync(
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            await page.ReloadAsync(new PageReloadOptions
            {
                Timeout = timeoutMs ?? _settings.DefaultTimeoutMs
            });
            var title = await page.TitleAsync();
            return BrowserActionResult.Ok($"Reloaded page: \"{title}\"");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Reload failed");
            return BrowserActionResult.Fail($"Reload failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> GetLinksAsync(
        int maxCount = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            var links = await page.EvaluateAsync<List<BrowserLinkInfo>>($@"
                () => Array.from(document.querySelectorAll('a[href]'))
                    .slice(0, {maxCount})
                    .map(el => ({{
                        text: (el.textContent || '').trim().substring(0, 100),
                        href: el.getAttribute('href') || ''
                    }}))
                    .filter(l => l.href && !l.href.startsWith('javascript:'))
            ");

            return BrowserActionResult.Ok($"Found {links?.Count ?? 0} links", links);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Get links failed");
            return BrowserActionResult.Fail($"Get links failed: {ex.Message}");
        }
    }

    public async Task<BrowserActionResult> GetFormInputsAsync(
        int maxCount = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await GetCurrentPageAsync(cancellationToken);
            var inputs = await page.EvaluateAsync<List<BrowserFormInput>>($@"
                () => Array.from(document.querySelectorAll('input, textarea, select'))
                    .slice(0, {maxCount})
                    .map(el => ({{
                        tag: el.tagName.toLowerCase(),
                        type: el.getAttribute('type') || 'text',
                        name: el.getAttribute('name') || '',
                        id: el.getAttribute('id') || '',
                        placeholder: el.getAttribute('placeholder') || '',
                        value: el.value || ''
                    }}))
            ");

            return BrowserActionResult.Ok($"Found {inputs?.Count ?? 0} form inputs", inputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Get form inputs failed");
            return BrowserActionResult.Fail($"Get form inputs failed: {ex.Message}");
        }
    }

    public async Task SaveStorageStateAsync(CancellationToken cancellationToken = default)
    {
        if (_context == null)
        {
            _logger.LogDebug("[Browser] No context to save storage state from");
            return;
        }

        try
        {
            EnsureStorageDirectory();
            await _context.StorageStateAsync(new BrowserContextStorageStateOptions
            {
                Path = _settings.StorageStateFilePath
            });
            _logger.LogDebug("[Browser] Storage state saved to: {Path}", _settings.StorageStateFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Failed to save storage state");
        }
    }

    public async Task LoadStorageStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settings.StorageStateFilePath))
        {
            _logger.LogDebug("[Browser] No storage state file to load");
            return;
        }

        try
        {
            // Need to recreate context with storage state
            await CloseAsync(cancellationToken);
            await InitializeAsync(cancellationToken);
            _logger.LogInformation("[Browser] Storage state loaded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Failed to load storage state");
        }
    }

    public async Task ClearBrowserDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Delete storage state file
            if (File.Exists(_settings.StorageStateFilePath))
            {
                File.Delete(_settings.StorageStateFilePath);
                _logger.LogInformation("[Browser] Storage state file deleted");
            }

            // Recreate context without storage state
            await CloseAsync(cancellationToken);
            
            _logger.LogInformation("[Browser] Browser data cleared");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Browser] Failed to clear browser data");
        }
    }

    public async Task CloseSessionPageAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessionPages.TryRemove(sessionId, out var page))
        {
            await SaveStorageStateAsync(cancellationToken);
            
            if (!page.IsClosed)
            {
                await page.CloseAsync();
            }
            
            _logger.LogInformation("[Browser] Closed page for session: {SessionId}", sessionId);
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Browser] Closing browser...");

        // Save storage state before closing
        await SaveStorageStateAsync(cancellationToken);

        // Close all session pages
        foreach (var (sessionId, page) in _sessionPages)
        {
            try
            {
                if (!page.IsClosed)
                {
                    await page.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Browser] Error closing page for session: {SessionId}", sessionId);
            }
        }
        _sessionPages.Clear();

        // Close context
        if (_context != null)
        {
            try
            {
                await _context.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Browser] Error closing context");
            }
            _context = null;
        }

        // Close browser
        if (_browser != null)
        {
            try
            {
                await _browser.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Browser] Error closing browser");
            }
            _browser = null;
        }

        // Dispose Playwright
        _playwright?.Dispose();
        _playwright = null;

        _currentPage = null;
        _logger.LogInformation("[Browser] Browser closed");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await CloseAsync();
        _initLock.Dispose();
    }
}