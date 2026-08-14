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
        var containers = listBox.SelectedItems.OfType<FileItem>().Where(static item => !item.IsLooseFilesContainer).ToArray();
        var hasDuplicateBasenames = containers.Length > 1 &&
            FModelV3ContainerIdentity.HasDuplicateBasenames(containers.Select(item => item.ContainerPath));
        var canExportToSource = containers.Length > 0 &&
            containers.All(static item => item.CanExportToSourceDirectory);

        var outputMenuItem = menu.FindName("ExportContainerAssetsToOutputMenuItem") as MenuItem;
        if (outputMenuItem is not null)
            outputMenuItem.Visibility = hasDuplicateBasenames ? Visibility.Collapsed : Visibility.Visible;

        var rawOutputMenuItem = menu.FindName("ExtractContainerRawToOutputMenuItem") as MenuItem;
        if (rawOutputMenuItem is not null)
            rawOutputMenuItem.Visibility = hasDuplicateBasenames ? Visibility.Collapsed : Visibility.Visible;

        var sourceMenuItem = menu.FindName("ExportContainerAssetsToSourceMenuItem") as MenuItem;
        if (sourceMenuItem is not null)
            sourceMenuItem.Visibility = canExportToSource ? Visibility.Visible : Visibility.Collapsed;

        var rawSourceMenuItem = menu.FindName("ExtractContainerRawToSourceMenuItem") as MenuItem;
        if (rawSourceMenuItem is not null)
            rawSourceMenuItem.Visibility = canExportToSource ? Visibility.Visible : Visibility.Collapsed;
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
