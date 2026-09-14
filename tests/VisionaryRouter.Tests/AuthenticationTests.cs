using VisionaryRouter.Server.Services;

namespace VisionaryRouter.Tests;

public class AuthenticationTests
{
    [Fact]
    public void Stored_password_hash_verifies_the_original_password()
    {
        var encoded = PasswordCredential.Hash("correct horse battery staple");

        Assert.True(PasswordCredential.Verify("correct horse battery staple", encoded));
        Assert.False(PasswordCredential.Verify("wrong password", encoded));
        Assert.DoesNotContain("correct horse battery staple", encoded, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-password-hash")]
    [InlineData("pbkdf2-sha256$abc$bad$bad")]
    public void Malformed_password_hashes_fail_closed(string encoded)
    {
        Assert.False(PasswordCredential.Verify("anything", encoded));
    }
}
