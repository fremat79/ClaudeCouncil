using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using ClaudeCouncilManager.Services;
using Microsoft.Win32;

namespace ClaudeCouncilManager.Views;

public enum MarkdownViewMode { Preview, Edit, Split }

/// <summary>
/// The app's Markdown viewer/editor (logbook, CLAUDE.md, …): a plain-text editor next to a live
/// preview. One window per file, not modal, so it can stay open next to the main window. The
/// file is watched: changes written by a session (the coordinator appending to the logbook)
/// show up by themselves, unless there are unsaved edits, in which case the user decides.
/// </summary>
public partial class MarkdownWindow : Window
{
    private const string AppTitle = "Claude Council Manager";

    private static readonly Dictionary<string, MarkdownWindow> OpenWindows = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _path;
    private readonly string _caption;
    private readonly bool _scrollToEnd;
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _diskTimer;
    // The file's content as last read or written: unsaved edits are whatever differs from it.
    private string _diskText = "";
    private string? _pendingDiskText;
    private bool _bom;
    private string _newline = Environment.NewLine;
    private bool _previewStale = true;
    private bool _findInEditor;

    /// <summary>Shows <paramref name="path"/>, bringing back the window already open on it if there is one.</summary>
    /// <param name="scrollToEnd">Start at the bottom, where an append-only file such as the logbook grows.</param>
    public static void Open(string path, MarkdownViewMode mode = MarkdownViewMode.Preview, string? caption = null, bool scrollToEnd = false)
    {
        path = Path.GetFullPath(path);
        if (OpenWindows.TryGetValue(path, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new MarkdownWindow(path, mode, caption ?? Path.GetFileName(path), scrollToEnd);
        OpenWindows[path] = window;
        window.Show();
    }

    /// <summary>Closes every viewer before the app quits; false if the user kept one open over unsaved edits.</summary>
    public static bool CloseAll()
    {
        foreach (var window in OpenWindows.Values.ToList())
        {
            window.Close();
            if (OpenWindows.ContainsKey(window._path)) return false;
        }
        return true;
    }

    private MarkdownWindow(string path, MarkdownViewMode mode, string caption, bool scrollToEnd)
    {
        InitializeComponent();
        _path = path;
        _caption = caption;
        _scrollToEnd = scrollToEnd;
        FilePath.Text = path;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _renderTimer.Tick += (_, _) => RenderPreview();
        // Writers often touch a file several times in a row: re-read once things settle.
        _diskTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _diskTimer.Tick += (_, _) => OnDiskChanged();

        Load(ReadFile(path)); // throws if the file can't be read: the window is never shown then

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        _watcher.Changed += OnWatcherEvent;
        _watcher.Created += OnWatcherEvent;
        _watcher.Renamed += OnWatcherEvent; // editors that save through a temporary file
        _watcher.EnableRaisingEvents = true;

        (mode switch { MarkdownViewMode.Edit => EditMode, MarkdownViewMode.Split => SplitMode, _ => PreviewMode }).IsChecked = true;
        Loaded += (_, _) =>
        {
            RenderPreview(initial: true);
            FocusPane(); // before loading the pane can't take focus, and it would land on the find box
        };
    }

    private bool IsDirty => Editor.Text.ReplaceLineEndings(_newline) != _diskText;

    // ---- Loading and saving --------------------------------------------------------------

    /// <summary>Reads the file even while a session has it open for writing.</summary>
    private string ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        _bom = bytes is [0xEF, 0xBB, 0xBF, ..];
        return new UTF8Encoding(false).GetString(bytes, _bom ? 3 : 0, bytes.Length - (_bom ? 3 : 0));
    }

    private void Load(string text)
    {
        var caret = Editor.CaretIndex;
        var offset = Editor.VerticalOffset;
        // Saved back with the line endings the file already uses.
        _newline = text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : Environment.NewLine;
        _diskText = text;
        _pendingDiskText = null;
        Editor.Text = text;
        Editor.CaretIndex = Math.Min(caret, text.Length);
        Editor.ScrollToVerticalOffset(offset);
        ChangedBanner.Visibility = Visibility.Collapsed;
        UpdateState();
    }

    private bool Save()
    {
        var text = Editor.Text.ReplaceLineEndings(_newline);
        try
        {
            File.WriteAllText(_path, text, new UTF8Encoding(_bom));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Can't save the file:\n\n" + ex.Message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _diskText = text; // so the watcher's echo of our own write isn't taken for a change
        _pendingDiskText = null;
        ChangedBanner.Visibility = Visibility.Collapsed;
        UpdateState();
        return true;
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            _diskTimer.Stop();
            _diskTimer.Start();
        });

    private void OnDiskChanged()
    {
        _diskTimer.Stop();
        string disk;
        try
        {
            disk = ReadFile(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // deleted or locked for now: a later event retries, Save recreates it
        }
        if (disk == _diskText || disk == _pendingDiskText) return;
        if (IsDirty)
        {
            _pendingDiskText = disk;
            ChangedBanner.Visibility = Visibility.Visible;
            return;
        }
        Load(disk);
        RenderPreview();
    }

    private void OnReloadFromDisk(object sender, RoutedEventArgs e)
    {
        try
        {
            Load(ReadFile(_path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Can't read the file:\n\n" + ex.Message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RenderPreview();
    }

    private void OnKeepEdits(object sender, RoutedEventArgs e)
    {
        // Compare against the newer version from now on: the edits stay unsaved until Save overwrites it.
        if (_pendingDiskText is not null) _diskText = _pendingDiskText;
        _pendingDiskText = null;
        ChangedBanner.Visibility = Visibility.Collapsed;
        UpdateState();
    }

    private void UpdateState()
    {
        var dirty = IsDirty;
        SaveButton.IsEnabled = dirty;
        Caption.Text = dirty ? _caption + "  •" : _caption;
        Title = (dirty ? _caption + " (unsaved)" : _caption) + " - " + AppTitle;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || !IsDirty) return;
        var answer = MessageBox.Show(this, $"Save the changes to {Path.GetFileName(_path)}?", AppTitle,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save()))
            e.Cancel = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _renderTimer.Stop();
        _diskTimer.Stop();
        OpenWindows.Remove(_path);
        base.OnClosed(e);
    }

    // ---- Preview -------------------------------------------------------------------------

    private ScrollViewer? PreviewScroller => Preview.Template?.FindName("PART_ContentHost", Preview) as ScrollViewer;

    /// <summary>Re-renders the preview, staying at the bottom if it was there (where the logbook grows).</summary>
    private void RenderPreview(bool initial = false)
    {
        _renderTimer.Stop();
        if (Preview.Visibility != Visibility.Visible)
        {
            _previewStale = true;
            return;
        }
        var scroller = PreviewScroller;
        var toEnd = initial ? _scrollToEnd : scroller is null || scroller.VerticalOffset >= scroller.ScrollableHeight - 2;
        var offset = initial ? 0 : scroller?.VerticalOffset ?? 0;
        Preview.Document = MarkdownRenderer.Render(Editor.Text, Path.GetDirectoryName(_path), OpenLink);
        _previewStale = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (PreviewScroller is not { } s) return;
            if (toEnd) s.ScrollToEnd();
            else s.ScrollToVerticalOffset(offset);
        });
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        UpdateState();
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    /// <summary>
    /// Web and mail links open in the browser; a Markdown file opens in a viewer; any other local
    /// file is only shown in Explorer, never run.
    /// </summary>
    private void OpenLink(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
                return;
            }
            if (url.StartsWith('#')) return; // links to a heading of the same file aren't supported
            if (MarkdownRenderer.ResolveLocalPath(url, Path.GetDirectoryName(_path)) is not { } path) return;
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            else if (!File.Exists(path))
                MessageBox.Show(this, "Not found:\n\n" + path, AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            else if (IsMarkdown(path))
                Open(path);
            else
                ShowInExplorer(path);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            MessageBox.Show(this, ex.Message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool IsMarkdown(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown";

    private static void ShowInExplorer(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");

    // ---- View mode -----------------------------------------------------------------------

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        var showEditor = EditMode.IsChecked == true || SplitMode.IsChecked == true;
        var showPreview = PreviewMode.IsChecked == true || SplitMode.IsChecked == true;
        // Star widths again: the splitter turns them into pixels when dragged.
        EditorColumn.Width = showEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PreviewColumn.Width = showPreview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Editor.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
        Splitter.Visibility = showEditor && showPreview ? Visibility.Visible : Visibility.Collapsed;
        Preview.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        if (showPreview && _previewStale && IsLoaded) RenderPreview();
        FocusPane();
    }

    private void FocusPane()
    {
        if (Editor.Visibility == Visibility.Visible) Editor.Focus();
        else Preview.Focus();
    }

    private void OnEditorFocused(object sender, KeyboardFocusChangedEventArgs e) => _findInEditor = true;

    private void OnPreviewFocused(object sender, KeyboardFocusChangedEventArgs e) => _findInEditor = false;

    // ---- Commands ------------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.S)
        {
            if (IsDirty) Save();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.F)
        {
            FindBox.Focus();
            FindBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            FindNext();
            e.Handled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void OnOpenOther(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open a Markdown file",
            Filter = "Markdown (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_path),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Open(dlg.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Can't open the file:\n\n" + ex.Message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnShowInFolder(object sender, RoutedEventArgs e) => ShowInExplorer(_path);

    private void OnOpenExternally(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _path, UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No app associated with .md files.
            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", ArgumentList = { _path }, UseShellExecute = true });
        }
    }

    // ---- Find ----------------------------------------------------------------------------

    private void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            (Editor.Visibility == Visibility.Visible && _findInEditor ? (IInputElement)Editor : Preview).Focus();
            e.Handled = true;
        }
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs e) => FindStatus.Text = "";

    /// <summary>Selects the next match after the current selection, wrapping around at the end.</summary>
    private void FindNext()
    {
        var query = FindBox.Text;
        if (query.Length == 0) return;
        var inEditor = Editor.Visibility == Visibility.Visible && (Preview.Visibility != Visibility.Visible || _findInEditor);
        var found = inEditor ? FindInEditor(query) : FindInPreview(query);
        FindStatus.Text = found ? "" : "No matches";
    }

    private bool FindInEditor(string query)
    {
        var text = Editor.Text;
        var from = Math.Min(Editor.SelectionStart + Editor.SelectionLength, text.Length);
        var index = text.IndexOf(query, from, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0) index = text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0) return false;
        Editor.Select(index, query.Length);
        Editor.ScrollToLine(Editor.GetLineIndexFromCharacterIndex(index));
        return true;
    }

    private bool FindInPreview(string query)
    {
        if (Preview.Document is not { } doc) return false;
        var start = Preview.Selection is { IsEmpty: false } selection ? selection.End : doc.ContentStart;
        var match = FindText(start, doc.ContentEnd, query) ?? FindText(doc.ContentStart, start, query);
        if (match is not { } m) return false;
        Preview.Selection?.Select(m.From, m.To);
        m.From.Paragraph?.BringIntoView();
        return true;
    }

    /// <summary>First match of <paramref name="query"/> within a single run of text between two positions.</summary>
    private static (TextPointer From, TextPointer To)? FindText(TextPointer start, TextPointer end, string query)
    {
        for (var p = start; p is not null && p.CompareTo(end) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
            var index = p.GetTextInRun(LogicalDirection.Forward).IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) continue;
            var from = p.GetPositionAtOffset(index);
            var to = from?.GetPositionAtOffset(query.Length);
            if (from is not null && to is not null && to.CompareTo(end) <= 0) return (from, to);
        }
        return null;
    }
}
