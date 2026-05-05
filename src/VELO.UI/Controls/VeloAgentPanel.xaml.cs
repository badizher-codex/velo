using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VELO.Agent;
using VELO.Agent.Models;

namespace VELO.UI.Controls;

/// <summary>
/// Collapsible right-side chat panel for VeloAgent.
///
/// Usage:
///   1. Call SetServices(launcher, sandbox) once after DI is ready.
///   2. Call SetTabContext(tabId, context) each time the active tab changes.
///   3. Toggle visibility with ToggleVisibility().
/// </summary>
public partial class VeloAgentPanel : UserControl
{
    // ── Services (injected by MainWindow) ────────────────────────────────────
    private AgentLauncher?      _launcher;
    private AgentActionSandbox? _sandbox;

    // Phase 3 / Sprint 6 — slash commands + per-tab page priming.
    private SlashCommandRouter? _slashRouter;
    private PageContextManager? _pageCtx;
    /// <summary>Raised when the user asks the host to extract the current page's content for priming.</summary>
    public event EventHandler? AskAboutPageRequested;

    // ── State ─────────────────────────────────────────────────────────────────
    private string       _activeTabId = "";
    private AgentContext _context     = new();
    private bool         _isThinking  = false;

    // Typing dots animation
    private readonly DispatcherTimer _dotsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _dotCount = 1;

    // ── Events (for MainWindow to react) ─────────────────────────────────────
    public event EventHandler? CloseRequested;
    public event EventHandler? ClearRequested;

