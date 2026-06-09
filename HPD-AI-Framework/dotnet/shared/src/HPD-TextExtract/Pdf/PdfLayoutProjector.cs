using System.Text;
using HPD.TextExtract.Models;

namespace HPD.TextExtract.Pdf
{
    internal static class PdfLayoutProjector
    {
        private const int FloatingSpaces = 2;
        private const int ColumnSpaces = 4;
        private const int FlowingMaxTotalAnchors = 4;
        private const int FlowingMaxLeftAnchors = 3;
        private const int FlowingMinLines = 3;
        private const float FlowingWideLineRatio = 0.5f;
        private const float FlowingWideLineThreshold = 0.6f;
        private const float FlowingColumnGapMultiplier = 4.0f;
        private const int FlowingMinLineItems = 3;
        private const float FlowingSpaceHeightRatio = 0.15f;
        private const float FlowingSpaceMinThreshold = 0.3f;
        private const int FlowingMaxIndent = 8;

        public static PdfLayoutProjectionResult Project(PdfLayoutProjectionInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            var items = CreateProjectionItems(input.TextItems);
            if (items.Count == 0)
            {
                return new PdfLayoutProjectionResult
                {
                    Metrics =
                    {
                        ["projectionVersion"] = "layout-reconstruction-v1",
                        ["pageNumber"] = input.PageNumber,
                        ["rotation"] = input.Rotation,
                        ["sourceItemCount"] = input.TextItems.Count
                    }
                };
            }

            var dotNoiseRemovedCount = RemoveExcessDotNoise(items);
            if (items.Count == 0)
            {
                return new PdfLayoutProjectionResult
                {
                    Metrics =
                    {
                        ["projectionVersion"] = "layout-reconstruction-v1",
                        ["pageNumber"] = input.PageNumber,
                        ["rotation"] = input.Rotation,
                        ["sourceItemCount"] = input.TextItems.Count,
                        ["dotNoiseRemovedCount"] = dotNoiseRemovedCount,
                        ["dotNoiseRemovedAll"] = true
                    }
                };
            }

            RoundWorkingDimensions(items);
            var medianWidth = Median(items, static item =>
            {
                var chars = Math.Max(1, CharLength(item.Text));
                return item.Box.Width > 0 ? item.Box.Width / chars : 1;
            });
            var medianHeight = Median(items, static item => item.Box.Height);
            var diagnostics = new ProjectionDiagnostics
            {
                SourceItemCount = input.TextItems.Count
            };
            diagnostics.DotNoiseRemovedCount = dotNoiseRemovedCount;

            NormalizeRotationReadingOrder(items, input.PageSize.Height, diagnostics);
            var lines = FormLines(items, medianWidth, medianHeight, input.PageSize.Width, diagnostics);
            if (lines.Count == 0)
            {
                return new PdfLayoutProjectionResult
                {
                    Metrics =
                    {
                        ["projectionVersion"] = "layout-reconstruction-v1",
                        ["pageNumber"] = input.PageNumber,
                        ["rotation"] = input.Rotation,
                        ["sourceItemCount"] = input.TextItems.Count,
                        ["medianCharacterWidth"] = medianWidth,
                        ["medianTextHeight"] = medianHeight
                    }
                };
            }

            var blocks = SegmentBlocks(lines);
            var rawLines = RenderLines(lines, blocks, input.PageSize, medianWidth, medianHeight, diagnostics);
            for (var i = 0; i < blocks.Count; i++)
            {
                FixSparseBlock(rawLines, blocks[i].Start, blocks[i].End, diagnostics);
            }

            var flattened = Flatten(lines);
            CleanProjectedItems(flattened, input.PageSize.Width, diagnostics);
            var text = CleanRenderedText(string.Join('\n', rawLines.Select(static line => (line ?? string.Empty).TrimEnd())));

            return new PdfLayoutProjectionResult
            {
                Text = text,
                ProjectedItems = flattened.Select(static item => item.ToPdfTextItem()).ToArray(),
                Metrics =
                {
                    ["projectionVersion"] = "layout-reconstruction-v1",
                    ["pageNumber"] = input.PageNumber,
                    ["rotation"] = input.Rotation,
                    ["sourceItemCount"] = diagnostics.SourceItemCount,
                    ["projectedItemCount"] = flattened.Count,
                    ["medianCharacterWidth"] = medianWidth,
                    ["medianTextHeight"] = medianHeight,
                    ["lineCount"] = lines.Count,
                    ["blockCount"] = blocks.Count,
                    ["flowBlockCount"] = diagnostics.FlowBlockCount,
                    ["gridBlockCount"] = diagnostics.GridBlockCount,
                    ["flowLineCount"] = diagnostics.FlowLineCount,
                    ["leftAnchorCount"] = diagnostics.LeftAnchorCount,
                    ["rightAnchorCount"] = diagnostics.RightAnchorCount,
                    ["centerAnchorCount"] = diagnostics.CenterAnchorCount,
                    ["rotatedItemCount"] = diagnostics.RotatedItemCount,
                    ["marginLineNumberCount"] = diagnostics.MarginLineNumberCount,
                    ["marginLineNumbersRemoved"] = diagnostics.MarginLineNumbersRemoved,
                    ["forceFloatingCount"] = diagnostics.ForceFloatingCount,
                    ["sparseBlockCompressionCount"] = diagnostics.SparseBlockCompressionCount,
                    ["dotNoiseRemovedCount"] = diagnostics.DotNoiseRemovedCount,
                    ["continuousMergeCount"] = diagnostics.ContinuousMergeCount,
                    ["wordMergeCount"] = diagnostics.WordMergeCount
                }
            };
        }

        private static List<ProjectionItem> CreateProjectionItems(IReadOnlyList<PdfTextItem> source)
        {
            var items = new List<ProjectionItem>(source.Count);
            for (var i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (string.IsNullOrEmpty(item.Text))
                {
                    continue;
                }

                items.Add(new ProjectionItem(item, i));
            }

            return items;
        }

        private static int RemoveExcessDotNoise(List<ProjectionItem> items)
        {
            var dotCount = items.Count(static item => item.Text.All(static c => c is '.' or '\u00b7' or '\u2022'));
            if (dotCount <= 100 || dotCount <= items.Count * 0.05)
            {
                return 0;
            }

            return items.RemoveAll(static item => item.Text.All(static c => c is '.' or '\u00b7' or '\u2022'));
        }

        private static void RoundWorkingDimensions(List<ProjectionItem> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.Box = item.Box with
                {
                    Width = MathF.Round(item.Box.Width),
                    Height = MathF.Round(item.Box.Height)
                };
            }
        }

