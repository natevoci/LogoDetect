using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LogoDetect.Services;

public partial class BoundingBoxSelector : Window
{
    private const int MinBoundingSize = 16;
    
    private System.Drawing.Rectangle _selectedBounds;
    private bool _isDrawing = false;
    private bool _isResizing = false;
    private System.Windows.Point _startPoint;
    private ResizeHandle _currentHandle = ResizeHandle.None;
    
    public System.Drawing.Rectangle SelectedBounds => _selectedBounds;

    private enum ResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public BoundingBoxSelector(YData logoReference, System.Drawing.Rectangle initialBounds)
    {
        InitializeComponent();
        
        _selectedBounds = initialBounds;
        
        // Convert YData to WPF BitmapSource
        var bitmap = ConvertYDataToBitmap(logoReference);
        LogoImage.Source = bitmap;
        
        // Set canvas size to match image
        ImageCanvas.Width = bitmap.PixelWidth;
        ImageCanvas.Height = bitmap.PixelHeight;
        
        // Size window based on image dimensions
        SizeWindowToImage(bitmap.PixelWidth, bitmap.PixelHeight);
        
        // Show initial bounds
        UpdateSelectionRectangle();
    }

    private BitmapSource ConvertYDataToBitmap(YData yData)
    {
        using var memoryStream = new MemoryStream();
        yData.SaveBitmapToStream(memoryStream);
        memoryStream.Position = 0;
        
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = memoryStream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        
        return bitmapImage;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(ImageCanvas);
        
        // Check if clicking on a handle
        _currentHandle = GetHandleAtPoint(_startPoint);
        
        if (_currentHandle != ResizeHandle.None)
        {
            _isResizing = true;
        }
        else if (IsPointInSelection(_startPoint))
        {
            // Start dragging the selection
            _isDrawing = false;
        }
        else
        {
            // Start new selection
            _isDrawing = true;
            _selectedBounds = new System.Drawing.Rectangle(
                (int)_startPoint.X, 
                (int)_startPoint.Y, 
                0, 
                0);
        }
        
        ImageCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            // Update cursor based on handle hover
            var pos = e.GetPosition(ImageCanvas);
            var handle = GetHandleAtPoint(pos);
            UpdateCursor(handle);
            return;
        }

        var currentPoint = e.GetPosition(ImageCanvas);
        
        if (_isDrawing)
        {
            // Update selection rectangle
            var x = (int)Math.Min(_startPoint.X, currentPoint.X);
            var y = (int)Math.Min(_startPoint.Y, currentPoint.Y);
            var width = (int)Math.Abs(currentPoint.X - _startPoint.X);
            var height = (int)Math.Abs(currentPoint.Y - _startPoint.Y);
            
            _selectedBounds = new System.Drawing.Rectangle(x, y, width, height);
            UpdateSelectionRectangle();
        }
        else if (_isResizing)
        {
            ResizeSelection(currentPoint);
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = false;
        _isResizing = false;
        _currentHandle = ResizeHandle.None;
        ImageCanvas.ReleaseMouseCapture();
        
        // Ensure minimum size
        if (_selectedBounds.Width < MinBoundingSize)
            _selectedBounds.Width = MinBoundingSize;
        if (_selectedBounds.Height < MinBoundingSize)
            _selectedBounds.Height = MinBoundingSize;
            
        UpdateSelectionRectangle();
        UpdateStatusText();
    }

    private void ResizeSelection(System.Windows.Point currentPoint)
    {
        var bounds = _selectedBounds;
        
        switch (_currentHandle)
        {
            case ResizeHandle.TopLeft:
                var newLeft = (int)currentPoint.X;
                var newTop = (int)currentPoint.Y;
                var newRight = bounds.Right;
                var newBottom = bounds.Bottom;
                
                if (newRight - newLeft >= MinBoundingSize)
                {
                    bounds.X = newLeft;
                    bounds.Width = newRight - newLeft;
                }
                if (newBottom - newTop >= MinBoundingSize)
                {
                    bounds.Y = newTop;
                    bounds.Height = newBottom - newTop;
                }
                break;
                
            case ResizeHandle.TopRight:
                newTop = (int)currentPoint.Y;
                newBottom = bounds.Bottom;
                var newWidth = (int)currentPoint.X - bounds.X;
                
                if (newWidth >= MinBoundingSize)
                    bounds.Width = newWidth;
                if (newBottom - newTop >= MinBoundingSize)
                {
                    bounds.Y = newTop;
                    bounds.Height = newBottom - newTop;
                }
                break;
                
            case ResizeHandle.BottomLeft:
                newLeft = (int)currentPoint.X;
                newRight = bounds.Right;
                var newHeight = (int)currentPoint.Y - bounds.Y;
                
                if (newRight - newLeft >= MinBoundingSize)
                {
                    bounds.X = newLeft;
                    bounds.Width = newRight - newLeft;
                }
                if (newHeight >= MinBoundingSize)
                    bounds.Height = newHeight;
                break;
                
            case ResizeHandle.BottomRight:
                newWidth = (int)currentPoint.X - bounds.X;
                newHeight = (int)currentPoint.Y - bounds.Y;
                
                if (newWidth >= MinBoundingSize)
                    bounds.Width = newWidth;
                if (newHeight >= MinBoundingSize)
                    bounds.Height = newHeight;
                break;
        }
        
        // Clamp to image bounds
        if (bounds.X < 0) bounds.X = 0;
        if (bounds.Y < 0) bounds.Y = 0;
        if (bounds.Right > ImageCanvas.Width) bounds.Width = (int)ImageCanvas.Width - bounds.X;
        if (bounds.Bottom > ImageCanvas.Height) bounds.Height = (int)ImageCanvas.Height - bounds.Y;
        
        _selectedBounds = bounds;
        UpdateSelectionRectangle();
    }

