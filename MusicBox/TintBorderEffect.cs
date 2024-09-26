using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

public class TintBorderEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("PSInput", typeof(TintBorderEffect), 0);

    public static readonly DependencyProperty TintColorProperty =
        DependencyProperty.Register("TintColor", typeof(Color), typeof(TintBorderEffect), new PropertyMetadata(Colors.White, PixelShaderConstantCallback(0)));
    
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register("BorderThickness", typeof(double), typeof(TintBorderEffect), new PropertyMetadata(0.1, PixelShaderConstantCallback(1)));

    public static readonly DependencyProperty AspectRatioProperty = 
        DependencyProperty.Register("AspectRatio", typeof(double), typeof(TintBorderEffect), new PropertyMetadata(1.0, PixelShaderConstantCallback(2)));

    public Brush Input
    {
        get { return (Brush)GetValue(InputProperty); }
        set { SetValue(InputProperty, value); }
    }

    public Color TintColor
    {
        get { return (Color)GetValue(TintColorProperty); }
        set { SetValue(TintColorProperty, value); }
    }

    public double BorderThickness
    {
        get { return (double)GetValue(BorderThicknessProperty); }
        set { SetValue(BorderThicknessProperty, value); }
    }

    public double AspectRatio
    {
        get { return (double)GetValue(AspectRatioProperty); }
        set { SetValue(AspectRatioProperty, value); }
    }

    public TintBorderEffect()
    {
        PixelShader = new PixelShader();
        PixelShader.UriSource = new Uri("pack://application:,,,/MusicBox;component/TintBorder.ps");

        UpdateShaderValue(InputProperty);
        UpdateShaderValue(TintColorProperty);
        UpdateShaderValue(BorderThicknessProperty);
        UpdateShaderValue(AspectRatioProperty);
    }
}