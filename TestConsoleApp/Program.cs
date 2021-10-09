using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using WebcamStream;

namespace TestConsoleApp
{
    class Program
    {
        public static async Task Main(String[] args)
        {
            var logging = LoggerFactory.Create(x => x.AddConsole());
            var logger = logging.CreateLogger<ImageCapture>();
            var d = new ImageCapture(920, 720, TimeSpan.FromSeconds(1), logger);
            d.ImageHandler += (_, payload) =>
            {
                var stream = new MemoryStream();
                payload.Image.SaveAsJpeg(stream);
                File.WriteAllBytes($"../../../{payload.SequenceNumber:0000}.jpg", stream.ToArray());
            };

            await d.Run();
        }
    }
}