using System.Drawing;
using Translator.Core.Models;
using Region = Translator.Core.Models.Region;

namespace Translator.Core.Interfaces;

public interface IScreenCaptureService
{
    Task<Bitmap> CaptureRegionAsync(Region region, CancellationToken cancellationToken = default);
}
