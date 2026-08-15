using System.Windows;
using System.Windows.Controls;

namespace zidimi_uninstaller.Controls;

/// <summary>Custom checkbox with rounded box styling.</summary>
public class ZCheckBox : CheckBox
{
    static ZCheckBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZCheckBox), new FrameworkPropertyMetadata(typeof(ZCheckBox)));
    }
}