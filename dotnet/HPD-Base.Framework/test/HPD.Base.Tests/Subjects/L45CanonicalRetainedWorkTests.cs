namespace HPD.Base.Tests.Subjects;

public sealed class L45CanonicalRetainedWorkTests
{
    [Fact]
    public void Primitive_encoding_has_exact_normative_sizes()
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer();                         // 8
        counter.AddSequence(2);                         // 8 + two references
        counter.AddInteger();                           // 8
        counter.AddBoolean();                           // 1
        counter.AddString("é");                         // 4 + 2 UTF-8 bytes
        counter.AddNullableString(null);                // absent tag
        counter.AddNullableString("é");                 // present tag + string
        counter.AddBytes(3);                            // length + bytes
        counter.AddNullableFixed16(false);              // absent tag
        counter.AddNullableFixed16(true);               // present tag + 16 bytes

        Assert.Equal(80, counter.Bytes);
        Assert.True(counter.Bytes <= 80);
        Assert.False(counter.Bytes <= 79);
    }

    [Fact]
    public void Primitive_encoding_uses_checked_arithmetic()
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.Add(long.MaxValue);
        Assert.Throws<OverflowException>(() => counter.AddInteger());
    }

    [Fact]
    public void Shared_overlay_encoding_includes_nullable_tags_and_eight_byte_integers()
    {
        var absent = new BasePreparedSubjectOverlayEvidence
        {
            ContractId = "c", ContractVersion = 1,
            SubjectId = BaseSubjectId.Create("s", BaseSubjectIdKind.OrdinalString),
            Exists = true, Incarnation = null, Active = null, Scope = null,
        };
        var present = absent with
        {
            Incarnation = new BaseSubjectIncarnation(Enumerable.Repeat((byte)1, 16).ToArray()),
            Active = true,
            Scope = "t",
        };

        Assert.Equal(30, BaseSubjectCanonicalRetainedWork.MeasureOverlay(absent));
        Assert.Equal(52, BaseSubjectCanonicalRetainedWork.MeasureOverlay(present));
    }
}
