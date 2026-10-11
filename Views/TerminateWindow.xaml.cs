using System.Collections.Generic;
using System.Windows;

namespace ClaudeCouncilManager.Views;

/// <summary>
/// Confirmation for Stop: lists the sessions and asks whether their terminal tabs/windows
/// should be closed too. DialogResult true means "terminate".
/// </summary>
public partial class TerminateWindow : Window
{
    public TerminateWindow(IReadOnlyCollection<string> sessionNames, bool closeTerminal)
    {
        InitializeComponent();
        Heading.Text = sessionNames.Count == 1 ? "Stop this session?" : $"Stop these {sessionNames.Count} sessions?";
        SessionList.Text = string.Join("\n", sessionNames);
        CloseTerminalBox.IsChecked = closeTerminal;
    }

    public bool CloseTerminal => CloseTerminalBox.IsChecked == true;

    private void OnTerminate(object sender, RoutedEventArgs e) => DialogResult = true;
}
