using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FModel.Framework;
using FModel.ViewModels;

namespace FModel.Views.Resources.Controls.ContextMenus;

public partial class ContainerContextMenuDictionary
{
    public ContainerContextMenuDictionary()
    {
        InitializeComponent();
    }

    private void ContainerContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { PlacementTarget: FrameworkElement element } menu)
            return;

        var listBox = FindAncestor<ListBox>(element);
        if (listBox is null)
            return;

        menu.DataContext = listBox.DataContext;
        menu.Tag = listBox.SelectedItems;
        var containers = listBox.SelectedItems.OfType<FileItem>().ToArray();
        var outputMenuItem = menu.FindName("ExportContainerAssetsToOutputMenuItem") as MenuItem;
        if (outputMenuItem is not null)
            outputMenuItem.Visibility = containers.Length > 1 && FModelV3ContainerIdentity.HasDuplicateBasenames(containers.Select(item => item.ContainerPath))
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T result)
                return result;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
