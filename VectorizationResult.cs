namespace ImageVectorizer;

public class VectorizationResult
{
    public required string SvgContent { get; set; }
    public byte[]? PngBytes { get; set; }
    public int PathCount { get; set; }
    public int ColorCount { get; set; }
}
