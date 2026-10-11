using System.Windows;

namespace ClaudeCouncilManager.Views;

/// <summary>Modal preview for <see cref="ViewModels.ImportViewModel"/>; DialogResult true means "import the ticked ones".</summary>
public partial class ImportWindow : Window
{
    public ImportWindow()
    {
        InitializeComponent();
    }

    private void OnImport(object sender, RoutedEventArgs e) => DialogResult = true;
}
