namespace StatePipes.ExplorerTypes
{
    public class SPEImage(SPEImageType imageType, IReadOnlyList<byte> imageBytes)
    {
        public SPEImageType ImageType { get; } = imageType;
        public IReadOnlyList<byte> ImageBytes { get; } = imageBytes;
    }
}
