using HPD.Base.Results;
using HPD.Base.Stores;

namespace HPD.Base.Runtime.Tests;

internal sealed class FakeRevisionedRecordStore : FakeRecordStore
{
    public FakeRevisionedRecordStore(string storeId)
        : base(
            storeId,
            revision: new RevisionCapability
            {
                Supported = true,
                Guarantee = RevisionGuarantee.Store,
                Patch = true,
                Replace = true
            })
    {
    }
}
