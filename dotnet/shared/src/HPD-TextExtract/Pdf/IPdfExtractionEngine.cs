using System.Threading;
using System.Threading.Tasks;
using HPD.TextExtract.Models;

namespace HPD.TextExtract.Pdf
{
    public interface IPdfExtractionEngine
    {
        ValueTask<PdfExtractionResult> ExtractAsync(
            ContentInput input,
            PdfExtractionOptions options,
            CancellationToken cancellationToken = default);
    }
}
