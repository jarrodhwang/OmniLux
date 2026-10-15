using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace OmniLux.Controls;

/// <summary>Commit a preview at interaction boundaries, never for intermediate ValueChanged events.</summary>
public static class SliderApply
{
    private static readonly DependencyProperty PointerActiveProperty = DependencyProperty.RegisterAttached(
        "PointerActive", typeof(bool), typeof(SliderApply), new PropertyMetadata(false));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(SliderApply), new PropertyMetadata(null, CommandChanged));
    public static void SetCommand(DependencyObject target, ICommand? value) => target.SetValue(CommandProperty, value);
    public static ICommand? GetCommand(DependencyObject target) => (ICommand?)target.GetValue(CommandProperty);

    private static void CommandChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Slider slider || args.OldValue is not null) return;
        slider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, _) => slider.SetValue(PointerActiveProperty, true)), true);
        slider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(MouseReleased), true);
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(DragCompleted), true);
        slider.MouseLeave += MouseLeave;
        slider.LostMouseCapture += LostCapture;
        slider.LostKeyboardFocus += LostFocus;
        slider.PreviewKeyUp += KeyUp;
    }
    private static void Apply(Slider slider)
    {
        slider.GetBindingExpression(RangeBase.ValueProperty)?.UpdateSource();
        var command = GetCommand(slider);
        if (command?.CanExecute(null) == true) command.Execute(null);
    }
    private static void EndInteraction(Slider slider) { slider.SetValue(PointerActiveProperty, false); Apply(slider); }
    private static void MouseReleased(object sender, MouseButtonEventArgs args) => EndInteraction((Slider)sender);
    private static void DragCompleted(object sender, DragCompletedEventArgs args) => EndInteraction((Slider)sender);
    private static void LostCapture(object sender, MouseEventArgs args)
    { if (Mouse.LeftButton != MouseButtonState.Pressed) Apply((Slider)sender); }
    private static void MouseLeave(object sender, MouseEventArgs args)
    {
        var slider = (Slider)sender;
        if (!(bool)slider.GetValue(PointerActiveProperty) && !slider.IsMouseCaptureWithin) Apply(slider);
    }
    private static void LostFocus(object sender, KeyboardFocusChangedEventArgs args)
    { if (Mouse.LeftButton != MouseButtonState.Pressed) Apply((Slider)sender); }
    private static void KeyUp(object sender, KeyEventArgs args)
    {
        if (args.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Enter)
            Apply((Slider)sender);
    }
}
