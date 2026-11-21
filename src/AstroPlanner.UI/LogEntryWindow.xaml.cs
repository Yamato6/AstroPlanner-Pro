using System.Windows;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI;

public partial class LogEntryWindow : Window
{
    public LogEntryWindow(LogEntryDraftViewModel draft)
    {
        InitializeComponent();
        NativeWindows.UseDarkTitleBar(this);
        DataContext = draft;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogEntryDraftViewModel draft) return;

        draft.Validate();
        if (draft.HasErrors || draft.ObservedAtLocal is null)
        {
            ErrorText.Text = draft.ObservedAtLocal is null ? Strings.LogTimeInvalid : draft.GetErrors().First().ErrorMessage;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    /// <summary>Picks the image of a photographed entry. Only its path is saved; the file is never copied.</summary>
    private void OnBrowseImageClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogEntryDraftViewModel draft) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.LogEntryImageDialogTitle,
            Filter = Strings.ImageFileFilter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            draft.ImagePath = dialog.FileName;
        }
    }
}