        private static void NormalizeRotationReadingOrder(
            List<ProjectionItem> items,
            float pageHeight,
            ProjectionDiagnostics diagnostics)
        {
            if (!items.Any(static item => CanonicalRotation(item.Rotation) != 0))
            {
                return;
            }

            var byRotation = new Dictionary<int, List<int>>();
            for (var i = 0; i < items.Count; i++)
            {
                var rotation = CanonicalRotation(items[i].Rotation);
                if (!byRotation.TryGetValue(rotation, out var group))
                {
                    group = new List<int>();
                    byRotation.Add(rotation, group);
                }

                group.Add(i);
            }

            var groups = new List<List<int>>();
            foreach (var (rotation, group) in byRotation)
            {
                group.Sort((a, b) => items[a].Box.Y.CompareTo(items[b].Box.Y));
                if ((rotation == 90 || rotation == 270) && group.Count > 1)
                {
                    var maxHeight = group.Max(index => items[index].Box.Height);
                    var threshold = maxHeight * 3;
                    var cluster = new List<int> { group[0] };
                    for (var i = 1; i < group.Count; i++)
                    {
                        var previous = items[group[i - 1]].Box;
                        var current = items[group[i]].Box;
                        if (current.Y - previous.Bottom > threshold)
                        {
                            groups.Add(cluster);
                            cluster = new List<int>();
                        }

                        cluster.Add(group[i]);
                    }

                    if (cluster.Count > 0)
                    {
                        groups.Add(cluster);
                    }
                }
                else
                {
                    groups.Add(group);
                }
            }

            foreach (var group in groups)
            {
                group.Sort((a, b) => items[a].Box.Y.CompareTo(items[b].Box.Y));
            }

            groups.Sort((a, b) => a.Min(index => items[index].Box.X).CompareTo(b.Min(index => items[index].Box.X)));

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                if (group.Count == 0)
                {
                    continue;
                }

                var rotation = CanonicalRotation(items[group[0]].Rotation);
                if (rotation != 90 && rotation != 270)
                {
                    continue;
                }

                var overlapsOtherText = RotationGroupOverlapsOtherText(items, group, rotation);
                if (overlapsOtherText)
                {
                    var averageCenterY = group.Average(index => items[index].Box.Y + items[index].Box.Height / 2);
                    var averageWidth = group.Average(index => items[index].Box.Width);
                    var commonY = averageCenterY - averageWidth / 2;

                    foreach (var index in group)
                    {
                        var item = items[index];
                        ApplyDeferredY(item);
                        item.Box = item.Box with { Y = commonY, Height = averageWidth };
                        item.Rotation = 0;
                        item.IsRotated = true;
                        diagnostics.RotatedItemCount++;
                    }
                }
                else
                {
                    var groupMaxX = group.Max(index => items[index].Box.Right);
                    var deltaY = 0f;
                    if (groupIndex != 0)
                    {
                        deltaY = groups[groupIndex - 1].Max(index => items[index].Box.Bottom) + pageHeight;
                    }

                    if (rotation == 90)
                    {
                        foreach (var index in group)
                        {
                            var box = items[index].Box;
                            items[index].Box = new BoundingBox(MathF.Round(box.Y), box.X + deltaY, box.Height, box.Width);
                            items[index].Rotation = 0;
                            items[index].IsRotated = true;
                            diagnostics.RotatedItemCount++;
                        }
                    }
                    else
                    {
                        var maxY = group.Max(index => items[index].Box.Bottom);
                        foreach (var index in group)
                        {
                            var box = items[index].Box;
                            items[index].Box = new BoundingBox(MathF.Round(maxY - box.Y - box.Height), box.X + deltaY, box.Height, box.Width);
                            items[index].Rotation = 0;
                            items[index].IsRotated = true;
                            diagnostics.RotatedItemCount++;
                        }
                    }

                    var globalDelta = deltaY + groupMaxX + pageHeight;
                    for (var nextGroupIndex = groupIndex + 1; nextGroupIndex < groups.Count; nextGroupIndex++)
                    {
                        foreach (var index in groups[nextGroupIndex])
                        {
                            var item = items[index];
                            var nextRotation = CanonicalRotation(item.Rotation);
                            if (nextRotation == 90 || nextRotation == 270)
                            {
                                item.DeferredY += globalDelta;
                            }
                            else
                            {
                                item.Box = item.Box with { Y = item.Box.Y + globalDelta };
                            }
                        }
                    }
                }
            }

            foreach (var group in groups)
            {
                if (group.Count == 0 || CanonicalRotation(items[group[0]].Rotation) != 180)
                {
                    continue;
                }

                foreach (var index in group.OrderBy(index => items[index].Box.X))
                {
                    items[index].Rotation = 0;
                    items[index].IsRotated = true;
                    diagnostics.RotatedItemCount++;
                }
            }

