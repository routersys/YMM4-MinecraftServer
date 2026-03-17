using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MinecraftHost.Behaviors;

public static class ComboBoxIncrementalLoadingBehavior
{
    public static readonly DependencyProperty LoadMoreCommandProperty =
        DependencyProperty.RegisterAttached(
            "LoadMoreCommand",
            typeof(ICommand),
            typeof(ComboBoxIncrementalLoadingBehavior),
            new PropertyMetadata(null, OnLoadMoreCommandChanged));

    public static ICommand? GetLoadMoreCommand(DependencyObject obj) => (ICommand?)obj.GetValue(LoadMoreCommandProperty);

    public static void SetLoadMoreCommand(DependencyObject obj, ICommand? value) => obj.SetValue(LoadMoreCommandProperty, value);

    private static readonly DependencyProperty OwnerComboBoxProperty =
        DependencyProperty.RegisterAttached(
            "OwnerComboBox",
            typeof(ComboBox),
            typeof(ComboBoxIncrementalLoadingBehavior),
            new PropertyMetadata(null));

    private static ComboBox? GetOwnerComboBox(DependencyObject obj) => (ComboBox?)obj.GetValue(OwnerComboBoxProperty);

    private static void SetOwnerComboBox(DependencyObject obj, ComboBox? value) => obj.SetValue(OwnerComboBoxProperty, value);

    private static void OnLoadMoreCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox comboBox)
            return;

        comboBox.DropDownOpened -= OnDropDownOpened;
        if (e.NewValue is ICommand)
            comboBox.DropDownOpened += OnDropDownOpened;
    }

    private static void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        var scrollViewer = FindDescendant<ScrollViewer>(comboBox);
        if (scrollViewer is null)
            return;

        SetOwnerComboBox(scrollViewer, comboBox);
        scrollViewer.ScrollChanged -= OnScrollChanged;
        scrollViewer.ScrollChanged += OnScrollChanged;

        var command = GetLoadMoreCommand(comboBox);
        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        var nearBottom = scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 1;
        if (!nearBottom)
            return;

        var comboBox = GetOwnerComboBox(scrollViewer) ?? FindAncestor<ComboBox>(scrollViewer);
        var command = comboBox is null ? null : GetLoadMoreCommand(comboBox);
        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T result)
                return result;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? source) where T : DependencyObject
    {
        if (source is null)
            return null;

        var count = VisualTreeHelper.GetChildrenCount(source);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is T result)
                return result;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}