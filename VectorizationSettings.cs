namespace ImageVectorizer;

public class VectorizationSettings
{
    public int ColorCount { get; set; } = 16;
    public int DetailLevel { get; set; } = 5;
    public int EdgeSensitivity { get; set; } = 5;
    public int Smoothing { get; set; } = 3;
    public bool PreserveTransparency { get; set; } = true;
    public bool AntiAliasing { get; set; } = true;
    public bool SimplifyPaths { get; set; } = true;
}
