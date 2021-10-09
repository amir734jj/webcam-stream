using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WebcamStream.Models
{
    public class Payload
    {
        public ulong SequenceNumber { get; }
        
        public Image<Bgra32> Image { get; }

        public Payload(ulong sequenceNumber, Image<Bgra32> image)
        {
            SequenceNumber = sequenceNumber;
            Image = image;
        }
    }
}