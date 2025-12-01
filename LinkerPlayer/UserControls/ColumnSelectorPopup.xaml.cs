using LinkerPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
namespace LinkerPlayer.UserControls;

public partial class ColumnSelectorPopup : UserControl
{
    public ColumnSelectorViewModel ViewModel { get; }
    //public event RoutedEventHandler? OkClicked;
    public ColumnSelectorPopup(ColumnSelectorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }
}