    private ResizeHandle GetHandleAtPoint(System.Windows.Point point)
    {
        const double handleTolerance = 10;
        
        if (IsNearPoint(point, new System.Windows.Point(_selectedBounds.Left, _selectedBounds.Top), handleTolerance))
            return ResizeHandle.TopLeft;
        if (IsNearPoint(point, new System.Windows.Point(_selectedBounds.Right, _selectedBounds.Top), handleTolerance))
            return ResizeHandle.TopRight;
        if (IsNearPoint(point, new System.Windows.Point(_selectedBounds.Left, _selectedBounds.Bottom), handleTolerance))
            return ResizeHandle.BottomLeft;
        if (IsNearPoint(point, new System.Windows.Point(_selectedBounds.Right, _selectedBounds.Bottom), handleTolerance))
            return ResizeHandle.BottomRight;
            
        return ResizeHandle.None;
    }

    private bool IsNearPoint(System.Windows.Point p1, System.Windows.Point p2, double tolerance)
    {
        return Math.Abs(p1.X - p2.X) <= tolerance && Math.Abs(p1.Y - p2.Y) <= tolerance;
    }

    private bool IsPointInSelection(System.Windows.Point point)
    {
        return point.X >= _selectedBounds.Left && point.X <= _selectedBounds.Right &&
               point.Y >= _selectedBounds.Top && point.Y <= _selectedBounds.Bottom;
    }

    private void UpdateCursor(ResizeHandle handle)
    {
        ImageCanvas.Cursor = handle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
            ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.Arrow
        };
    }

    private void UpdateSelectionRectangle()
    {
        if (_selectedBounds.Width > 0 && _selectedBounds.Height > 0)
        {
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _selectedBounds.X);
            Canvas.SetTop(SelectionRect, _selectedBounds.Y);
            SelectionRect.Width = _selectedBounds.Width;
            SelectionRect.Height = _selectedBounds.Height;
            
            // Update handles
            UpdateHandle(TopLeftHandle, _selectedBounds.Left, _selectedBounds.Top);
            UpdateHandle(TopRightHandle, _selectedBounds.Right, _selectedBounds.Top);
            UpdateHandle(BottomLeftHandle, _selectedBounds.Left, _selectedBounds.Bottom);
            UpdateHandle(BottomRightHandle, _selectedBounds.Right, _selectedBounds.Bottom);
        }
    }

    private void UpdateHandle(System.Windows.Shapes.Ellipse handle, double x, double y)
    {
        handle.Visibility = Visibility.Visible;
        Canvas.SetLeft(handle, x - handle.Width / 2);
        Canvas.SetTop(handle, y - handle.Height / 2);
    }

    private void UpdateStatusText()
    {
        StatusText.Text = $"Selection: {_selectedBounds.Width} x {_selectedBounds.Height} at ({_selectedBounds.X}, {_selectedBounds.Y})";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBounds.Width < MinBoundingSize || _selectedBounds.Height < MinBoundingSize)
        {
            MessageBox.Show($"Selection must be at least {MinBoundingSize}x{MinBoundingSize} pixels.", 
                          "Invalid Selection", 
                          MessageBoxButton.OK, 
                          MessageBoxImage.Warning);
            return;
        }
        
        DialogResult = true;
        Close();
    }

    private void NoLogoButton_Click(object sender, RoutedEventArgs e)
    {
        // Reset to full image
        _selectedBounds = new System.Drawing.Rectangle(0, 0, (int)ImageCanvas.Width, (int)ImageCanvas.Height);
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void SizeWindowToImage(int imageWidth, int imageHeight)
    {
        // Get the screen's working area (excluding taskbar)
        var workingArea = SystemParameters.WorkArea;
        
        // Reserve space for window chrome, controls, and padding
        const double chromeHeight = 120; // Title bar + controls panel
        const double chromeWidth = 20;   // Window borders
        const double maxScreenPercentage = 0.9; // Use up to 90% of screen
        
        // Calculate maximum available space
        var maxWidth = workingArea.Width * maxScreenPercentage;
        var maxHeight = workingArea.Height * maxScreenPercentage - chromeHeight;
        
        // Calculate scale factor to fit image within available space
        var scaleWidth = maxWidth / imageWidth;
        var scaleHeight = maxHeight / imageHeight;
        var scale = Math.Min(Math.Min(scaleWidth, scaleHeight), 1.0); // Don't scale up, only down
        
        // Set window size
        Width = (imageWidth * scale) + chromeWidth;
        Height = (imageHeight * scale) + chromeHeight;
        
        // Ensure minimum size
        MinWidth = 400;
        MinHeight = 300;
    }
}
