using HPD.Extract.Models;

namespace HPD.Extract.Pdf
{
    internal static class PdfPageQualityAnalyzer
    {
        private const int SparseTextLengthThreshold = 20;
        private const float LowCoverageThreshold = 0.15f;
        private const float GarbledScoreThreshold = 0.35f;

        public static PdfPageQuality Analyze(PdfPageQualityInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            var textItems = input.TextItems;
            var nativeTextLength = 0;
            var nonGarbledNativeTextLength = 0;
            var corruptNativeTextLength = 0;
            var unicodeMapErrorTextLength = 0;
            var nativeArea = 0f;
            var invisibleCount = 0;
            var nativeCount = 0;

            for (var i = 0; i < textItems.Count; i++)
            {
                var item = textItems[i];
                if (item.Layer is not (PdfTextLayerKind.Native or PdfTextLayerKind.InvisibleOcrLayer))
                {
                    continue;
                }

                nativeCount++;
                nativeTextLength += item.Text.Length;
                nativeArea += item.BoundingBox.Width * item.BoundingBox.Height;
                var corrupt = item.Font?.LooksCorrupt == true || item.HasUnicodeMapError == true;
                if (corrupt)
                {
                    corruptNativeTextLength += item.Text.Length;
                }

                if (item.HasUnicodeMapError == true)
                {
                    unicodeMapErrorTextLength += item.Text.Length;
                }

                if (item.Layer == PdfTextLayerKind.InvisibleOcrLayer)
                {
                    invisibleCount++;
                }

                if (!corrupt && !PdfGarbledTextDetector.IsLikelyGarbled(item.Text))
                {
                    nonGarbledNativeTextLength += item.Text.Length;
                }
            }

            var pageArea = input.PageSize.Width * input.PageSize.Height;
            var coverage = pageArea > 0 ? nativeArea / pageArea : 0;
            var invisibleRatio = nativeCount > 0 ? (float)invisibleCount / nativeCount : 0;
            var garbledScore = PdfGarbledTextDetector.ScorePage(textItems);
            var imageCount = input.ImageRegions.Count;
            var ocrRelevantImageCount = input.ImageRegions.Count(static image => image.IsOcrRelevant);
            var hasImages = imageCount > 0;
            var hasOcrRelevantImages = ocrRelevantImageCount > 0;
            var sparse = nonGarbledNativeTextLength < SparseTextLengthThreshold;
            var lowCoverage = coverage < LowCoverageThreshold;
            var corruptRatio = nativeTextLength > 0 ? (float)corruptNativeTextLength / nativeTextLength : 0;
            var looksGarbled = garbledScore >= GarbledScoreThreshold
                || corruptNativeTextLength >= SparseTextLengthThreshold && corruptRatio >= GarbledScoreThreshold;

            return new PdfPageQuality
            {
                NativeTextLength = nativeTextLength,
                NonGarbledNativeTextLength = nonGarbledNativeTextLength,
                CorruptNativeTextLength = corruptNativeTextLength,
                UnicodeMapErrorTextLength = unicodeMapErrorTextLength,
                NativeTextCoverage = coverage,
                InvisibleTextRatio = invisibleRatio,
                GarbledScore = garbledScore,
                EmbeddedImageCount = imageCount,
                OcrRelevantImageCount = ocrRelevantImageCount,
                HasEmbeddedImages = hasImages,
                HasOcrRelevantImages = hasOcrRelevantImages,
                LooksScanned = sparse && hasOcrRelevantImages,
                LooksGarbled = looksGarbled,
                NeedsOcr = sparse || lowCoverage || hasOcrRelevantImages || looksGarbled
            };
        }

        public static PdfOcrDecision PlanOcr(PdfPageQuality quality, PdfExtractionOptions options)
        {
            if (!options.OcrEnabled)
            {
                return PdfOcrDecision.NoOcr;
            }

            var reasons = new List<PdfOcrDecisionReason>();
            if (quality.NonGarbledNativeTextLength < SparseTextLengthThreshold)
            {
                reasons.Add(PdfOcrDecisionReason.SparseNativeText);
            }

            if (quality.NativeTextCoverage < LowCoverageThreshold)
            {
                reasons.Add(PdfOcrDecisionReason.LowNativeTextCoverage);
            }

            if (quality.HasOcrRelevantImages)
            {
                reasons.Add(PdfOcrDecisionReason.EmbeddedImages);
            }

            if (quality.LooksGarbled)
            {
                reasons.Add(PdfOcrDecisionReason.GarbledNativeText);
            }

            if (reasons.Count == 0)
            {
                return PdfOcrDecision.NoOcr;
            }

            var failurePolicy = quality.NonGarbledNativeTextLength < SparseTextLengthThreshold
                || quality.NativeTextCoverage < LowCoverageThreshold
                || quality.LooksGarbled
                    ? PdfOcrFailurePolicy.FailIfAllOcrFails
                    : PdfOcrFailurePolicy.BestEffortEnrichment;

            return new PdfOcrDecision
            {
                ShouldRun = true,
                Reasons = reasons,
                FailurePolicy = failurePolicy
            };
        }
    }
}
