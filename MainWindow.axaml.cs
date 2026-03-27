using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ImageVectorizer;

public partial class MainWindow : Window
{
    private string? _currentImagePath;
    private string? _vectorizedSvgContent;
    private Bitmap? _vectorizedBitmap;
    private bool _isProcessing;
    private readonly VectorizationEngine _engine;

    // UI Elements
    private Border? _dropZone;
    private Image? _originalImage;
    private Image? _resultImage;
    private StackPanel? _resultPlaceholder;
    private ProgressBar? _processingProgress;
    private TextBlock? _statusText;
    private Slider? _colorCountSlider;
    private Slider? _detailSlider;
    private Slider? _edgeSlider;
    private Slider? _smoothingSlider;
    private CheckBox? _preserveTransparency;
    private CheckBox? _antiAliasing;
    private CheckBox? _simplifyPaths;
    private Button? _vectorizeButton;
    private Button? _saveSvgButton;
    private Button? _savePngButton;
    private Button? _saveBothButton;
    private Button? _browseButton;
    private Button? _resetButton;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new VectorizationEngine();
        
        Loaded += OnLoaded;
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Get references to controls
        _dropZone = this.FindControl<Border>("DropZone");
        _originalImage = this.FindControl<Image>("OriginalImage");
        _resultImage = this.FindControl<Image>("ResultImage");
        _resultPlaceholder = this.FindControl<StackPanel>("ResultPlaceholder");
        _processingProgress = this.FindControl<ProgressBar>("ProcessingProgress");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _colorCountSlider = this.FindControl<Slider>("ColorCountSlider");
        _detailSlider = this.FindControl<Slider>("DetailSlider");
        _edgeSlider = this.FindControl<Slider>("EdgeSlider");
        _smoothingSlider = this.FindControl<Slider>("SmoothingSlider");
        _preserveTransparency = this.FindControl<CheckBox>("PreserveTransparency");
        _antiAliasing = this.FindControl<CheckBox>("AntiAliasing");
        _simplifyPaths = this.FindControl<CheckBox>("SimplifyPaths");
        _vectorizeButton = this.FindControl<Button>("VectorizeButton");
        _saveSvgButton = this.FindControl<Button>("SaveSvgButton");
        _savePngButton = this.FindControl<Button>("SavePngButton");
        _saveBothButton = this.FindControl<Button>("SaveBothButton");
        _browseButton = this.FindControl<Button>("BrowseButton");
        _resetButton = this.FindControl<Button>("ResetButton");

        // Wire up events
        if (_browseButton != null) _browseButton.Click += BrowseFiles_Click;
        if (_vectorizeButton != null) _vectorizeButton.Click += Vectorize_Click;
        if (_saveSvgButton != null) _saveSvgButton.Click += SaveSvg_Click;
        if (_savePngButton != null) _savePngButton.Click += SavePng_Click;
        if (_saveBothButton != null) _saveBothButton.Click += SaveBoth_Click;
        if (_resetButton != null) _resetButton.Click += Reset_Click;