    public VeloAgentPanel()
    {
        InitializeComponent();
        _dotsTimer.Tick += (_, _) =>
        {
            _dotCount = (_dotCount % 3) + 1;
            TypingDots.Text = new string('.', _dotCount);
        };
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetServices(AgentLauncher launcher, AgentActionSandbox sandbox)
    {
        _launcher = launcher;
        _sandbox  = sandbox;

        _launcher.ResponseReady += OnResponseReady;
        _sandbox.ActionProposed += OnActionProposed;

        BackendLabel.Text = "Listo";
    }

    /// <summary>Wires Sprint 6 slash-command + page-priming services.</summary>
    public void SetSlashServices(SlashCommandRouter router, PageContextManager pageCtx)
    {
        _slashRouter = router;
        _pageCtx     = pageCtx;
    }

    /// <summary>
    /// Called by the host after extracting the active page's reader-mode
    /// text. Primes the chat so the next user message gets the page-context
    /// system prompt prepended (spec § 7.3).
    /// </summary>
    public void PrimeWithPage(string url, string title, string content)
    {
        if (_pageCtx == null || string.IsNullOrEmpty(_activeTabId)) return;
        _pageCtx.Prime(_activeTabId, url, title, content);
        AppendSystemMessage($"Contexto cargado de: {title} — pregúntame lo que quieras sobre esta página.");
    }

    /// <summary>
    /// Called by the host when the active tab changes. Adds a visual
    /// separator instead of clearing the chat (spec § 7.3 — context
    /// changes must be marked, not silently reset).
    /// </summary>
    public void NotifyTabSwitched(string newUrl)
    {
        if (MessagesPanel.Children.Count == 0) return;
        AppendSystemMessage(PageContextManager.BuildTabSwitchSeparator(newUrl));
    }

    public void SetTabContext(string tabId, AgentContext context)
    {
        _activeTabId = tabId;
        _context     = context;
    }

    public void SetBackendName(string name)
        => Dispatcher.Invoke(() => BackendLabel.Text = name);

    public void ToggleVisibility()
    {
        Visibility = Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (Visibility == Visibility.Visible)
            InputBox.Focus();
    }

    // ── Message rendering ─────────────────────────────────────────────────────

    private void AppendUserBubble(string text)
    {
        var border = new Border
        {
            Background    = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x3E)),
            CornerRadius  = new CornerRadius(12, 12, 2, 12),
            Padding       = new Thickness(10, 7, 10, 7),
            Margin        = new Thickness(40, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        border.Child = new TextBlock
        {
            Text             = text,
            Foreground       = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xE8)),
            FontSize         = 13,
            TextWrapping     = TextWrapping.Wrap,
        };
        MessagesPanel.Children.Add(border);
        ScrollToBottom();
    }

    private void AppendAgentBubble(string text)
    {
        var border = new Border
        {
            Background    = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x22)),
            BorderBrush   = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius  = new CornerRadius(12, 12, 12, 2),
            Padding       = new Thickness(10, 7, 10, 7),
            Margin        = new Thickness(8, 4, 40, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text         = "🤖 VeloAgent",
            FontSize     = 10,
            FontWeight   = FontWeights.SemiBold,
            Foreground   = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            Margin       = new Thickness(0, 0, 0, 4),
        });
        stack.Children.Add(new TextBlock
        {
            Text         = text,
            Foreground   = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xE8)),
            FontSize     = 13,
            TextWrapping = TextWrapping.Wrap,
        });

        border.Child = stack;
        MessagesPanel.Children.Add(border);
        ScrollToBottom();
    }

    private void AppendActionCard(string tabId, AgentAction action)
    {
        var card = new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x0D, 0x1A, 0x2E)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(10, 8, 10, 8),
            Margin          = new Thickness(8, 3, 8, 3),
        };

        var icon = action.Type switch
        {
            AgentActionType.OpenTab        => "🌐",
            AgentActionType.Search         => "🔍",
            AgentActionType.Summarize      => "📄",
            AgentActionType.FillForm       => "✏️",
            AgentActionType.ClickElement   => "👆",
            AgentActionType.ScrollTo       => "↕️",
            AgentActionType.CopyToClipboard => "📋",
            AgentActionType.ReadPage       => "👁",
            _                              => "⚡",
        };

        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        stack.Children.Add(new TextBlock
        {
            Text       = $"{icon} {action.Type}",
            FontSize   = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            Margin     = new Thickness(0, 0, 0, 2),
        });
        stack.Children.Add(new TextBlock
        {
            Text         = action.Description,
            FontSize     = 12,
            Foreground   = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xD8)),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrEmpty(action.Url))
            stack.Children.Add(new TextBlock
            {
                Text         = action.Url,
                FontSize     = 11,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x88)),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 2, 0, 0),
            });

        // Approve / Reject buttons
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 6, 0, 0),
        };

        var approveBtn = MakeActionButton("✓ Aprobar", "#FF2EB54F", action.Id, approve: true);
        var rejectBtn  = MakeActionButton("✗ Rechazar", "#FFE53E3E", action.Id, approve: false);

        buttons.Children.Add(approveBtn);
        buttons.Children.Add(rejectBtn);

        var cardStack = new StackPanel();
        cardStack.Children.Add(stack);
        cardStack.Children.Add(buttons);
        card.Child = cardStack;

        MessagesPanel.Children.Add(card);
        ScrollToBottom();
    }

    private Button MakeActionButton(string label, string colorHex, string actionId, bool approve)
    {
        var color  = (Color)ColorConverter.ConvertFromString(colorHex);
        var btn    = new Button
        {
            Content         = label,
            FontSize        = 11,
            FontWeight      = FontWeights.SemiBold,
            Foreground      = new SolidColorBrush(color),
            Background      = new SolidColorBrush(Color.FromArgb(0x1A, color.R, color.G, color.B)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(10, 4, 10, 4),
            Margin          = new Thickness(0, 0, 6, 0),
            Cursor          = Cursors.Hand,
        };

        btn.Click += (_, _) =>
        {
            if (approve) _sandbox?.Approve(actionId);
            else         _sandbox?.Reject(actionId);

            // Disable both buttons on the same card after decision
            var panel  = (btn.Parent as StackPanel);
            if (panel != null)
                foreach (var child in panel.Children.OfType<Button>())
                    child.IsEnabled = false;

            var resultText = approve ? "✓ Aprobado" : "✗ Rechazado";
            var resultColor = approve
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xB5, 0x4F))
                : new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E));

            panel?.Children.Add(new TextBlock
            {
                Text       = resultText,
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = resultColor,
                Margin     = new Thickness(4, 4, 0, 0),
            });
        };

        return btn;
    }

    private void AppendSystemMessage(string text)
    {
        MessagesPanel.Children.Add(new TextBlock
        {
            Text              = text,
            FontSize          = 11,
            FontStyle         = FontStyles.Italic,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x66)),
            Margin            = new Thickness(8, 6, 8, 6),
            TextAlignment     = TextAlignment.Center,
            TextWrapping      = TextWrapping.Wrap,
        });
        ScrollToBottom();
    }

    private void ScrollToBottom()
        => Dispatcher.InvokeAsync(() => MessagesScroll.ScrollToBottom(),
            DispatcherPriority.Background);

    // ── Send flow ─────────────────────────────────────────────────────────────

    private async void TrySend()
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text) || _isThinking) return;
        if (_launcher == null)
        {
            AppendSystemMessage("No hay modelo de IA configurado.");
            return;
        }

        InputBox.Clear();
        AppendUserBubble(text);
        SetThinking(true);

        // Phase 3 / Sprint 6 — slash command? Run it locally without the
        // model's tool-calling roundtrip; only fall through to the launcher
        // when the command is unrecognised (then it's free-form chat).
        if (_slashRouter != null && SlashCommandRouter.IsSlashCommand(text))
        {
            try
            {
                var slashResult = await _slashRouter.TryDispatchAsync(text);
                if (slashResult != null)
                {
                    SetThinking(false);
                    AppendAgentBubble(slashResult);
                    return;
                }
            }
            catch (Exception ex)
            {
                SetThinking(false);
                AppendSystemMessage($"⚠ Comando falló: {ex.Message}");
                return;
            }
        }

        // Page-priming: build a context with the per-tab system prompt the
        // first time the user types after "Ask about this page". Subsequent
        // turns use the same primed adapter history without resending the
        // full document text.
        var ctx = _context;
        if (_pageCtx != null && _pageCtx.IsPrimed(_activeTabId))
        {
            var primingPrompt = _pageCtx.BuildSystemPrompt(_activeTabId);
            if (!string.IsNullOrEmpty(primingPrompt))
            {
                ctx = new AgentContext
                {
                    CurrentUrl      = _context.CurrentUrl,
                    CurrentDomain   = _context.CurrentDomain,
                    PageTitle       = _context.PageTitle,
                    PageTextSnippet = primingPrompt,    // adapter prepends this to system
                    ContainerId     = _context.ContainerId,
                    OpenTabCount    = _context.OpenTabCount,
                    History         = _context.History,
                };
                _pageCtx.MarkSent(_activeTabId);
            }
        }

        _launcher.SendAsync(_activeTabId, text, ctx);
    }

    private void SetThinking(bool thinking)
    {
        _isThinking = thinking;
        TypingIndicator.Visibility = thinking ? Visibility.Visible : Visibility.Collapsed;
        SendButton.IsEnabled = !thinking;

        if (thinking) _dotsTimer.Start();
        else          _dotsTimer.Stop();
    }

    // ── Callbacks from services ───────────────────────────────────────────────

    private void OnResponseReady(AgentResponse response)
    {
        Dispatcher.Invoke(() =>
        {
            SetThinking(false);
            if (!string.IsNullOrEmpty(response.ReplyText))
                AppendAgentBubble(response.ReplyText);
        });
    }

    private void OnActionProposed(string tabId, AgentAction action)
    {
        if (tabId != _activeTabId) return;
        Dispatcher.Invoke(() => AppendActionCard(tabId, action));
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void Send_Click(object sender, RoutedEventArgs e) => TrySend();

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift)
                               && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            TrySend();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        MessagesPanel.Children.Clear();
        _launcher?.ClearHistory(_activeTabId);  // requires AgentLauncher.ClearHistory to be public
        AppendSystemMessage("Conversación borrada.");
        ClearRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void AskAboutPage_Click(object sender, RoutedEventArgs e)
        => AskAboutPageRequested?.Invoke(this, EventArgs.Empty);
}
