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
            ErrorText.Text = Strings.LogTimeInvalid;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
