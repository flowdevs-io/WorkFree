using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WorkFree.Core;

namespace WorkFree.Gui;

/// <summary>Modal question shown before anything is deleted.</summary>
public static class ConfirmDialog
{
    public static Task<DeleteChoice> Ask(Window owner, DeleteRequest request)
    {
        var dialog = Build(request);
        return dialog.ShowDialog<DeleteChoice>(owner);
    }

    public static Window Build(DeleteRequest request)
    {
        var dialog = new Window
        {
            Title = "Delete worktrees",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var safeCount = request.Count - request.UnsafeCount;
        var text = new StackPanel { Spacing = 8, MaxWidth = 460 };
        text.Children.Add(new TextBlock
        {
            Text = $"Delete {request.Count} worktree(s), {Format.Size(request.Bytes)}?",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        });
        text.Children.Add(new TextBlock
        {
            Text = "The folders are removed from disk. Branches and their commits are kept.",
            TextWrapping = TextWrapping.Wrap,
        });
        if (request.UnsafeCount > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = $"{request.UnsafeCount} of them have uncommitted changes or a lock. Force-deleting those loses the uncommitted work for good.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#D64545")),
            });
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        void Add(string label, DeleteChoice choice, bool isDefault = false, bool isCancel = false, bool danger = false)
        {
            var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
            if (danger) button.Classes.Add("danger");
            button.Click += (_, _) => dialog.Close(choice);
            buttons.Children.Add(button);
        }

        Add("Cancel", DeleteChoice.Cancel, isDefault: true, isCancel: true);
        if (request.UnsafeCount == 0)
        {
            Add("Delete", DeleteChoice.SafeOnly);
        }
        else
        {
            if (safeCount > 0) Add($"Delete the {safeCount} safe one(s)", DeleteChoice.SafeOnly);
            Add("Force-delete all", DeleteChoice.ForceAll, danger: true);
        }

        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 18, Children = { text, buttons } };
        return dialog;
    }
}
