# webcam-stream
Stream webcam as image using LibVLCSharp

```csharp
var logging = LoggerFactory.Create(x => x.AddConsole());
var logger = logging.CreateLogger<ImageCapture>();

// width, height, spin wait between each frame, logger instance
var imageCapture = new ImageCapture(920, 720, TimeSpan.FromSeconds(1), logger);
imageCapture.ImageHandler += (_, payload) =>
{
    var stream = new MemoryStream();
    payload.Image.SaveAsJpeg(stream);
    File.WriteAllBytes($"../../../{payload.SequenceNumber:0000}.jpg", stream.ToArray());
};

await imageCapture.Run();
```
