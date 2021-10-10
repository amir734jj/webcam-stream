using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using WebcamStream;

namespace Api
{
    public class ImageStream : IDisposable
    {
        public EventHandler<string> ImageBase64Handler;
        
        private readonly ImageCapture imageCapture;

        public ImageStream(ILogger<ImageStream> logger)
        {
            imageCapture = new ImageCapture(640, 480, TimeSpan.FromMilliseconds(10), logger);

            imageCapture.ImageHandler += (_, payload) =>
            {
                var stream = new MemoryStream();
                payload.Image.SaveAsJpeg(stream);

                var base64 = Convert.ToBase64String(stream.ToArray());
                var image = $"data:image/jpg;base64,{base64}";
               
                ImageBase64Handler?.Invoke(null, image);
            };

            imageCapture.Run();
        }

        public void Dispose()
        {
            imageCapture.Dispose();
        }
    }
}