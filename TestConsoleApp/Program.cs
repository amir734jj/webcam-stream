using System;
using System.IO;
using System.Linq;
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
            var imageCapture = new ImageCapture(logger);

            var devices = imageCapture.GetVideoDevices();
            var selectedDevice = devices.First();
            
            imageCapture.ImageHandler += (_, payload) =>
            {
                var stream = new MemoryStream();
                payload.Image.SaveAsJpeg(stream);
                File.WriteAllBytes($"../../../{payload.SequenceNumber:0000}.jpg", stream.ToArray());
            };

            await imageCapture.Run(selectedDevice, 640, 480, TimeSpan.FromSeconds(1));
        }
    }
}