        // Setup drag and drop
        if (_dropZone != null)
        {
            _dropZone.AddHandler(DragDrop.DropEvent, OnDrop);
            _dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }
        
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) 
            ? DragDropEffects.Copy 
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        
        var files = e.Data.GetFiles();
        if (files == null) return;

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path != null && IsImageFile(path))
            {
                await LoadImageAsync(path);
                break;
            }
        }
    }

    private async void BrowseFiles_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an image to vectorize",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Image files")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" }
                },
                new FilePickerFileType("All files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                await LoadImageAsync(path);
            }
        }
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);

            _currentImagePath = path;
            
            if (_originalImage != null)
            {
                _originalImage.Source = bitmap;
                _originalImage.IsVisible = true;
            }
            if (_dropZone != null) _dropZone.IsVisible = false;
            if (_vectorizeButton != null) _vectorizeButton.IsEnabled = true;

            // Reset result
            if (_resultImage != null) _resultImage.IsVisible = false;
            if (_resultPlaceholder != null) _resultPlaceholder.IsVisible = true;
            _vectorizedSvgContent = null;
            _vectorizedBitmap?.Dispose();
            _vectorizedBitmap = null;
            UpdateSaveButtons();

            if (_statusText != null) _statusText.Text = $"Loaded: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            if (_statusText != null) _statusText.Text = $"Error: {ex.Message}";
        }
    }

    private async void Vectorize_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentImagePath == null || _isProcessing) return;

        _isProcessing = true;
        if (_vectorizeButton != null) _vectorizeButton.IsEnabled = false;
        if (_processingProgress != null) _processingProgress.IsVisible = true;
        if (_statusText != null) _statusText.Text = "Vectorizing...";

        try
        {
            var settings = new VectorizationSettings
            {
                ColorCount = (int)(_colorCountSlider?.Value ?? 16),
                DetailLevel = (int)(_detailSlider?.Value ?? 5),
                EdgeSensitivity = (int)(_edgeSlider?.Value ?? 5),
                Smoothing = (int)(_smoothingSlider?.Value ?? 3),
                PreserveTransparency = _preserveTransparency?.IsChecked ?? true,
                AntiAliasing = _antiAliasing?.IsChecked ?? true,
                SimplifyPaths = _simplifyPaths?.IsChecked ?? true
            };

            var result = await Task.Run(() => _engine.Vectorize(_currentImagePath, settings));

            _vectorizedSvgContent = result.SvgContent;

            // Load the PNG preview
            if (result.PngBytes != null)
            {
                using var ms = new MemoryStream(result.PngBytes);
                _vectorizedBitmap = new Bitmap(ms);
            }

            if (_resultImage != null && _vectorizedBitmap != null)
            {
                _resultImage.Source = _vectorizedBitmap;
                _resultImage.IsVisible = true;
            }
            if (_resultPlaceholder != null) _resultPlaceholder.IsVisible = false;

            if (_statusText != null) 
                _statusText.Text = $"Vectorized! {result.PathCount} paths, {result.ColorCount} colors";
            UpdateSaveButtons();
        }
        catch (Exception ex)
        {
            if (_statusText != null) _statusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _isProcessing = false;
            if (_vectorizeButton != null) _vectorizeButton.IsEnabled = true;
            if (_processingProgress != null) _processingProgress.IsVisible = false;
        }
    }

    private async void SaveSvg_Click(object? sender, RoutedEventArgs e)
    {
        if (_vectorizedSvgContent == null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save SVG",
            DefaultExtension = "svg",
            SuggestedFileName = GetDefaultFileName("svg"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SVG files") { Patterns = new[] { "*.svg" } }
            }
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                await File.WriteAllTextAsync(path, _vectorizedSvgContent);
                if (_statusText != null) _statusText.Text = $"Saved: {Path.GetFileName(path)}";
            }
        }
    }

    private async void SavePng_Click(object? sender, RoutedEventArgs e)
    {
        if (_vectorizedBitmap == null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save PNG",
            DefaultExtension = "png",
            SuggestedFileName = GetDefaultFileName("png"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PNG files") { Patterns = new[] { "*.png" } }
            }
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                _vectorizedBitmap.Save(path);
                if (_statusText != null) _statusText.Text = $"Saved: {Path.GetFileName(path)}";
            }
        }
    }

    private async void SaveBoth_Click(object? sender, RoutedEventArgs e)
    {
        if (_vectorizedSvgContent == null || _vectorizedBitmap == null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save vectorized image (will save both SVG and PNG)",
            DefaultExtension = "svg",
            SuggestedFileName = GetDefaultFileName("svg"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SVG files") { Patterns = new[] { "*.svg" } }
            }
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                var basePath = Path.ChangeExtension(path, null);
                var svgPath = basePath + ".svg";
                var pngPath = basePath + ".png";

                await File.WriteAllTextAsync(svgPath, _vectorizedSvgContent);
                _vectorizedBitmap.Save(pngPath);

                if (_statusText != null) 
                    _statusText.Text = $"Saved: {Path.GetFileName(svgPath)} and {Path.GetFileName(pngPath)}";
            }
        }
    }

    private void Reset_Click(object? sender, RoutedEventArgs e)
    {
        // Reset sliders
        if (_colorCountSlider != null) _colorCountSlider.Value = 16;
        if (_detailSlider != null) _detailSlider.Value = 5;
        if (_edgeSlider != null) _edgeSlider.Value = 5;
        if (_smoothingSlider != null) _smoothingSlider.Value = 3;

        // Reset checkboxes
        if (_preserveTransparency != null) _preserveTransparency.IsChecked = true;
        if (_antiAliasing != null) _antiAliasing.IsChecked = true;
        if (_simplifyPaths != null) _simplifyPaths.IsChecked = true;

        // Reset images
        _currentImagePath = null;
        _vectorizedSvgContent = null;
        _vectorizedBitmap?.Dispose();
        _vectorizedBitmap = null;

        if (_originalImage != null)
        {
            _originalImage.Source = null;
            _originalImage.IsVisible = false;
        }
        if (_dropZone != null) _dropZone.IsVisible = true;

        if (_resultImage != null)
        {
            _resultImage.Source = null;
            _resultImage.IsVisible = false;
        }
        if (_resultPlaceholder != null) _resultPlaceholder.IsVisible = true;

        if (_vectorizeButton != null) _vectorizeButton.IsEnabled = false;
        UpdateSaveButtons();
        if (_statusText != null) _statusText.Text = "Ready";
    }

    private void UpdateSaveButtons()
    {
        var hasResult = _vectorizedSvgContent != null && _vectorizedBitmap != null;
        if (_saveSvgButton != null) _saveSvgButton.IsEnabled = hasResult;
        if (_savePngButton != null) _savePngButton.IsEnabled = hasResult;
        if (_saveBothButton != null) _saveBothButton.IsEnabled = hasResult;
    }

    private string GetDefaultFileName(string extension)
    {
        if (_currentImagePath == null) return $"vectorized.{extension}";
        var baseName = Path.GetFileNameWithoutExtension(_currentImagePath);
        return $"{baseName}_vectorized.{extension}";
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
    }
}
