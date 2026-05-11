using System.Windows.Input;

namespace VELO.App.Controllers;

/// <summary>
/// Phase 3 / Sprint 10b chunk 5 (v2.4.28) — Extracted from
/// MainWindow.OnPreviewKeyDown. Replaces a ~150-line <c>switch</c>
/// statement with a declarative dictionary of <see cref="ShortcutKey"/>
/// to <see cref="Action"/>. The host registers every binding in one
/// table; a single <see cref="HandleKeyDown"/> dispatches.
///
/// Why bother:
///
///   1. <b>Duplicate-binding bugs become impossible.</b> Dictionary
///      initializers throw <see cref="ArgumentException"/> on
///      construction when two entries share a key. v2.4.23 silently
///      shadowed the Security Inspector binding under Ctrl+Shift+V
///      because clipboard history grabbed it first in the switch —
///      this controller would have failed loud at boot.
///
///   2. <b>One place to read all shortcuts.</b> Adding/auditing/
///      tweaking a binding is a one-line dictionary edit instead of
///      hunting through a 150-line switch.
///
///   3. <b>Setup for a future "customize shortcuts" UI.</b> The
///      bindings live in a mutable map — eventually a settings panel
///      can rewrite entries at runtime.
///
/// The Tab-1..Tab-9 ("Ctrl+1 to switch to tab N") binding doesn't fit a
/// key-equality table cleanly (it's a range), so it gets a dedicated
/// <c>tabNumberHandler</c> callback that fires on Ctrl + D1..D9.
///
/// The escape handler is declared like any other Action — the host
/// supplies an <c>OnEscape</c> method that knows whether to close the
/// find bar or stop the current navigation.
///
/// Pure: no WPF dependency beyond <c>Key</c>/<c>ModifierKeys</c>, no
/// owner window, no DI. Tests can construct the controller and call
/// <see cref="HandleKeyDown"/> directly.
/// </summary>
public sealed class KeyboardShortcutsController
{
    /// <summary>
    /// Composite shortcut key. <see cref="ModifierKeys"/> is a
    /// bitfield so e.g. <c>Control | Shift</c> is a distinct key
    /// from plain <c>Control</c>.
    /// </summary>
    public readonly record struct ShortcutKey(Key Key, ModifierKeys Modifiers);

    private readonly IReadOnlyDictionary<ShortcutKey, Action> _bindings;
    private readonly Action<int>? _tabNumberHandler;

    /// <summary>
    /// Constructs the controller with the supplied bindings.
    /// Duplicate keys throw <see cref="ArgumentException"/> via the
    /// dictionary initializer that produced <paramref name="bindings"/>.
    /// </summary>
    /// <param name="bindings">Map of shortcut → action.</param>
    /// <param name="tabNumberHandler">
    /// Optional callback invoked with the 0-based index when the user
    /// presses Ctrl+D1..Ctrl+D9. <c>null</c> disables the binding.
    /// </param>
    public KeyboardShortcutsController(
        IReadOnlyDictionary<ShortcutKey, Action> bindings,
        Action<int>? tabNumberHandler = null)
    {
        _bindings         = bindings;
        _tabNumberHandler = tabNumberHandler;
    }

    /// <summary>
    /// Number of registered bindings (excluding the tab-number range).
    /// Test helper.
    /// </summary>
    public int Count => _bindings.Count;

    /// <summary>
    /// Dispatches <paramref name="key"/>+<paramref name="modifiers"/>
    /// to the registered Action when one matches. Tab-number bindings
    /// (Ctrl+1..Ctrl+9) are checked first. Returns true when an action
    /// was invoked — the caller is expected to mark the event handled.
    /// </summary>
    public bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        // Tab-number range (Ctrl+D1..Ctrl+D9). Special-cased because
        // Dictionary equality can't express a range.
        if (_tabNumberHandler is not null
            && (modifiers & ModifierKeys.Control) != 0
            && key >= Key.D1 && key <= Key.D9)
        {
            _tabNumberHandler(key - Key.D1);
            return true;
        }

        if (_bindings.TryGetValue(new ShortcutKey(key, modifiers), out var action))
        {
            action();
            return true;
        }
        return false;
    }
}
