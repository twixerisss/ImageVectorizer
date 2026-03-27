# Image Vectorizer

A **cross-platform** desktop application that converts raster images (PNG, JPG, BMP, GIF) to scalable vector graphics (SVG) with PNG export.

Built with **Avalonia UI** - runs on **Linux**, **macOS**, and **Windows**!

## Features

- 🖼️ **Drag & Drop** - Simply drag images into the app
- 🎨 **Color Quantization** - Reduce colors (2-64) using median cut algorithm
- 🔍 **Adjustable Detail** - Control the level of detail in the output
- ✨ **Edge Detection** - Configurable edge sensitivity
- 🌊 **Path Smoothing** - Smooth curves for cleaner vectors
- 💾 **Export Options** - Save as SVG, PNG, or both
- 🐧 **Cross-Platform** - Works on Linux, macOS, and Windows

## Requirements

- .NET 8.0 SDK/Runtime

## Building & Running

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### From Command Line (Linux, macOS, Windows)

```bash
# Navigate to the project folder
cd ImageVectorizer

# Restore packages
dotnet restore

# Build
dotnet build

# Run
dotnet run
```

### With VS Code
1. Open the `ImageVectorizer` folder in VS Code
2. Install the C# Dev Kit extension
3. Press F5 to build and run

## Usage

1. **Load an Image**: Drag and drop an image onto the app, or click "Browse Files"
2. **Adjust Settings**:
   - **Color Count**: Number of colors in the output (2-64)
   - **Detail Level**: Higher = more detail, larger file
   - **Edge Sensitivity**: How aggressively edges are detected
   - **Path Smoothing**: Smoothness of vector curves
3. **Click "Vectorize"**: Process the image
4. **Save**: Export as SVG, PNG, or both

## Settings Explained

| Setting | Range | Description |
|---------|-------|-------------|
| Color Count | 2-64 | Number of distinct colors in the vector output |
| Detail Level | 1-10 | Higher values preserve more detail but create larger files |
| Edge Sensitivity | 1-10 | How strongly edges are detected and traced |
| Path Smoothing | 0-10 | Amount of curve smoothing applied to paths |

## Options

- **Preserve Transparency**: Maintain transparent areas from the original image
- **Anti-aliased Edges**: Smooth rendering of edges in the output
- **Simplify Paths**: Reduce number of points in paths (Douglas-Peucker algorithm)

## Technical Details

The vectorization process:
1. **Color Quantization**: Reduces the image to the specified number of colors using median cut
2. **Region Detection**: Groups pixels by color into distinct regions
3. **Contour Tracing**: Traces the outline of each color region
4. **Path Simplification**: Reduces path complexity using Douglas-Peucker algorithm
5. **Smoothing**: Applies curve smoothing for cleaner output
6. **SVG Generation**: Creates SVG with path elements for each region

## License

MIT License - Feel free to use and modify!
