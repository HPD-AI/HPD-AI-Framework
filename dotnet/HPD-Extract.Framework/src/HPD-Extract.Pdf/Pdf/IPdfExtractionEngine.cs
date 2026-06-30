using System.Threading;
using System.Threading.Tasks;
using HPD.Extract.Models;

namespace HPD.Extract.Pdf
{
    public interface IPdfExtractionEngine
    {
        ValueTask<PdfExtractionResult> ExtractAsync(
            ContentInput input,
            PdfExtractionOptions options,
            CancellationToken cancellationToken = default);
    }
}
