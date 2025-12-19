using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScottPlot;

namespace LogoDetect.Services;

public partial class ThresholdSelector : Window
{
    private readonly Plot _plot;
    private ScottPlot.Plottables.HorizontalLine? _thresholdLine;
    
    public double Threshold { get; private set; }
    public bool WasConfirmed { get; private set; }

    public ThresholdSelector(Plot plot, double currentThreshold)
    {
        InitializeComponent();
        
        _plot = plot;
        
        // Set initial threshold
        ThresholdSlider.Value = currentThreshold;
    }

    private void UpdateGraphImage()
    {
        if (_plot == null)
            return;

        try
        {
            // Remove existing threshold line if present
            if (_thresholdLine != null)
            {
                _plot.Remove(_thresholdLine);
            }
            
            // Add threshold line at current slider value
            _thresholdLine = _plot.Add.HorizontalLine(ThresholdSlider.Value);
            _thresholdLine.Color = Colors.Red;
            _thresholdLine.LinePattern = ScottPlot.LinePattern.Dashed;
            _thresholdLine.LineWidth = 2;
            _thresholdLine.LegendText = "Logo Threshold";
            _thresholdLine.Axes.YAxis = _plot.Axes.Right;
            
            // Generate image from plot
            var imageBytes = _plot.GetImageBytes(2000, 1000, ScottPlot.ImageFormat.Png);
            
            // Convert to WPF BitmapSource
            using var memoryStream = new MemoryStream(imageBytes);
            
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = memoryStream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            
            GraphImage.Source = bitmapImage;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error generating graph image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateThresholdDisplay();
        UpdateGraphImage();
    }

    private void UpdateThresholdDisplay()
    {
        if (ThresholdValue != null)
        {
            ThresholdValue.Text = ThresholdSlider.Value.ToString("F2");
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        WasConfirmed = false;
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmSelection();
        }
        else if (e.Key == Key.Escape)
        {
            CancelButton_Click(sender, e);
        }
    }

    private void ConfirmSelection()
    {
        Threshold = ThresholdSlider.Value;
        WasConfirmed = true;
        DialogResult = true;
        Close();
    }
}
