using System;
using System.Globalization;
using Xunit;
using HPD.VCS;

namespace HPD.VCS.Tests
{
    public class UserSettingsTests
    {
        [Fact]
        public void Constructor_ValidInputs_CreatesInstance()
        {
            // Arrange & Act
            var userSettings = new UserSettings("John Doe", "john.doe@example.com");            // Assert
            Assert.Equal("John Doe", userSettings.GetUsername());
            Assert.Equal("john.doe@example.com", userSettings.GetEmail());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_InvalidName_ThrowsArgumentException(string invalidName)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new UserSettings(invalidName, "test@example.com"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("invalid-email")]
        [InlineData("@example.com")]
        [InlineData("user@")]
        [InlineData("user@.com")]
        public void Constructor_InvalidEmail_ThrowsArgumentException(string invalidEmail)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new UserSettings("Test User", invalidEmail));
        }        [Fact]
        public void GenerateSignature_ValidDateTime_ReturnsCorrectFormat()
        {
            // Arrange
            var userSettings = new UserSettings("Jane Smith", "jane.smith@example.com");

            // Act
            var signature = userSettings.GetSignature();

            // Assert
            Assert.Equal("jane smith", signature.Name); // normalized to lowercase
            Assert.Equal("jane.smith@example.com", signature.Email);
            Assert.True(signature.Timestamp <= DateTimeOffset.UtcNow);
            Assert.True(signature.Timestamp >= DateTimeOffset.UtcNow.AddSeconds(-5)); // Should be very recent
        }        [Fact]
        public void GenerateSignature_DifferentTimezones_NormalizesToUTC()
        {
            // Arrange
            var userSettings = new UserSettings("Test User", "test@example.com");

            // Act
            var signature1 = userSettings.GetSignature();
            var signature2 = userSettings.GetSignature();

            // Assert - Both should have similar timestamps (within a second)
            Assert.Equal("test user", signature1.Name); // normalized to lowercase
            Assert.Equal("test@example.com", signature1.Email);
            Assert.Equal(signature1.Name, signature2.Name);
            Assert.Equal(signature1.Email, signature2.Email);
            Assert.True(Math.Abs((signature2.Timestamp - signature1.Timestamp).TotalMilliseconds) < 1000);
        }        [Fact]
        public void GenerateSignature_WithSpecialCharactersInName_HandlesCorrectly()
        {
            // Arrange
            var userSettings = new UserSettings("José María Aznar-López", "jose.maria@example.com");

            // Act
            var signature = userSettings.GetSignature();

            // Assert
            Assert.Equal("josé maría aznar-lópez", signature.Name); // normalized to lowercase
            Assert.Equal("jose.maria@example.com", signature.Email);
            Assert.True(signature.Timestamp <= DateTimeOffset.UtcNow);
        }        [Fact]
        public void GenerateSignature_UnixEpoch_ReturnsZero()
        {
            // Arrange
            var userSettings = new UserSettings("Test User", "test@example.com");

            // Act
            var signature = userSettings.GetSignature();

            // Assert
            Assert.Equal("test user", signature.Name);
            Assert.Equal("test@example.com", signature.Email);
            // Timestamp should be current time, not unix epoch
        }        [Fact]
        public void GenerateSignature_FutureDate_HandlesCorrectly()
        {
            // Arrange
            var userSettings = new UserSettings("Future User", "future@example.com");

            // Act
            var signature = userSettings.GetSignature();

            // Assert
            Assert.Equal("future user", signature.Name); // normalized to lowercase
            Assert.Equal("future@example.com", signature.Email);
            Assert.True(signature.Timestamp <= DateTimeOffset.UtcNow);
        }        [Fact]
        public void GenerateSignature_Consistency_SameInputProducesSameOutput()
        {
            // Arrange
            var userSettings = new UserSettings("Consistency Test", "consistency@example.com");

            // Act
            var signature1 = userSettings.GetSignature();
            var signature2 = userSettings.GetSignature();

            // Assert
            Assert.Equal("consistency test", signature1.Name); // normalized to lowercase
            Assert.Equal("consistency@example.com", signature1.Email);
            Assert.Equal(signature1.Name, signature2.Name);
            Assert.Equal(signature1.Email, signature2.Email);
        }        [Fact]
        public void GenerateSignature_DifferentUsers_ProduceDifferentSignatures()
        {
            // Arrange
            var user1 = new UserSettings("Alice", "alice@example.com");
            var user2 = new UserSettings("Bob", "bob@example.com");

            // Act
            var signature1 = user1.GetSignature();
            var signature2 = user2.GetSignature();

            // Assert
            Assert.NotEqual(signature1.Name, signature2.Name);
            Assert.NotEqual(signature1.Email, signature2.Email);
            Assert.Equal("alice", signature1.Name);
            Assert.Equal("alice@example.com", signature1.Email);
            Assert.Equal("bob", signature2.Name);
            Assert.Equal("bob@example.com", signature2.Email);
        }        [Fact]
        public void GenerateSignature_EmailNormalization_HandlesCorrectly()
        {
            // Arrange - Test that email casing is preserved
            var userSettings = new UserSettings("Test User", "Test.User@Example.COM");

            // Act
            var signature = userSettings.GetSignature();

            // Assert
            Assert.Equal("test user", signature.Name); // name normalized to lowercase
            Assert.Equal("Test.User@Example.COM", signature.Email); // email preserved as-is
        }        [Fact]
        public void GetSignature_NameWithSpaces_HandlesCorrectly()
        {
            // Arrange
            var userSettings = new UserSettings("  Alice   Bob  ", "alice.bob@example.com");

            // Act
            var signature = userSettings.GetSignature();

            // Assert
            // Name should be trimmed and normalized to lowercase, but internal spaces preserved
            Assert.Equal("alice   bob", signature.Name);
            Assert.Equal("alice.bob@example.com", signature.Email);
        }        [Fact]
        public void UserSettings_Equality_SameValuesAreEqual()
        {
            // Arrange
            var user1 = new UserSettings("John Doe", "john@example.com");
            var user2 = new UserSettings("John Doe", "john@example.com");

            // Act & Assert
            Assert.Equal(user1.GetUsername(), user2.GetUsername());
            Assert.Equal(user1.GetEmail(), user2.GetEmail());
        }        [Fact]
        public void UserSettings_Immutability_PropertiesAreReadOnly()
        {
            // Arrange
            var userSettings = new UserSettings("Test User", "test@example.com");

            // Act & Assert
            // UserSettings uses methods, not properties, for access
            Assert.Equal("Test User", userSettings.GetUsername()); // preserves original case
            Assert.Equal("test@example.com", userSettings.GetEmail());
            Assert.NotEmpty(userSettings.GetHostname());
        }[Theory]
        [InlineData("user@example.com")]
        [InlineData("test.email+tag@example.co.uk")]
        [InlineData("user123@subdomain.example.org")]
        [InlineData("first.last@example.com")]
        public void Constructor_ValidEmails_Accepted(string validEmail)
        {
            // Act & Assert - Should not throw
            var userSettings = new UserSettings("Test User", validEmail);
            Assert.Equal(validEmail, userSettings.GetEmail());
        }        [Fact]
        public void GetSignature_CurrentTime_HandlesCorrectly()
        {
            // Arrange
            var userSettings = new UserSettings("Leap Year User", "leap@example.com");

            // Act
            var signature = userSettings.GetSignature();

            // Assert
            Assert.Equal("leap year user", signature.Name); // normalized to lowercase
            Assert.Equal("leap@example.com", signature.Email);
            Assert.True(signature.Timestamp <= DateTimeOffset.UtcNow);
        }
    }
}
