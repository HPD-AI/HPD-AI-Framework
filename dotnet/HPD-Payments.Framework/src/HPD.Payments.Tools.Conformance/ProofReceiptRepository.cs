namespace HPD.Payments.Tools.Conformance;

/// <summary>Loads an unordered content-addressed receipt directory into one exact append-only chain.</summary>
internal static class ProofReceiptRepository
{
    /// <summary>Inventories, decodes, content-verifies, and orders every retained receipt.</summary>
    internal static IReadOnlyList<ProofReceipt> LoadChain(string rootDirectory)
    {
        var inventory = ProofArtifactInventory.Capture(rootDirectory);
        if (!inventory.IsClean) throw new InvalidDataException("Proof root cleanup inventory is inadmissible.");
        var byAddress = new Dictionary<string, ProofReceipt>(StringComparer.Ordinal);
        foreach (var entry in inventory.Entries)
        {
            var separator = entry.LastIndexOf('|');
            if (separator <= 0) throw new InvalidDataException("Proof inventory entry is malformed.");
            var relative = entry[..separator];
            var path = Path.Combine(Path.GetFullPath(rootDirectory), relative.Replace('/', Path.DirectorySeparatorChar));
            var canonical = File.ReadAllText(path);
            var receipt = ProofReceiptCodec.Parse(canonical);
            var address = receipt.ContentAddress();
            var expectedRelative = $"{address[..2]}/{address}.receipt";
            if (!StringComparer.Ordinal.Equals(relative, expectedRelative) || !byAddress.TryAdd(address, receipt))
                throw new InvalidDataException("Receipt filename, content address, or uniqueness is invalid.");
        }
        if (byAddress.Count == 0) return Array.Empty<ProofReceipt>();
        var genesis = byAddress.Values.Where(static x => x.PredecessorDigest == "GENESIS").ToArray();
        if (genesis.Length != 1) throw new InvalidDataException("Receipt chain requires exactly one genesis.");
        var successor = new Dictionary<string, ProofReceipt>(StringComparer.Ordinal);
        foreach (var receipt in byAddress.Values.Where(static x => x.PredecessorDigest != "GENESIS"))
            if (!successor.TryAdd(receipt.PredecessorDigest, receipt))
                throw new InvalidDataException("Receipt chain forks at one predecessor.");
        var ordered = new List<ProofReceipt>(byAddress.Count) { genesis[0] };
        var addressCursor = genesis[0].ContentAddress();
        while (successor.Remove(addressCursor, out var next))
        {
            ordered.Add(next); addressCursor = next.ContentAddress();
        }
        if (ordered.Count != byAddress.Count || successor.Count != 0)
            throw new InvalidDataException("Receipt chain contains an orphan, cycle, or missing predecessor.");
        return ordered.AsReadOnly();
    }
}
