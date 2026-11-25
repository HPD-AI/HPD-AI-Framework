using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPDOS.ToolHarnesses.Middleware;
using System.Collections.Immutable;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class CodingHarnessConfigTests
{
    [Fact]
    public void Defaults_match_documented_edit_and_file_limits()
    {
        var config = new CodingHarnessConfig();

        config.ValidateSettings();
        config.EditFile.MaxEdits.Should().Be(20);
        config.EditFile.MaxNormalizationNotes.Should().Be(50);
        config.EditFile.MinRecoveryOldStringChars.Should().Be(10);
        config.EditFile.MaxRecoveryCandidates.Should().Be(20);
        config.EditFile.MaxReplacementCandidates.Should().Be(20_000);
        config.Files.MaxMutableFileBytes.Should().Be(50L * 1024 * 1024);
        config.Files.MaxEventSnapshotChars.Should().Be(500_000);
        config.Files.MaxEventTextEditChars.Should().Be(100_000);
        config.Files.MaxEventHunks.Should().Be(200);
        config.Files.DiffContextLines.Should().Be(3);
        config.Files.MaxEventNotes.Should().Be(50);
    }

    [Fact]
    public void Invalid_edit_and_file_limits_are_rejected()
    {
        var invalidEdit = new CodingHarnessConfig
        {
            EditFile = new EditFileConfig
            {
                MinRecoveryOldStringChars = 9
            }
        };
        var invalidFile = new CodingHarnessConfig
        {
            Files = new FileMutationConfig
            {
                MaxMutableFileBytes = 0
            }
        };

        var editError = FluentActions.Invoking(invalidEdit.ValidateSettings)
            .Should().Throw<AgentRunConfigurationException>().Subject.Single();
        var fileError = FluentActions.Invoking(invalidFile.ValidateSettings)
            .Should().Throw<AgentRunConfigurationException>().Subject.Single();

        editError.Code.Should().Be("CodingEditSettingsInvalid");
        fileError.Code.Should().Be("CodingFileMutationSettingsInvalid");
    }

    [Fact]
    public void Large_policy_values_are_not_rejected_by_arbitrary_product_caps()
    {
        var map = Enumerable.Range(0, 300).ToImmutableDictionary(
            i => $"token-{i}",
            i => $"replacement-{i}",
            StringComparer.Ordinal);
        var config = new CodingHarnessConfig
        {
            EditFile = new EditFileConfig
            {
                MaxEdits = 5_000,
                MaxNormalizationNotes = 20_000,
                MinRecoveryOldStringChars = 32,
                MaxRecoveryCandidates = 2_000,
                MaxReplacementCandidates = 2_000_000,
                Recovery = new EditRecoveryConfig
                {
                    DesanitizationMap = map
                }
            },
            Files = new FileMutationConfig
            {
                MaxMutableFileBytes = int.MaxValue,
                MaxEventSnapshotChars = 32_000_000,
                MaxEventTextEditChars = 32_000_000,
                MaxEventHunks = 20_000,
                DiffContextLines = 200,
                MaxEventNotes = 20_000
            }
        };

        config.ValidateSettings();
    }

    [Fact]
    public void Edit_settings_are_registered_for_source_generated_json()
    {
        CodingToolHarnessJsonContext.Default.Options
            .GetTypeInfo(typeof(CodingHarnessConfig))
            .Should().NotBeNull();
        CodingToolHarnessJsonContext.Default.Options
            .GetTypeInfo(typeof(EditFileConfig))
            .Should().NotBeNull();
        CodingToolHarnessJsonContext.Default.Options
            .GetTypeInfo(typeof(EditRecoveryConfig))
            .Should().NotBeNull();
        CodingToolHarnessJsonContext.Default.Options
            .GetTypeInfo(typeof(FileMutationConfig))
            .Should().NotBeNull();
    }

    [Fact]
    public void Edit_and_file_limits_are_run_overridable()
    {
        typeof(CodingHarnessConfig)
            .GetProperty(nameof(CodingHarnessConfig.EditFile))!
            .GetCustomAttributes(
                typeof(HarnessRunSettingAttribute),
                inherit: false)
            .Should().ContainSingle();
        typeof(CodingHarnessConfig)
            .GetProperty(nameof(CodingHarnessConfig.Files))!
            .GetCustomAttributes(
                typeof(HarnessRunSettingAttribute),
                inherit: false)
            .Should().ContainSingle();
    }
}