            items.Sort(static (a, b) => a.Box.Y.CompareTo(b.Box.Y));
        }

        private static bool RotationGroupOverlapsOtherText(List<ProjectionItem> items, List<int> group, int rotation)
        {
            for (var otherIndex = 0; otherIndex < items.Count; otherIndex++)
            {
                if (CanonicalRotation(items[otherIndex].Rotation) == rotation)
                {
                    continue;
                }

                var other = items[otherIndex].Box;
                foreach (var groupIndex in group)
                {
                    if (groupIndex == otherIndex)
                    {
                        continue;
                    }

                    var box = items[groupIndex].Box;
                    var margin = Math.Max(box.Height, other.Height);
                    var xOverlap = box.X < other.Right && box.Right > other.X;
                    var yOverlap = box.Y < other.Bottom + margin && box.Bottom + margin > other.Y;
                    if (xOverlap && yOverlap)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ApplyDeferredY(ProjectionItem item)
        {
            if (item.DeferredY == 0)
            {
                return;
            }

            item.Box = item.Box with { Y = item.Box.Y + item.DeferredY };
            item.DeferredY = 0;
        }

        private static List<List<ProjectionItem>> FormLines(
            List<ProjectionItem> items,
            float medianWidth,
            float medianHeight,
            float pageWidth,
            ProjectionDiagnostics diagnostics)
        {
            MarkMarginLineNumbers(items, pageWidth, diagnostics);
            var yTolerance = Math.Max(5, medianHeight * 0.5f);
            items.Sort((a, b) =>
            {
                var ay = (int)MathF.Round(a.Box.Y / yTolerance);
                var by = (int)MathF.Round(b.Box.Y / yTolerance);
                return ay != by ? ay.CompareTo(by) : a.Box.X.CompareTo(b.Box.X);
            });

            MergeContinuousItems(items, diagnostics);

            var lines = new List<List<ProjectionItem>>();
            var current = new List<ProjectionItem>();
            var minY = float.PositiveInfinity;
            var maxY = float.NegativeInfinity;
            foreach (var item in items)
            {
                if (current.Count == 0)
                {
                    current.Add(item);
                    minY = item.Box.Y;
                    maxY = item.Box.Bottom;
                    continue;
                }

                var collision = current.Any(lineItem =>
                {
                    var overlap = Math.Min(lineItem.Box.Right, item.Box.Right) - Math.Max(lineItem.Box.X, item.Box.X);
                    return overlap > Math.Max(5, medianWidth / 3);
                });
                var marginMismatch = current.Any(static lineItem => lineItem.IsMarginLineNumber) != item.IsMarginLineNumber;
                var proposedMin = Math.Min(minY, item.Box.Y);
                var proposedMax = Math.Max(maxY, item.Box.Bottom);
                var tooTall = proposedMax - proposedMin > medianHeight * 1.8f;
                var rotatedTolerance = item.IsRotated && Math.Abs(item.Box.Y - minY) < Math.Max(20, medianHeight * 2);
                var midpoint = item.Box.Y + item.Box.Height * 0.5f;
                var insideLine = midpoint >= minY && midpoint <= maxY || item.Box.Y >= minY && item.Box.Y <= maxY;

                if (!collision && !marginMismatch && !tooTall && (rotatedTolerance || insideLine))
                {
                    current.Add(item);
                    minY = proposedMin;
                    maxY = proposedMax;
                }
                else
                {
                    AddSortedLine(lines, current);
                    current = new List<ProjectionItem> { item };
                    minY = item.Box.Y;
                    maxY = item.Box.Bottom;
                }
            }

            AddSortedLine(lines, current);
            lines.Sort(static (a, b) => FirstY(a).CompareTo(FirstY(b)));
            MergeWords(lines, diagnostics);
            MergeVerticallyOverlappingLines(lines);
            InsertBlankLines(lines, medianHeight);
            return lines;
        }

        private static void MarkMarginLineNumbers(List<ProjectionItem> items, float pageWidth, ProjectionDiagnostics diagnostics)
        {
            if (pageWidth <= 0)
            {
                return;
            }

            var midpoint = pageWidth / 2;
            var left = midpoint - 5;
            var right = midpoint + 20;
            foreach (var item in items)
            {
                var center = item.Box.X + item.Box.Width / 2;
                if (center > left && center < right && LooksLikeMarginLineNumber(item.Text) && item.Box.Width < 15)
                {
                    item.IsMarginLineNumber = true;
                    diagnostics.MarginLineNumberCount++;
                }
            }
        }

        private static bool LooksLikeMarginLineNumber(string text)
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 3)
            {
                return false;
            }

            var digitCount = 0;
            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                if (char.IsAsciiDigit(c))
                {
                    digitCount++;
                }
                else if (c == 'O' && i == trimmed.Length - 1)
                {
                    continue;
                }
                else
                {
                    return false;
                }
            }

            return digitCount is >= 1 and <= 2;
        }

        private static void MergeContinuousItems(List<ProjectionItem> items, ProjectionDiagnostics diagnostics)
        {
            var merged = new List<ProjectionItem>(items.Count);
            foreach (var item in items)
            {
                var previous = merged.LastOrDefault();
                if (previous is not null &&
                    Math.Abs(item.Box.Y - previous.Box.Y) <= 0.1f &&
                    Math.Abs(item.Box.Height - previous.Box.Height) <= 0.1f)
                {
                    var deltaX = item.Box.X - previous.Box.Right;
                    if (deltaX is >= -0.5f and < 0.1f)
                    {
                        previous.Absorb(item, insertSpace: false);
                        diagnostics.ContinuousMergeCount++;
                        continue;
                    }
                }

                merged.Add(item);
            }

            items.Clear();
            items.AddRange(merged);
        }

        private static void MergeWords(List<List<ProjectionItem>> lines, ProjectionDiagnostics diagnostics)
        {
            foreach (var line in lines)
            {
                var merged = new List<ProjectionItem>(line.Count);
                foreach (var item in line)
                {
                    var previous = merged.LastOrDefault();
                    if (previous is not null)
                    {
                        var bothNumbers = LooksLikeTableNumber(previous.Text) && LooksLikeTableNumber(item.Text);
                        var deltaX = item.Box.X - previous.Box.Right;
                        var yCompatible = Math.Abs(item.Box.Y - previous.Box.Y) <= 1.5f;
                        if (yCompatible && !bothNumbers && deltaX <= 1)
                        {
                            previous.Absorb(item, insertSpace: false);
                            diagnostics.WordMergeCount++;
                            continue;
                        }

                        var previousCharWidth = previous.Box.Width / Math.Max(1, CharLength(previous.Text));
                        if (yCompatible && !bothNumbers && deltaX < previousCharWidth)
                        {
                            previous.Absorb(item, insertSpace: !previous.Text.EndsWith(" ", StringComparison.Ordinal));
                            diagnostics.WordMergeCount++;
                            continue;
                        }
                    }

                    merged.Add(item);
                }

                line.Clear();
                line.AddRange(merged);
            }
        }

        private static bool LooksLikeTableNumber(string text)
        {
            var trimmed = text.Trim();
            if (CharLength(trimmed) < 2)
            {
                return false;
            }

            var index = 0;
            if (index < trimmed.Length && trimmed[index] == '$')
            {
                index++;
            }

            if (index < trimmed.Length && trimmed[index] == '-')
            {
                index++;
            }

            var hasDigit = false;
            var hasDecimal = false;
            for (; index < trimmed.Length; index++)
            {
                var c = trimmed[index];
                if (char.IsAsciiDigit(c))
                {
                    hasDigit = true;
                }
                else if (c == ',')
                {
                    continue;
                }
                else if (c == '.')
                {
                    if (hasDecimal)
                    {
                        return false;
                    }

                    hasDecimal = true;
                }
                else if (c == '%')
                {
                    return hasDigit && index == trimmed.Length - 1;
                }
                else
                {
                    return false;
                }
            }

            return hasDigit;
        }

        private static void MergeVerticallyOverlappingLines(List<List<ProjectionItem>> lines)
        {
            var i = 1;
            while (i < lines.Count)
            {
                if (lines[i - 1].Count == 0 || lines[i].Count == 0)
                {
                    i++;
                    continue;
                }

                var previousTop = lines[i - 1].Min(static item => item.Box.Y);
                var previousBottom = lines[i - 1].Max(static item => item.Box.Bottom);
                var currentTop = lines[i].Min(static item => item.Box.Y);
                var currentBottom = lines[i].Max(static item => item.Box.Bottom);
                var overlapsVertically = previousBottom > currentTop && previousTop < currentBottom;
                var overlapsHorizontally = lines[i].Any(current =>
                    lines[i - 1].Any(previous => current.Box.X >= previous.Box.X && current.Box.X <= previous.Box.Right ||
                                                 previous.Box.X >= current.Box.X && previous.Box.X <= current.Box.Right));

                if (overlapsVertically && !overlapsHorizontally)
                {
                    lines[i - 1].AddRange(lines[i]);
                    lines[i - 1].Sort(static (a, b) => a.Box.X.CompareTo(b.Box.X));
                    lines.RemoveAt(i);
                    continue;
                }

                i++;
            }
        }

        private static void InsertBlankLines(List<List<ProjectionItem>> lines, float medianHeight)
        {
            var i = 1;
            while (i < lines.Count)
            {
                var previous = RepresentativeLineMetrics(lines[i - 1], medianHeight);
                var current = RepresentativeLineMetrics(lines[i], medianHeight);
                var yDelta = current.Top - previous.Bottom;
                var referenceHeight = Math.Max(medianHeight, Math.Min(previous.Height, current.Height));
                if (yDelta > referenceHeight)
                {
                    var blanks = Math.Clamp((int)MathF.Round(yDelta / referenceHeight) - 1, 1, 10);
                    for (var j = 0; j < blanks; j++)
                    {
                        lines.Insert(i, new List<ProjectionItem>());
                        i++;
                    }
                }

                i++;
            }
        }

        private static IReadOnlyList<LineRange> SegmentBlocks(List<List<ProjectionItem>> lines)
        {
            var blocks = new List<LineRange>();
            int? start = null;
            var emptyCount = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Count == 0)
                {
                    emptyCount++;
                    if (emptyCount > 1)
                    {
                        if (start is int blockStart)
                        {
                            blocks.Add(new LineRange(blockStart, i + 1));
                        }

                        start = null;
                    }
                }
                else
                {
                    emptyCount = 0;
                    start ??= i;
                }
            }

            if (start is int finalStart)
            {
                blocks.Add(new LineRange(finalStart, lines.Count));
            }

            if (blocks.Count == 0 && lines.Count > 0)
            {
                blocks.Add(new LineRange(0, lines.Count));
            }

            return blocks;
        }

        private static string[] RenderLines(
            List<List<ProjectionItem>> lines,
            IReadOnlyList<LineRange> blocks,
            PageSize pageSize,
            float medianWidth,
            float medianHeight,
            ProjectionDiagnostics diagnostics)
        {
            var rawLines = new string[lines.Count];
            var forwardLeft = new SortedDictionary<int, int>();
            var forwardRight = new SortedDictionary<int, int>();
            var forwardCenter = new SortedDictionary<int, int>();
            var forwardFloating = new SortedDictionary<int, int>();

            foreach (var block in blocks)
            {
                var anchors = AnalyzeAnchors(lines, block, pageSize, diagnostics);
                if (IsFlowingTextBlock(lines, block, anchors, pageSize.Width, medianWidth))
                {
                    RenderFlowingBlock(lines, block, rawLines, medianWidth, diagnostics);
                    diagnostics.FlowBlockCount++;
                    continue;
                }

                diagnostics.GridBlockCount++;
                AssignAnchors(anchors);
                ResolveSnaps(lines, block, anchors);
                ApplyJustifiedTextFixups(lines, block, anchors, pageSize.Width, diagnostics);
                DetectAndRenderFlowingLines(lines, block, rawLines, medianWidth, pageSize.Width, diagnostics);
                ComputeSpacingHints(lines, block, medianWidth, pageSize.Width);
                RenderGridBlock(lines, block, rawLines, anchors, medianWidth, forwardLeft, forwardRight, forwardCenter, forwardFloating);
            }

            AlignRotatedFloatingItems(lines, blocks, rawLines, medianWidth);
            return rawLines;
        }

        private static AnchorSet AnalyzeAnchors(
            List<List<ProjectionItem>> lines,
            LineRange block,
            PageSize pageSize,
            ProjectionDiagnostics diagnostics)
        {
            var anchors = new AnchorSet();
            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                for (var itemIndex = 0; itemIndex < lines[lineIndex].Count; itemIndex++)
                {
                    var item = lines[lineIndex][itemIndex];
                    if (item.IsRotated)
                    {
                        continue;
                    }

                    GetOrAdd(anchors.Left, AnchorKey(item.Box.X)).Add(new ItemRef(lineIndex, itemIndex, lines));
                    GetOrAdd(anchors.Right, AnchorKey(item.Box.Right)).Add(new ItemRef(lineIndex, itemIndex, lines));
                    GetOrAdd(anchors.Center, AnchorKey(item.Box.X + item.Box.Width * 0.5f)).Add(new ItemRef(lineIndex, itemIndex, lines));
                }
            }

            MergeNearbyAnchorGroups(anchors.Left);
            MergeNearbyAnchorGroups(anchors.Right);
            MergeNearbyAnchorGroups(anchors.Center);
            DeltaMinFilter(anchors.Left, lines, pageSize.Height, 0.25f);
            DeltaMinFilter(anchors.Right, lines, pageSize.Height, 0.17f);
            DeltaMinFilter(anchors.Center, lines, pageSize.Height, 0.05f);
            InterceptFilter(anchors.Left, lines);
            InterceptFilter(anchors.Right, lines);
            InterceptFilter(anchors.Center, lines);
            AlignFloatingToNearbyAnchors(anchors.Left, lines, block, 4, static item => item.Box.X, static item => AnchorKey(item.Box.X));
            AlignFloatingToNearbyAnchors(anchors.Right, lines, block, 4, static item => item.Box.Right, static item => AnchorKey(item.Box.Right));
            AlignFloatingToNearbyAnchors(anchors.Center, lines, block, 4, static item => item.Box.X + item.Box.Width * 0.5f, static item => AnchorKey(item.Box.X + item.Box.Width * 0.5f));
            RemoveSingletons(anchors.Left);
            RemoveSingletons(anchors.Right);
            RemoveSingletons(anchors.Center);
            diagnostics.LeftAnchorCount += anchors.Left.Count;
            diagnostics.RightAnchorCount += anchors.Right.Count;
            diagnostics.CenterAnchorCount += anchors.Center.Count;
            return anchors;
        }

        private static void AssignAnchors(AnchorSet anchors)
        {
            foreach (var (key, members) in anchors.Left)
            {
                foreach (var item in members)
                {
                    item.Resolve().LeftAnchor = key;
                }
            }

            foreach (var (key, members) in anchors.Right)
            {
                foreach (var item in members)
                {
                    item.Resolve().RightAnchor = key;
                }
            }

            foreach (var (key, members) in anchors.Center)
            {
                foreach (var item in members)
                {
                    item.Resolve().CenterAnchor = key;
                }
            }

        }

        private static void ResolveSnaps(List<List<ProjectionItem>> lines, LineRange block, AnchorSet anchors)
        {
            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                foreach (var item in lines[lineIndex])
                {
                    var leftCount = item.LeftAnchor is int left && anchors.Left.TryGetValue(left, out var leftItems) ? leftItems.Count : 0;
                    var rightCount = item.RightAnchor is int right && anchors.Right.TryGetValue(right, out var rightItems) ? rightItems.Count : 0;
                    var centerCount = item.CenterAnchor is int center && anchors.Center.TryGetValue(center, out var centerItems) ? centerItems.Count : 0;
                    if (leftCount == 0 && rightCount == 0 && centerCount == 0)
                    {
                        continue;
                    }

                    var leftBiased = leftCount > 0 && rightCount > 0 && leftCount >= rightCount * 0.8;
                    item.Snap = (leftCount >= rightCount || leftBiased) && leftCount >= centerCount
                        ? SnapKind.Left
                        : rightCount >= leftCount && rightCount >= centerCount
                            ? SnapKind.Right
                            : SnapKind.Center;
                }
            }
        }

        private static void ApplyJustifiedTextFixups(
            List<List<ProjectionItem>> lines,
            LineRange block,
            AnchorSet anchors,
            float pageWidth,
            ProjectionDiagnostics diagnostics)
        {
            var rightInfo = new Dictionary<int, (int Total, int HasLeft, float MedianLeft)>();
            foreach (var (key, members) in anchors.Right)
            {
                var leftXs = members.Select(item => item.Resolve().Box.X).Where((_, index) => members[index].Resolve().LeftAnchor is not null).OrderBy(static x => x).ToArray();
                rightInfo[key] = (members.Count, leftXs.Length, leftXs.Length == 0 ? 0 : leftXs[leftXs.Length / 2]);
            }

            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                foreach (var item in lines[lineIndex])
                {
                    if (item.Snap == SnapKind.Right && item.LeftAnchor is null && item.RightAnchor is int rightKey &&
                        rightInfo.TryGetValue(rightKey, out var info) &&
                        info.Total >= 10 &&
                        (float)info.HasLeft / info.Total >= 0.9f &&
                        Math.Abs(item.Box.X - info.MedianLeft) <= pageWidth * 0.25f)
                    {
                        item.Snap = null;
                        item.RightAnchor = null;
                    }
                }
            }

            var pageMidKey = AnchorKey(pageWidth * 0.4f);
            var leftInfo = new Dictionary<int, (int Total, int RightSnapped)>();
            foreach (var (key, members) in anchors.Left)
            {
                leftInfo[key] = (members.Count, members.Count(item => item.Resolve().Snap == SnapKind.Right));
            }

            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                foreach (var item in lines[lineIndex])
                {
                    if (item.Snap == SnapKind.Left && item.RightAnchor is null && item.LeftAnchor is int leftKey &&
                        leftKey >= pageMidKey &&
                        leftInfo.TryGetValue(leftKey, out var info) &&
                        info.Total >= 4 &&
                        (float)info.RightSnapped / info.Total >= 0.5f)
                    {
                        item.Snap = null;
                        item.LeftAnchor = null;
                        item.ForceFloating = true;
                        diagnostics.ForceFloatingCount++;
                    }
                }
            }
        }

        private static void ComputeSpacingHints(List<List<ProjectionItem>> lines, LineRange block, float medianWidth, float pageWidth)
        {
            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                for (var itemIndex = 0; itemIndex < lines[lineIndex].Count; itemIndex++)
                {
                    var item = lines[lineIndex][itemIndex];
                    if (item.Rendered || itemIndex == 0 || lines[lineIndex][itemIndex - 1].Rendered)
                    {
                        item.ShouldSpace = 0;
                        continue;
                    }

                    var previous = lines[lineIndex][itemIndex - 1];
                    var xDelta = item.Box.X - previous.Box.Right;
                    var shouldSpace = 0;
                    if (xDelta > 2)
                    {
                        shouldSpace = 1;
                        var previousCharWidth = Math.Max(0.1f, previous.Box.Width / Math.Max(1, CharLength(previous.Text)));
                        if (xDelta > previousCharWidth * 2)
                        {
                            var sameColumn = xDelta < pageWidth * 0.1f &&
                                !(LineHasColumnGap(lines[lineIndex], medianWidth, pageWidth) && xDelta > medianWidth * 2);
                            var alignmentBreak = !item.ForceFloating && xDelta > previousCharWidth * 8 ||
                                item.Snap == SnapKind.Left ||
                                previous.Snap == SnapKind.Right ||
                                item.Snap is not null && previous.Snap is not null;
                            shouldSpace = alignmentBreak
                                ? sameColumn ? FloatingSpaces : ColumnSpaces
                                : sameColumn ? 1 : FloatingSpaces;
                        }
                    }

                    item.ShouldSpace = shouldSpace;
                }
            }
        }

        private static void RenderGridBlock(
            List<List<ProjectionItem>> lines,
            LineRange block,
            string[] rawLines,
            AnchorSet anchors,
            float medianWidth,
            SortedDictionary<int, int> forwardLeft,
            SortedDictionary<int, int> forwardRight,
            SortedDictionary<int, int> forwardCenter,
            SortedDictionary<int, int> forwardFloating)
        {
            var leftSnaps = anchors.Left.Keys.OrderBy(static key => key).ToList();
            var rightSnaps = anchors.Right.Keys.OrderBy(static key => key).ToList();
            var centerSnaps = anchors.Center.Keys.OrderBy(static key => key).ToList();
            var floatingSnaps = lines.Skip(block.Start).Take(block.End - block.Start)
                .SelectMany(static line => line)
                .Where(static item => item.Snap is null && !item.Rendered)
                .Select(static item => AnchorKey(item.Box.X))
                .Distinct()
                .OrderBy(static key => key)
                .ToList();

            var changed = true;
            while (changed || leftSnaps.Count > 0 || rightSnaps.Count > 0 || centerSnaps.Count > 0)
            {
                changed = false;
                for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
                {
                    for (var itemIndex = 0; itemIndex < lines[lineIndex].Count; itemIndex++)
                    {
                        var item = lines[lineIndex][itemIndex];
                        if (item.Rendered)
                        {
                            continue;
                        }

                        if (!item.ForceFloating)
                        {
                            if (item.Snap is not null)
                            {
                                continue;
                            }

                            var xKey = AnchorKey(item.Box.X);
                            var centerKey = AnchorKey(item.Box.X + item.Box.Width * 0.5f);
                            if (leftSnaps.FirstOrDefault(int.MaxValue) < xKey ||
                                rightSnaps.FirstOrDefault(int.MaxValue) < xKey ||
                                centerSnaps.FirstOrDefault(int.MaxValue) < centerKey)
                            {
                                continue;
                            }
                        }
                        else if (leftSnaps.Count > 0 || rightSnaps.Count > 0 || centerSnaps.Count > 0)
                        {
                            continue;
                        }

                        if (!CanRender(lines[lineIndex], itemIndex))
                        {
                            break;
                        }

                        var renderTarget = Math.Clamp((int)MathF.Round(item.Box.X / medianWidth), 0, ColumnSpaces);
                        var lastLeft = LastForwardValue(forwardLeft, AnchorKey(item.Box.X));
                        renderTarget = Math.Max(renderTarget, Math.Max(lastLeft, TrimEndLength(rawLines[lineIndex]) + item.ShouldSpace));
                        if (!item.ForceFloating && forwardFloating.TryGetValue(AnchorKey(item.Box.X), out var floatingTarget) && renderTarget < floatingTarget)
                        {
                            renderTarget = Math.Max(renderTarget, Math.Min(floatingTarget, renderTarget + 4));
                        }

                        AppendAt(rawLines, lineIndex, item, renderTarget);
                        changed = true;
                        UpdateForwardAnchors(leftSnaps, forwardLeft, AnchorKey(item.Box.Right), CharLength(rawLines[lineIndex]) + NextShouldSpace(lines[lineIndex], itemIndex));
                        UpdateForwardAnchors(rightSnaps, forwardRight, AnchorKey(item.Box.Right), CharLength(rawLines[lineIndex]) + NextShouldSpace(lines[lineIndex], itemIndex));
                        UpdateForwardAnchors(floatingSnaps, forwardFloating, AnchorKey(item.Box.Right), CharLength(rawLines[lineIndex]) + NextShouldSpace(lines[lineIndex], itemIndex));
                    }
                }

                var nextKind = NextSnapKind(leftSnaps, rightSnaps, centerSnaps);
                if (nextKind is null)
                {
                    continue;
                }

                var currentAnchor = nextKind switch
                {
                    SnapKind.Left => leftSnaps[0],
                    SnapKind.Right => rightSnaps[0],
                    _ => centerSnaps[0]
                };
                var turnItems = GetTurnItems(lines, block, nextKind.Value, currentAnchor).ToArray();
                if (turnItems.Length == 0)
                {
                    RemoveSnap(leftSnaps, rightSnaps, centerSnaps, nextKind.Value);
                    continue;
                }

                changed = true;
                var target = Math.Clamp((int)MathF.Round(AnchorToX(currentAnchor) / medianWidth), 0, ColumnSpaces);
                target = Math.Max(target, ComputeSnapLineMax(lines, rawLines, turnItems, nextKind.Value, forwardLeft));
                target = nextKind switch
                {
                    SnapKind.Left when forwardLeft.TryGetValue(currentAnchor, out var value) => Math.Max(target, value),
                    SnapKind.Right when forwardRight.TryGetValue(currentAnchor, out var value) => Math.Max(target, value),
                    SnapKind.Center when forwardCenter.TryGetValue(currentAnchor, out var value) => Math.Max(target, value),
                    _ => target
                };

                if (nextKind == SnapKind.Left)
                {
                    forwardLeft[currentAnchor] = target;
                }
                else if (nextKind == SnapKind.Right)
                {
                    forwardRight[currentAnchor] = target;
                }
                else
                {
                    forwardCenter[currentAnchor] = target;
                }

                foreach (var itemRef in turnItems)
                {
                    var item = lines[itemRef.LineIndex][itemRef.ItemIndex];
                    AppendSnapped(rawLines, itemRef.LineIndex, item, nextKind.Value, target);
                    UpdateForwardAnchors(leftSnaps, forwardLeft, AnchorKey(item.Box.Right), CharLength(rawLines[itemRef.LineIndex]) + NextShouldSpace(lines[itemRef.LineIndex], itemRef.ItemIndex));
                    UpdateForwardAnchors(rightSnaps, forwardRight, AnchorKey(item.Box.Right), CharLength(rawLines[itemRef.LineIndex]) + NextShouldSpace(lines[itemRef.LineIndex], itemRef.ItemIndex));
                    UpdateForwardAnchors(floatingSnaps, forwardFloating, AnchorKey(item.Box.Right), CharLength(rawLines[itemRef.LineIndex]) + NextShouldSpace(lines[itemRef.LineIndex], itemRef.ItemIndex));
                }

                RemoveSnap(leftSnaps, rightSnaps, centerSnaps, nextKind.Value);
            }

            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                for (var itemIndex = 0; itemIndex < lines[lineIndex].Count; itemIndex++)
                {
                    var item = lines[lineIndex][itemIndex];
                    if (item.Rendered)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(rawLines[lineIndex]) && !rawLines[lineIndex].EndsWith(" ", StringComparison.Ordinal))
                    {
                        rawLines[lineIndex] += " ";
                    }

                    item.ProjectedColumn = CharLength(rawLines[lineIndex]);
                    rawLines[lineIndex] += item.Text;
                    item.Rendered = true;
                }
            }
        }

        private static void RenderFlowingBlock(List<List<ProjectionItem>> lines, LineRange block, string[] rawLines, float medianWidth, ProjectionDiagnostics diagnostics)
        {
            var minX = lines.Skip(block.Start).Take(block.End - block.Start).Where(static line => line.Count > 0).Select(static line => line[0].Box.X).DefaultIfEmpty(0).Min();
            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                if (lines[lineIndex].Count == 0)
                {
                    continue;
                }

                rawLines[lineIndex] = RenderFlowingLine(lines[lineIndex], minX, medianWidth);
                diagnostics.FlowLineCount++;
            }
        }

        private static void DetectAndRenderFlowingLines(
            List<List<ProjectionItem>> lines,
            LineRange block,
            string[] rawLines,
            float medianWidth,
            float pageWidth,
            ProjectionDiagnostics diagnostics)
        {
            var threshold = medianWidth * FlowingColumnGapMultiplier;
            var blockMinX = lines.Skip(block.Start).Take(block.End - block.Start).Where(static line => line.Count > 0).Select(static line => line[0].Box.X).DefaultIfEmpty(0).Min();
            var flowing = new HashSet<int>();
            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                var line = lines[lineIndex];
                if (line.Count < FlowingMinLineItems)
                {
                    continue;
                }

                var span = line[^1].Box.Right - line[0].Box.X;
                if (!HasMixedSnaps(line) && span > pageWidth * FlowingWideLineRatio && LineMaxGap(line) < threshold && !LineHasColumnGap(line, medianWidth, pageWidth))
                {
                    flowing.Add(lineIndex);
                }
            }

            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                if (!flowing.Contains(lineIndex) &&
                    lineIndex > block.Start &&
                    flowing.Contains(lineIndex - 1) &&
                    lines[lineIndex].Count > 0 &&
                    !HasMixedSnaps(lines[lineIndex]) &&
                    LineMaxGap(lines[lineIndex]) < threshold &&
                    !LineHasColumnGap(lines[lineIndex], medianWidth, pageWidth))
                {
                    flowing.Add(lineIndex);
                }
            }

            for (var lineIndex = block.End - 1; lineIndex >= block.Start; lineIndex--)
            {
                if (!flowing.Contains(lineIndex) &&
                    lineIndex + 1 < block.End &&
                    flowing.Contains(lineIndex + 1) &&
                    lines[lineIndex].Count > 0 &&
                    !HasMixedSnaps(lines[lineIndex]) &&
                    LineMaxGap(lines[lineIndex]) < threshold &&
                    !LineHasColumnGap(lines[lineIndex], medianWidth, pageWidth))
                {
                    flowing.Add(lineIndex);
                }
            }

            foreach (var lineIndex in flowing)
            {
                rawLines[lineIndex] = RenderFlowingLine(lines[lineIndex], blockMinX, medianWidth);
                diagnostics.FlowLineCount++;
            }
        }

        private static string RenderFlowingLine(List<ProjectionItem> line, float minX, float medianWidth)
        {
            if (line.Count == 0)
            {
                return string.Empty;
            }

            var result = new StringBuilder();
            var indent = Math.Clamp((int)MathF.Round((line[0].Box.X - minX) / medianWidth), 0, FlowingMaxIndent);
            result.Append(' ', indent);
            for (var i = 0; i < line.Count; i++)
            {
                if (i > 0)
                {
                    var gap = line[i].Box.X - line[i - 1].Box.Right;
                    var threshold = Math.Max(FlowingSpaceMinThreshold, line[i].Box.Height * FlowingSpaceHeightRatio);
                    if (gap > threshold && (result.Length == 0 || result[^1] != ' '))
                    {
                        result.Append(' ');
                    }
                }

                line[i].ProjectedColumn = CharLength(result.ToString());
                result.Append(line[i].Text);
                line[i].Rendered = true;
            }

            return result.ToString();
        }

        private static bool IsFlowingTextBlock(
            List<List<ProjectionItem>> lines,
            LineRange block,
            AnchorSet anchors,
            float pageWidth,
            float medianWidth)
        {
            var totalAnchors = anchors.Left.Count + anchors.Right.Count + anchors.Center.Count;
            if (totalAnchors > FlowingMaxTotalAnchors || anchors.Left.Count > FlowingMaxLeftAnchors)
            {
                return false;
            }

            var nonEmpty = 0;
            var wide = 0;
            var columnGap = 0;
            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                var line = lines[lineIndex];
                if (line.Count == 0)
                {
                    continue;
                }

                nonEmpty++;
                if (line[^1].Box.Right - line[0].Box.X > pageWidth * FlowingWideLineRatio)
                {
                    wide++;
                }

                if (LineHasColumnGap(line, medianWidth, pageWidth))
                {
                    columnGap++;
                }
            }

            return nonEmpty >= FlowingMinLines &&
                columnGap < 2 &&
                (float)wide / nonEmpty > FlowingWideLineThreshold;
        }

        private static void AlignRotatedFloatingItems(
            List<List<ProjectionItem>> lines,
            IReadOnlyList<LineRange> blocks,
            string[] rawLines,
            float medianWidth)
        {
            foreach (var block in blocks)
            {
                for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
                {
                    var rotated = lines[lineIndex]
                        .Select((item, index) => (item, index))
                        .Where(static pair => pair.item.IsRotated && pair.item.Snap is null)
                        .Select(static pair => pair.index)
                        .ToArray();
                    if (rotated.Length == 0)
                    {
                        continue;
                    }

                    var first = lines[lineIndex][rotated[0]];
                    var bestColumn = FindBestNearbySnappedColumn(lines, block, lineIndex, first.Box.X, medianWidth);
                    if (bestColumn is not int target || target >= first.ProjectedColumn)
                    {
                        continue;
                    }

                    var shift = first.ProjectedColumn - target;
                    var rebuilt = TakeChars(rawLines[lineIndex], first.ProjectedColumn).TrimEnd();
                    foreach (var itemIndex in rotated)
                    {
                        var item = lines[lineIndex][itemIndex];
                        var newColumn = Math.Max(0, item.ProjectedColumn - shift);
                        rebuilt = PadTo(rebuilt, newColumn);
                        item.ProjectedColumn = CharLength(rebuilt);
                        rebuilt += item.Text;
                    }

                    rawLines[lineIndex] = rebuilt;
                }
            }
        }

        private static int? FindBestNearbySnappedColumn(List<List<ProjectionItem>> lines, LineRange block, int lineIndex, float x, float medianWidth)
        {
            int? bestColumn = null;
            var bestDiff = float.PositiveInfinity;
            var bestCenter = true;
            var low = Math.Max(block.Start, lineIndex - 4);
            var high = Math.Min(block.End, lineIndex + 5);
            for (var adjacent = low; adjacent < high; adjacent++)
            {
                if (adjacent == lineIndex)
                {
                    continue;
                }

                foreach (var item in lines[adjacent])
                {
                    if (item.Snap is null)
                    {
                        continue;
                    }

                    var isCenter = item.Snap == SnapKind.Center;
                    var diff = Math.Abs(item.Box.X - x);
                    if (diff < medianWidth * 3 && (!bestColumn.HasValue || (!isCenter && bestCenter) || isCenter == bestCenter && diff < bestDiff))
                    {
                        bestColumn = item.ProjectedColumn;
                        bestDiff = diff;
                        bestCenter = isCenter;
                    }
                }
            }

            return bestColumn;
        }

        private static void CleanProjectedItems(List<ProjectionItem> items, float pageWidth, ProjectionDiagnostics diagnostics)
        {
            var midpoint = pageWidth * 0.5f;
            var left = midpoint - 5;
            var right = midpoint + 20;
            var hasNonMarginByLine = items
                .Where(static item => !item.IsMarginLineNumber)
                .Select(static item => (int)MathF.Round(item.Box.Y))
                .ToHashSet();

            var before = items.Count;
            items.RemoveAll(item =>
            {
                var center = item.Box.X + item.Box.Width * 0.5f;
                var likelyMargin = item.IsMarginLineNumber ||
                    center > left && center < right && LooksLikeMarginLineNumber(item.Text) && item.Box.Width < 15;
                return likelyMargin && !hasNonMarginByLine.Contains((int)MathF.Round(item.Box.Y));
            });
            diagnostics.MarginLineNumbersRemoved += before - items.Count;
        }

        private static void FixSparseBlock(string[] rawLines, int start, int end, ProjectionDiagnostics diagnostics)
        {
            var total = 0;
            var whitespace = 0;
            for (var i = start; i < end; i++)
            {
                rawLines[i] = (rawLines[i] ?? string.Empty).TrimEnd();
                if (rawLines[i].Length == 0)
                {
                    continue;
                }

                total += rawLines[i].Length;
                whitespace += rawLines[i].Count(char.IsWhiteSpace);
            }

            if (total >= 500 && (float)whitespace / total > 0.8f)
            {
                for (var i = start; i < end; i++)
                {
                    rawLines[i] = CompressWideSpaces(rawLines[i] ?? string.Empty, ColumnSpaces, FloatingSpaces);
                }

                diagnostics.SparseBlockCompressionCount++;
            }
        }

        private static string CleanRenderedText(string text)
        {
            text = text.Replace('\0', ' ');
            var lines = text.Split('\n');
            int? minX = null;
            int? minY = null;
            int? maxY = null;
            for (var i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var leading = lines[i].Length - lines[i].TrimStart().Length;
                minX = minX.HasValue ? Math.Min(minX.Value, leading) : leading;
                minY ??= i;
                maxY = i;
            }

            if (minX is null || minY is null || maxY is null)
            {
                return string.Empty;
            }

            return string.Join('\n', lines[minY.Value..(maxY.Value + 1)].Select(line =>
                line.Length > minX.Value ? line[minX.Value..] : line.TrimEnd()));
        }

        private static void AddSortedLine(List<List<ProjectionItem>> lines, List<ProjectionItem> line)
        {
            if (line.Count == 0)
            {
                return;
            }

            line.Sort(static (a, b) => a.Box.X.CompareTo(b.Box.X));
            lines.Add(line);
        }

        private static (float Top, float Bottom, float Height) RepresentativeLineMetrics(List<ProjectionItem> line, float medianHeight)
        {
            if (line.Count == 0)
            {
                return (0, 0, 0);
            }

            var minRepresentative = medianHeight * 0.5f;
            var hasRepresentative = line.Any(item => item.Box.Height >= minRepresentative);
            var representative = hasRepresentative ? line.Where(item => item.Box.Height >= minRepresentative).ToArray() : line.ToArray();
            var top = representative.Min(static item => item.Box.Y);
            var bottom = representative.Max(static item => item.Box.Bottom);
            return (top, bottom, bottom - top);
        }

        private static bool LineHasColumnGap(List<ProjectionItem> line, float medianWidth, float pageWidth)
        {
            if (line.Count < 2)
            {
                return false;
            }

            var midpoint = pageWidth * 0.5f;
            for (var i = 1; i < line.Count; i++)
            {
                var previousEnd = line[i - 1].Box.Right;
                var currentStart = line[i].Box.X;
                if (currentStart - previousEnd > medianWidth * 2 && previousEnd < midpoint && currentStart > midpoint)
                {
                    return true;
                }
            }

            return false;
        }

        private static float LineMaxGap(List<ProjectionItem> line)
        {
            var max = 0f;
            for (var i = 1; i < line.Count; i++)
            {
                max = Math.Max(max, line[i].Box.X - line[i - 1].Box.Right);
            }

            return max;
        }

        private static bool HasMixedSnaps(List<ProjectionItem> line)
        {
            SnapKind? first = null;
            foreach (var item in line)
            {
                if (item.Snap is not SnapKind snap)
                {
                    continue;
                }

                first ??= snap;
                if (first != snap)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CanonicalRotation(float rotation)
        {
            var normalized = rotation % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            var best = 0f;
            var bestDelta = float.PositiveInfinity;
            foreach (var candidate in new[] { 0f, 90f, 180f, 270f })
            {
                var raw = Math.Abs(normalized - candidate);
                var delta = Math.Min(raw, 360 - raw);
                if (delta < bestDelta)
                {
                    best = candidate;
                    bestDelta = delta;
                }
            }

            return bestDelta <= 2 ? (int)best : (int)MathF.Round(normalized);
        }

        private static float Median(List<ProjectionItem> items, Func<ProjectionItem, float> selector)
        {
            var values = items.Select(selector).Where(static value => value > 0).OrderBy(static value => value).ToArray();
            if (values.Length == 0)
            {
                return 1;
            }

            var mid = values.Length / 2;
            return values.Length % 2 == 0 ? (values[mid - 1] + values[mid]) / 2 : values[mid];
        }

        private static List<ProjectionItem> Flatten(List<List<ProjectionItem>> lines) =>
            lines.SelectMany(static line => line).ToList();

        private static float FirstY(List<ProjectionItem> line) =>
            line.Count == 0 ? float.PositiveInfinity : line[0].Box.Y;

        private static int AnchorKey(float x) => (int)MathF.Round(x * 4);
        private static float AnchorToX(int key) => key / 4f;
        private static int CharLength(string? text) => text?.EnumerateRunes().Count() ?? 0;
        private static int TrimEndLength(string? text) => CharLength(text?.TrimEnd() ?? string.Empty);
        private static string PadTo(string line, int target) => target > CharLength(line) ? line + new string(' ', target - CharLength(line)) : line;
        private static int NextShouldSpace(List<ProjectionItem> line, int itemIndex) => itemIndex + 1 < line.Count ? line[itemIndex + 1].ShouldSpace : 0;
        private static string TakeChars(string text, int count) => new(text.EnumerateRunes().Take(count).SelectMany(static rune => rune.ToString()).ToArray());

        private static int LastForwardValue(SortedDictionary<int, int> values, int maxKey)
        {
            var result = 0;
            foreach (var (key, value) in values)
            {
                if (key > maxKey)
                {
                    break;
                }

                result = Math.Max(result, value);
            }

            return result;
        }

        private static bool CanRender(List<ProjectionItem> line, int itemIndex) =>
            line.Take(itemIndex).All(static item => item.Rendered);

        private static void AppendAt(string[] rawLines, int lineIndex, ProjectionItem item, int target)
        {
            var line = (rawLines[lineIndex] ?? string.Empty).TrimEnd();
            var before = CharLength(line);
            line = PadTo(line, target);
            item.ProjectedColumn = CharLength(line);
            item.NumSpaces = item.ProjectedColumn - before;
            rawLines[lineIndex] = line + item.Text;
            item.Rendered = true;
        }

        private static void AppendSnapped(string[] rawLines, int lineIndex, ProjectionItem item, SnapKind snap, int target)
        {
            var line = rawLines[lineIndex] ?? string.Empty;
            var before = CharLength(line);
            if (snap == SnapKind.Right)
            {
                line = line.TrimEnd();
                var textLength = CharLength(item.Text);
                line = PadTo(line, Math.Max(CharLength(line), target - textLength));
            }
            else if (snap == SnapKind.Center)
            {
                var half = CharLength(item.Text) / 2;
                line = PadTo(line, Math.Max(CharLength(line), target - half));
            }
            else
            {
                line = PadTo(line, target);
            }

            item.ProjectedColumn = CharLength(line);
            item.NumSpaces = item.ProjectedColumn - before;
            rawLines[lineIndex] = line + item.Text;
            item.Rendered = true;
        }

        private static int ComputeSnapLineMax(
            List<List<ProjectionItem>> lines,
            string[] rawLines,
            IReadOnlyList<ItemRef> turnItems,
            SnapKind kind,
            SortedDictionary<int, int> forwardLeft)
        {
            var max = 0;
            foreach (var itemRef in turnItems)
            {
                var item = lines[itemRef.LineIndex][itemRef.ItemIndex];
                var rawLine = rawLines[itemRef.LineIndex] ?? string.Empty;
                max = Math.Max(max, kind switch
                {
                    SnapKind.Left => CharLength(rawLine) + LineSpaceEnd(rawLine, item.ShouldSpace) + 1,
                    SnapKind.Right => Math.Max(LastForwardValue(forwardLeft, AnchorKey(item.Box.X)), TrimEndLength(rawLine) + item.ShouldSpace) + CharLength(item.Text),
                    _ => CharLength(rawLine) + CharLength(item.Text) / 2 + LineSpaceEnd(rawLine, item.ShouldSpace)
                });
            }

            return max;
        }

        private static int LineSpaceEnd(string rawLine, int shouldSpace)
        {
            var spaceEnd = rawLine.EndsWith(" ", StringComparison.Ordinal) ? 0 : shouldSpace;
            if (shouldSpace > 1)
            {
                var trailing = CharLength(rawLine) - TrimEndLength(rawLine);
                if (trailing < shouldSpace)
                {
                    spaceEnd = shouldSpace - trailing;
                }
            }

            return spaceEnd;
        }

        private static IEnumerable<ItemRef> GetTurnItems(List<List<ProjectionItem>> lines, LineRange block, SnapKind kind, int anchor)
        {
            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                for (var itemIndex = 0; itemIndex < lines[lineIndex].Count; itemIndex++)
                {
                    var item = lines[lineIndex][itemIndex];
                    if (item.Rendered)
                    {
                        continue;
                    }

                    var matches = kind switch
                    {
                        SnapKind.Left => item.LeftAnchor == anchor && item.Snap != SnapKind.Right && item.Snap != SnapKind.Center,
                        SnapKind.Right => item.RightAnchor == anchor && item.Snap == SnapKind.Right,
                        _ => item.CenterAnchor == anchor && item.Snap == SnapKind.Center
                    };
                    if (matches)
                    {
                        yield return new ItemRef(lineIndex, itemIndex, lines);
                    }
                }
            }
        }

        private static SnapKind? NextSnapKind(List<int> left, List<int> right, List<int> center)
        {
            var candidates = new List<(SnapKind Kind, int Key)>();
            if (left.Count > 0)
            {
                candidates.Add((SnapKind.Left, left[0]));
            }

            if (right.Count > 0)
            {
                candidates.Add((SnapKind.Right, right[0]));
            }

            if (center.Count > 0)
            {
                candidates.Add((SnapKind.Center, center[0]));
            }

            return candidates.OrderBy(static candidate => candidate.Key).Select(static candidate => (SnapKind?)candidate.Kind).FirstOrDefault();
        }

        private static void RemoveSnap(List<int> left, List<int> right, List<int> center, SnapKind kind)
        {
            var list = kind == SnapKind.Left ? left : kind == SnapKind.Right ? right : center;
            if (list.Count > 0)
            {
                list.RemoveAt(0);
            }
        }

        private static void UpdateForwardAnchors(List<int> snaps, SortedDictionary<int, int> forward, int rightBound, int target)
        {
            const int tolerance = 8;
            for (var i = snaps.Count - 1; i >= 0; i--)
            {
                var anchor = snaps[i];
                if (anchor < rightBound)
                {
                    return;
                }

                forward[anchor] = Math.Max(forward.GetValueOrDefault(anchor), target);
                for (var j = i - 1; j >= 0 && anchor - snaps[j] <= tolerance; j--)
                {
                    forward[snaps[j]] = Math.Max(forward.GetValueOrDefault(snaps[j]), target);
                }
            }
        }

        private static void MergeNearbyAnchorGroups(Dictionary<int, List<ItemRef>> anchors)
        {
            const int tolerance = 8;
            var keys = anchors.Keys.OrderBy(static key => key).ToArray();
            foreach (var key in keys)
            {
                if (!anchors.ContainsKey(key))
                {
                    continue;
                }

                foreach (var next in keys.Where(next => next > key && next - key <= tolerance).ToArray())
                {
                    if (!anchors.ContainsKey(next))
                    {
                        continue;
                    }

                    if (anchors[next].Count > anchors[key].Count)
                    {
                        anchors[next].AddRange(anchors[key]);
                        anchors.Remove(key);
                        break;
                    }

                    anchors[key].AddRange(anchors[next]);
                    anchors.Remove(next);
                }
            }
        }

        private static List<ItemRef> GetOrAdd(Dictionary<int, List<ItemRef>> dictionary, int key)
        {
            if (!dictionary.TryGetValue(key, out var value))
            {
                value = new List<ItemRef>();
                dictionary.Add(key, value);
            }

            return value;
        }

        private static void RemoveSingletons(Dictionary<int, List<ItemRef>> dictionary)
        {
            foreach (var key in dictionary.Keys.ToArray())
            {
                if (dictionary[key].Count < 2)
                {
                    dictionary.Remove(key);
                }
            }
        }

        private static void DeltaMinFilter(Dictionary<int, List<ItemRef>> anchors, List<List<ProjectionItem>> lines, float pageHeight, float delta)
        {
            var maxDelta = pageHeight * delta;
            foreach (var key in anchors.Keys.ToArray())
            {
                var members = anchors[key].OrderBy(item => lines[item.LineIndex][item.ItemIndex].Box.Y).ToArray();
                var keep = new bool[members.Length];
                for (var i = 0; i < members.Length; i++)
                {
                    var y = lines[members[i].LineIndex][members[i].ItemIndex].Box.Y;
                    if (i > 0 && y - lines[members[i - 1].LineIndex][members[i - 1].ItemIndex].Box.Y < maxDelta)
                    {
                        keep[i] = true;
                        keep[i - 1] = true;
                    }

                    if (i + 1 < members.Length && lines[members[i + 1].LineIndex][members[i + 1].ItemIndex].Box.Y - y < maxDelta)
                    {
                        keep[i] = true;
                    }
                }

                anchors[key] = members.Where((_, index) => keep[index]).ToList();
                if (anchors[key].Count == 0)
                {
                    anchors.Remove(key);
                }
            }
        }

        private static void InterceptFilter(Dictionary<int, List<ItemRef>> anchors, List<List<ProjectionItem>> lines)
        {
            foreach (var key in anchors.Keys.ToArray())
            {
                var members = anchors[key].OrderBy(item => lines[item.LineIndex][item.ItemIndex].Box.Y).ToArray();
                if (members.Length < 2)
                {
                    continue;
                }

                var anchorX = AnchorToX(key);
                var clearPair = false;
                for (var i = 1; i < members.Length; i++)
                {
                    var y1 = lines[members[i - 1].LineIndex][members[i - 1].ItemIndex].Box.Y;
                    var y2 = lines[members[i].LineIndex][members[i].ItemIndex].Box.Y;
                    var minY = Math.Min(y1, y2);
                    var maxY = Math.Max(y1, y2);
                    var intercepted = lines.Any(line =>
                        line.Count > 0 &&
                        line[0].Box.Y > minY &&
                        line[0].Box.Y < maxY &&
                        line.Any(item => item.Box.X < anchorX && item.Box.Right > anchorX));
                    if (!intercepted)
                    {
                        clearPair = true;
                        break;
                    }
                }

                if (!clearPair)
                {
                    anchors.Remove(key);
                }
            }
        }

        private static void AlignFloatingToNearbyAnchors(
            Dictionary<int, List<ItemRef>> anchors,
            List<List<ProjectionItem>> lines,
            LineRange block,
            float margin,
            Func<ProjectionItem, float> refX,
            Func<ProjectionItem, int> keyFor)
        {
            var anchored = anchors.Values.SelectMany(static members => members).ToHashSet();
            var additions = new List<(int Key, ItemRef Item)>();
            for (var lineIndex = block.Start; lineIndex < block.End; lineIndex++)
            {
                for (var itemIndex = 0; itemIndex < lines[lineIndex].Count; itemIndex++)
                {
                    var itemRef = new ItemRef(lineIndex, itemIndex, lines);
                    if (anchored.Contains(itemRef) || itemRef.Resolve().IsRotated)
                    {
                        continue;
                    }

                    int? bestKey = null;
                    var bestDiff = margin + 1;
                    foreach (var adjacent in new[] { lineIndex - 1, lineIndex + 1 })
                    {
                        if (adjacent < block.Start || adjacent >= block.End)
                        {
                            continue;
                        }

                        foreach (var adjacentItem in lines[adjacent])
                        {
                            var candidate = keyFor(adjacentItem);
                            if (!anchors.ContainsKey(candidate))
                            {
                                continue;
                            }

                            var diff = Math.Abs(AnchorToX(candidate) - refX(itemRef.Resolve()));
                            if (diff <= margin && diff < bestDiff)
                            {
                                bestDiff = diff;
                                bestKey = candidate;
                            }
                        }
                    }

                    if (bestKey is int key)
                    {
                        additions.Add((key, itemRef));
                    }
                }
            }

            foreach (var (key, item) in additions)
            {
                if (!anchors[key].Contains(item))
                {
                    anchors[key].Add(item);
                }
            }
        }

        private static string CompressWideSpaces(string line, int minRun, int replaceWith)
        {
            var result = new StringBuilder(line.Length);
            for (var i = 0; i < line.Length;)
            {
                if (line[i] != ' ')
                {
                    result.Append(line[i++]);
                    continue;
                }

                var start = i;
                while (i < line.Length && line[i] == ' ')
                {
                    i++;
                }

                result.Append(' ', i - start >= minRun ? replaceWith : i - start);
            }

            return result.ToString();
        }

        private enum SnapKind
        {
            Left,
            Right,
            Center
        }

        private readonly record struct LineRange(int Start, int End);

        private sealed class AnchorSet
        {
            public Dictionary<int, List<ItemRef>> Left { get; } = new();
            public Dictionary<int, List<ItemRef>> Right { get; } = new();
            public Dictionary<int, List<ItemRef>> Center { get; } = new();
        }

        private readonly record struct ItemRef(int LineIndex, int ItemIndex, List<List<ProjectionItem>> Lines)
        {
            public ProjectionItem Resolve() => Lines[LineIndex][ItemIndex];
        }

        private sealed class ProjectionDiagnostics
        {
            public int SourceItemCount { get; init; }
            public int FlowBlockCount { get; set; }
            public int GridBlockCount { get; set; }
            public int FlowLineCount { get; set; }
            public int LeftAnchorCount { get; set; }
            public int RightAnchorCount { get; set; }
            public int CenterAnchorCount { get; set; }
            public int RotatedItemCount { get; set; }
            public int MarginLineNumberCount { get; set; }
            public int MarginLineNumbersRemoved { get; set; }
            public int ForceFloatingCount { get; set; }
            public int SparseBlockCompressionCount { get; set; }
            public int DotNoiseRemovedCount { get; set; }
            public int ContinuousMergeCount { get; set; }
            public int WordMergeCount { get; set; }
        }

        private sealed class ProjectionItem
        {
            private readonly PdfTextItem _source;
            private readonly int _sourceIndex;

            public ProjectionItem(PdfTextItem source, int sourceIndex)
            {
                _source = source;
                _sourceIndex = sourceIndex;
                Text = source.Text;
                OriginalBox = source.BoundingBox;
                Box = source.BoundingBox;
                OriginalRotation = source.Rotation;
                Rotation = source.Rotation;
            }

            public string Text { get; private set; }
            public BoundingBox OriginalBox { get; private set; }
            public BoundingBox Box { get; set; }
            public float OriginalRotation { get; }
            public float Rotation { get; set; }
            public float DeferredY { get; set; }
            public bool IsRotated { get; set; }
            public bool IsMarginLineNumber { get; set; }
            public bool ForceFloating { get; set; }
            public bool Rendered { get; set; }
            public int ProjectedColumn { get; set; }
            public int NumSpaces { get; set; }
            public int ShouldSpace { get; set; }
            public int? LeftAnchor { get; set; }
            public int? RightAnchor { get; set; }
            public int? CenterAnchor { get; set; }
            public SnapKind? Snap { get; set; }

            public void Absorb(ProjectionItem item, bool insertSpace)
            {
                if (insertSpace && !Text.EndsWith(" ", StringComparison.Ordinal))
                {
                    Text += " ";
                }

                Text += item.Text;
                Box = Union(Box, item.Box);
                OriginalBox = Union(OriginalBox, item.OriginalBox);
            }

            public PdfTextItem ToPdfTextItem()
            {
                var metadata = new Dictionary<string, object?>(_source.Metadata)
                {
                    ["sourceIndex"] = _sourceIndex,
                    ["projectionBounds"] = Box,
                    ["projectedColumn"] = ProjectedColumn,
                    ["projectionSnap"] = Snap?.ToString(),
                    ["projectionLeftAnchor"] = LeftAnchor,
                    ["projectionRightAnchor"] = RightAnchor,
                    ["projectionCenterAnchor"] = CenterAnchor,
                    ["projectionSpaces"] = NumSpaces,
                    ["projectionForceFloating"] = ForceFloating,
                    ["projectionRotated"] = IsRotated,
                    ["projectionMarginLineNumber"] = IsMarginLineNumber
                };

                return new PdfTextItem
                {
                    Text = Text,
                    BoundingBox = OriginalBox,
                    Rotation = OriginalRotation,
                    Layer = _source.Layer,
                    Font = _source.Font,
                    TextWidth = _source.TextWidth,
                    HasUnicodeMapError = _source.HasUnicodeMapError,
                    Confidence = _source.Confidence,
                    MarkedContentId = _source.MarkedContentId,
                    RenderMode = _source.RenderMode,
                    FillColorArgb = _source.FillColorArgb,
                    StrokeColorArgb = _source.StrokeColorArgb,
                    Metadata = metadata
                };
            }

            private static BoundingBox Union(BoundingBox a, BoundingBox b)
            {
                var x = Math.Min(a.X, b.X);
                var y = Math.Min(a.Y, b.Y);
                var right = Math.Max(a.Right, b.Right);
                var bottom = Math.Max(a.Bottom, b.Bottom);
                return new BoundingBox(x, y, right - x, bottom - y);
            }
        }
    }
}
