using System.Windows;
using System.Windows.Controls;

namespace zidimi_uninstaller.Controls;
public class ZCheckBox : CheckBox
{
    static ZCheckBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ZCheckBox), new FrameworkPropertyMetadata(typeof(ZCheckBox)));
    }
}