using Microsoft.Cognitive.CustomVision.Training.Models;

namespace CustomVisionTrainer
{
    public class ImageDto
    {
        public string FullName { get; set; }
        public string FileName { get; set; }
        public byte[] Content { get; set; }
        public Tag Tag { get; set; }
    }
}
