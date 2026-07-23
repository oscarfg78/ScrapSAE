using System.Windows;
using System.Windows.Controls;

namespace ScrapSAE.Desktop.Views;

public partial class CoverageCard : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(CoverageCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CoverageProperty =
        DependencyProperty.Register(nameof(Coverage), typeof(int), typeof(CoverageCard),
            new PropertyMetadata(0));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public int Coverage
    {
        get => (int)GetValue(CoverageProperty);
        set => SetValue(CoverageProperty, value);
    }

    public CoverageCard()
    {
        InitializeComponent();
    }
}
