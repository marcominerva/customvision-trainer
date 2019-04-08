using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomVisionTrainer.Extensions
{
    public static class StreamExtensions
    {
        public static byte[] ToByteArray(this Stream input)
        {
            input.Position = 0;
            var buffer = new byte[16 * 1024];
            using (var ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }

                return ms.ToArray();
            }
        }

        public static Task<Stream> ResizeImageAsync(this Stream image, int width, int height)
        {
            // Read image from stream
            using (var output = new MagickImage(image))
            {
                // The image will be resized to fit inside the specified size.
                var size = new MagickGeometry(width, height)
                {
                    Greater = true
                };
                output.Resize(size);

                var ms = new MemoryStream();
                output.Write(ms);
                ms.Position = 0;

                return Task.FromResult(ms as Stream);
            }
        }
    }
}
