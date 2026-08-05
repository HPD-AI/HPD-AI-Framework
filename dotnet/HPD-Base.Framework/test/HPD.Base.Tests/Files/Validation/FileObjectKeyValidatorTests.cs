namespace HPD.Base.Tests.Files.Validation;

public sealed class FileObjectKeyValidatorTests
{
    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("%2e%2e/secret.txt")]
    [InlineData("/absolute/file.txt")]
    [InlineData("C:/absolute/file.txt")]
    [InlineData("folder\\file.txt")]
    [InlineData("folder//file.txt")]
    [InlineData("bad\0file.txt")]
    public void RejectsUnsafeKeys(string key)
    {
        using var provider = new ServiceCollection().AddHPDBaseFiles().BuildServiceProvider();
        var validator = provider.GetRequiredService<IFileObjectKeyValidator>();

        validator.Normalize(key).Status.Should().Be(OperationStatus.ValidationFailed);
    }

    [Fact]
    public void NormalizesSafeKeys()
    {
        using var provider = new ServiceCollection().AddHPDBaseFiles().BuildServiceProvider();
        var validator = provider.GetRequiredService<IFileObjectKeyValidator>();

        var result = validator.Normalize(" images / avatars / user.png ");

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value.Value.Should().Be("images/avatars/user.png");
    }
}
