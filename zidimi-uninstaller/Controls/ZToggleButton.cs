using System.Windows;
using System.Windows.Controls.Primitives;

namespace zidimi_uninstaller.Controls;

/// <summary>Toggle switch button control.</summary>
public class ZToggleButton : ToggleButton
{
    static ZToggleButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZToggleButton), new FrameworkPropertyMetadata(typeof(ZToggleButton)));
    }
}